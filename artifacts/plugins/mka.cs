// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Canger.Core.Input;
using Canger.Core.Processes;
using Canger.Core.Tasks;

// Background audio, for listening to an audiobook while working.
//
// Enter on a `.mka` starts mpv without a window and without taking the terminal, so the browser
// stays usable; `pap` pauses and resumes; `pas` stops. The status bar shows mpv's own progress
// line in the gap between the file details and the free space, and tints itself as it goes — and
// gives way to a copy or an archive for as long as one is running.
//
// Three things about mpv were measured rather than assumed, because each of them decides the
// design:
//
//   1. `--term-status-msg` is written to standard output even when that is a pipe, about eight
//      times a second. So progress needs no socket and no polling of our own: the text mpv would
//      have printed to a terminal is there to be read. (7-zip, by contrast, writes its per-file
//      announcements only when the job ends and suppresses its percentage display entirely off a
//      terminal, which is why archives get no bar. Seeing that a program *can* emit something is
//      not evidence that it emits it while there is still something to report.)
//   2. Pausing needs the JSON IPC socket. mpv takes no signal for it, and `--input-file` is gone.
//   3. **The socket path must be short.** A unix socket address is about 108 bytes, and mpv says
//      only "Could not create IPC socket" when it is longer — so the socket goes in
//      `$XDG_RUNTIME_DIR`, which is `/run/user/1000` and leaves plenty of room. A path under a
//      long temporary directory silently gave no control channel at all.

/// <summary>Formats and extensions this plugin plays.</summary>
internal static class Playable
{
    /// <summary>
    /// The extensions Enter should hand to mpv rather than to rifle.
    /// </summary>
    /// <remarks>
    /// Matroska audio is what this was asked for. The others are here because they are the same
    /// kind of thing — a long single file listened to over days — and because a rule that covers
    /// one and not its neighbours is a rule nobody can remember.
    /// </remarks>
    internal static readonly string[] Extensions =
        [".mka", ".opus", ".m4b", ".m4a", ".flac", ".ogg", ".oga", ".mp3", ".wav"];

    /// <summary>Whether Enter on this name should start playback.</summary>
    /// <param name="name">The file's name.</param>
    /// <returns>Whether it is one of the audio formats.</returns>
    internal static bool Matches(string name) =>
        Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// How long each file lasts, asked of a tool that can say.
/// </summary>
/// <remarks>
/// <para>
/// A queue's total cannot come from mpv: it reports on the file it is playing and knows nothing
/// about the length of the ones after it until it reaches them, so a bar that waited for mpv
/// would only learn the total as it finished. The files are therefore measured before playback
/// starts, which is the only moment the answer is knowable in advance.
/// </para>
/// <para>
/// <c>ffprobe</c> first, <c>mediainfo</c> second, because between them they cover every machine
/// this has been run on and neither is worth requiring. With neither, or with any one file
/// unmeasurable, there is no honest total — and the status line falls back to the file playing
/// now rather than inventing one.
/// </para>
/// </remarks>
internal static class Durations
{
    /// <summary>Seconds, as ffprobe prints them.</summary>
    /// <param name="output">What ffprobe wrote.</param>
    /// <returns>The duration, or <see langword="null"/> where the answer was not a number.</returns>
    /// <remarks>
    /// <c>N/A</c> is ffprobe's answer for a stream it cannot measure, and the empty string is
    /// what a missing file gives. Both have to read as "no answer" rather than as zero, or a
    /// silent file would shorten the total everything else is measured against.
    /// </remarks>
    internal static double? Seconds(string output) =>
        double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double seconds) && seconds > 0
            ? seconds
            : null;

    /// <summary>Seconds, from the milliseconds mediainfo prints.</summary>
    /// <param name="output">What mediainfo wrote.</param>
    /// <returns>The duration, or <see langword="null"/> where the answer was not a number.</returns>
    internal static double? Milliseconds(string output) =>
        Seconds(output) is { } milliseconds ? milliseconds / 1000 : null;

    /// <summary>Measures every file, or reports that it could not.</summary>
    /// <param name="paths">The files, in the order they will play.</param>
    /// <returns>
    /// One duration per file, or <see langword="null"/> when any of them could not be measured.
    /// </returns>
    /// <remarks>
    /// All at once, because the answers are independent and a queue of forty parts measured one
    /// after another would keep the total off the screen for as long as it took. Each probe is
    /// its own short-lived process; the whole thing runs off the interface's thread.
    /// </remarks>
    internal static double[]? Of(IReadOnlyList<string> paths)
    {
        double?[] measured = new double?[paths.Count];

        Parallel.For(0, paths.Count, i => measured[i] = Measure(paths[i]));

        return measured.All(duration => duration is not null)
            ? [.. measured.Select(duration => duration!.Value)]
            : null;
    }

    /// <summary>Asks each tool in turn for one file's length.</summary>
    private static double? Measure(string path) =>
        Seconds(Ask("ffprobe",
                    ["-v", "error", "-show_entries", "format=duration",
                     "-of", "default=nw=1:nk=1", path]))
        ?? Milliseconds(Ask("mediainfo", ["--Output=General;%Duration%", path]));

    /// <summary>Runs a program and returns what it printed.</summary>
    /// <remarks>
    /// Started directly rather than through the file manager's runner: that runner is for
    /// programs the user asked for — it can suspend the interface, take the terminal, or queue
    /// the work — and none of that belongs to a measurement nobody asked for. The arguments go
    /// through <see cref="ProcessStartInfo.ArgumentList"/>, so a filename containing a quote or a
    /// space reaches the tool exactly as it is spelled and no shell ever sees it.
    /// </remarks>
    private static string Ask(string program, IReadOnlyList<string> arguments)
    {
        try
        {
            ProcessStartInfo start = new(program)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);

            if (process is null)
            {
                return string.Empty;
            }

            string output = process.StandardOutput.ReadToEnd();

            // Bounded, because a measurement that hangs would hang the queue's total behind it.
            // The file plays either way; only the total waits on this.
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                return string.Empty;
            }

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            // The tool is not installed, or cannot be run here. The other one is tried next, and
            // if it is not there either the queue simply has no total.
            return string.Empty;
        }
    }
}

/// <summary>
/// The one thing playing, and what it has to say for the status bar.
/// </summary>
/// <remarks>
/// <para>
/// One at a time on purpose: two audiobooks at once is not a feature, and a single slot means
/// <c>pap</c> and <c>pas</c> never have to ask which. Starting a second file replaces the first.
/// </para>
/// <para>
/// <see cref="Describe"/> is called by the status bar while it draws, which is the only heartbeat
/// a plugin gets, so the reading of mpv's output happens there. It costs a string comparison over
/// what mpv has written; nothing is parsed twice, because only the last line matters.
/// </para>
/// </remarks>
internal sealed class Playback : IBackgroundActivity, IKeyGrab
{
    /// <summary>What mpv is told to print, and what this looks for.</summary>
    private const string Marker = "canger-mka:";

    /// <summary>
    /// The status line mpv is asked for: a machine-readable percentage, then the line as a person
    /// would read it.
    /// </summary>
    /// <remarks>
    /// mpv does the formatting — <c>${time-pos}</c> and <c>${duration}</c> are already
    /// <c>HH:MM:SS</c> and <c>${percent-pos}</c> is already rounded — so there is no arithmetic
    /// here to disagree with what the user sees when they run mpv themselves. <c>${=percent-pos}</c>
    /// is the same figure unrounded, which is what the bar is tinted from.
    /// </remarks>
    private const string DefaultStatusFormat =
        "${time-pos} / ${duration} (${percent-pos}%) ${speed}x";

    /// <summary>
    /// What mpv is asked to print: the marker, a machine-readable percentage, then the line as a
    /// person reads it.
    /// </summary>
    /// <param name="display">The format for the readable half.</param>
    /// <returns>The whole format to hand to <c>--term-status-msg</c>.</returns>
    /// <remarks>
    /// Only the first two fields are Canger's. Everything after the bar is whatever format is in
    /// force — the user's own <c>term-status-msg</c> where they have set one — so what the status
    /// bar shows is what mpv would have shown them, down to the field order and the wording.
    /// <c>${=percent-pos}</c> is the same figure unrounded, and is what the bar is tinted from;
    /// it has to be asked for separately because a format written for a person need not contain a
    /// percentage at all. The three fields after it — where the playlist has got to, how far into
    /// that file, and the speed — are what a queue's own clock is worked out from, and are asked
    /// for on every playback because asking only for a queue would mean two formats to keep in
    /// step. The speed is asked for in mpv's display spelling rather than raw, since nothing here
    /// does arithmetic with it and <c>${=speed}</c> reads as <c>1.500000x</c> on the bar.
    /// </para>
    /// <para>
    /// After those come the parts of the user's own format that only mpv can answer — a title, a
    /// volume, a <c>${?pause==yes:…}</c> — each asked for separately so that a queue's line can
    /// be built from the user's format with the queue's own figures in place of the file's. They
    /// are rendered by mpv and spliced back in; nothing here tries to interpret what they mean.
    /// </remarks>
    private static string StatusFormat(string display) =>
        Marker + "${=percent-pos};${playlist-pos};${=time-pos};${speed};${volume}" +
        ";${?pause==yes:1}${!pause==yes:0}" +
        string.Concat(Passthrough(display).Select(expression => ";" + expression)) +
        "|" + display;

    /// <summary>What is shown in front of the clock while playback is held.</summary>
    /// <remarks>
    /// Added by this side rather than relying on the user's format carrying
    /// <c>${?pause==yes:…}</c>, since most do not. Whether mpv is paused is asked of mpv all the
    /// same: it writes two or three status lines as it pauses and then stops, so the last line to
    /// arrive is the one that says so. An earlier comment here claimed it wrote nothing further,
    /// which is what led to this side keeping the state itself — measured, and wrong.
    ///
    /// Left off when the line already says it, so a format carrying its own
    /// <c>${?pause==yes:(Paused)}</c> does not end up saying it twice on the occasions mpv does
    /// manage to report it.
    /// </remarks>
    private const string PausedPrefix = "(paused) ";

    private readonly IFileManager _fileManager;

    private IBackgroundProcess? _process;
    private string _socket = string.Empty;
    private string? _text;
    private double? _progress;
    private bool _paused;
    private bool _handsOver;
    private string _display = DefaultStatusFormat;

    /// <summary>The files handed to mpv, in the order they play.</summary>
    private string[] _files = [];

    /// <summary>
    /// How long each of them lasts, or <see langword="null"/> while that is not known.
    /// </summary>
    /// <remarks>
    /// Written by the thread that measures them and read by the one that draws, so it is written
    /// as one reference rather than filled in place: a reader sees either no answer or the whole
    /// answer, never half of one.
    /// </remarks>
    private volatile double[]? _durations;

    /// <summary>Which file of the queue mpv says it is playing, counted from zero.</summary>
    private int _index;

    /// <summary>How far into that file it has got, in seconds.</summary>
    private double _position;

    /// <summary>The speed mpv reports, as it spells it.</summary>
    private string _speed = "1";

    /// <summary>The volume mpv last reported, or empty before it has said.</summary>
    private string _volume = string.Empty;

    /// <summary>When that figure last changed.</summary>
    /// <remarks>
    /// The volume is shown because it moved, not because a mode is on — which is how mpv behaves
    /// in a terminal, where the figure appears as it is changed and then goes away again. Watching
    /// the figure rather than the keys means it shows however it was changed: <c>8</c> and
    /// <c>9</c>, mute, or something else on the machine moving it.
    /// </remarks>
    private DateTimeOffset _volumeMoved = DateTimeOffset.MinValue;

    /// <summary>How long the volume stays on the line after it moves.</summary>
    /// <remarks>mpv's own <c>osd-duration</c> default, so the two behave alike.</remarks>
    private static readonly TimeSpan VolumeWindow = TimeSpan.FromSeconds(1);

    /// <summary>What mpv made of the parts of the format only it can answer.</summary>
    private string[] _rendered = [];

    /// <summary>Those parts, in the order they are asked for and read back.</summary>
    private string[] _passthrough = [];

    /// <summary>
    /// The format mpv is filling in, which is the user's own until the mode extends it.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="_display"/>, which stays the format as the user wrote it: the
    /// mode is built by extending that one and leaving it is what makes leaving the mode put the
    /// line back.
    /// </remarks>
    private string _effective = DefaultStatusFormat;

    /// <summary>The format used where mpv's configuration names none.</summary>
    internal static string PlainStatusFormat => DefaultStatusFormat;

    /// <summary>Prepares the slot.</summary>
    /// <param name="fileManager">Used to start mpv and to say what happened.</param>
    internal Playback(IFileManager fileManager) => _fileManager = fileManager;

    /// <summary>Whether something is playing or paused.</summary>
    internal bool IsActive => _process is { HasExited: false };

    /// <inheritdoc />
    /// <remarks>
    /// The whole queue's, where the queue has been measured: a bar that ran to full and started
    /// again at each part would say nothing about how much listening is left, which is the one
    /// thing a queue's bar is for.
    /// </remarks>
    public double? Progress
    {
        get
        {
            if (_files.Length > 1 && _durations is { Length: > 1 } durations)
            {
                double total = durations.Sum();

                return total > 0
                    ? Math.Clamp(Elapsed(durations, _index, _position) / total, 0, 1)
                    : _progress;
            }

            return _progress;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only while the keyboard belongs to mpv, because that is the state worth announcing: every
    /// key does something different until it is handed back, and the way back is Escape.
    /// </remarks>
    public string? Badge => _handsOver ? "MPV" : null;

    /// <summary>Whether the keyboard has been handed to mpv.</summary>
    internal bool HandsOver => _handsOver;

    /// <inheritdoc />
    public string? Describe()
    {
        if (_process is not { } process)
        {
            return null;
        }

        // Read before the exit is acted on, so the last thing mpv said is not lost when it ends
        // between two frames.
        Read(process.StandardOutput);

        if (process.HasExited)
        {
            Forget();
            return null;
        }

        if (_text is null)
        {
            return null;
        }

        // The queue's own clock where there is one, and mpv's line for the file playing now
        // where there is not — which covers a single file, a queue still being measured, and one
        // holding something no tool here could measure.
        string text = _files.Length > 1 && _durations is { Length: > 1 } durations
            ? OverallLine(_effective, _rendered, durations, _index, _position, Rate(_speed))
            : _text;

        // mpv's answer where there is one; what its last reading said otherwise, which is all
        // there is when no control socket came up.
        _paused = AskPause() ?? _paused;

        text += VolumeSuffix(_effective, _volume,
                             DateTimeOffset.UtcNow - _volumeMoved < VolumeWindow);

        return _paused && !text.Contains("paused", StringComparison.OrdinalIgnoreCase)
            ? PausedPrefix + text
            : text;
    }

    /// <summary>
    /// Hands the keyboard to mpv, or takes it back.
    /// </summary>
    /// <remarks>
    /// mpv's own keys are the point: <c>[</c> and <c>]</c> for speed, <c>8</c> and <c>9</c> for
    /// volume, the arrows for seeking. Every one of them means something to the browser, so they
    /// cannot simply be forwarded all the time — the user says when.
    /// </remarks>
    internal void ToggleKeyboard()
    {
        if (!IsActive)
        {
            _fileManager.Notify("mka: nothing is playing", isError: true);
            return;
        }

        _handsOver = !_handsOver;
        _fileManager.KeyGrab = _handsOver ? this : null;

        ShowWhatTheModeIsFor();
        _fileManager.Redraw();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Escape is the way out and is never forwarded, so the keyboard cannot be lost: whatever mpv
    /// would do with it matters less than always being able to take the keys back.
    /// </para>
    /// <para>
    /// Everything else goes to mpv by name, through its <c>keypress</c> command, so what a key
    /// does is whatever the user's own mpv configuration says it does — measured: two <c>]</c>
    /// took the speed from 1.5 to 1.815, and three <c>9</c> took the volume from 100 to 94, which
    /// are mpv's own bindings and step sizes rather than anything reimplemented here.
    /// </para>
    /// <para>
    /// A key mpv has no name for is swallowed rather than passed to the browser. Handing half the
    /// keyboard back while the bar still says MPV would be worse than doing nothing with it.
    /// </para>
    /// </remarks>
    public bool Handle(int key)
    {
        if (!_handsOver)
        {
            return false;
        }

        if (key == KeyCodes.Escape || !IsActive)
        {
            _handsOver = false;
            _fileManager.KeyGrab = null;
            ShowWhatTheModeIsFor();

            return key == KeyCodes.Escape;
        }

        if (MpvKeys.NameOf(key) is { } name)
        {
            Send($$"""{"command":["keypress","{{name}}"]}""");
        }

        return true;
    }

    /// <summary>Starts playing files, replacing whatever was playing.</summary>
    /// <param name="paths">The files, in the order they should play.</param>
    /// <remarks>
    /// Handed to one mpv as a playlist rather than started one after another, so that a queue
    /// behaves as a queue: <c>pap</c> holds all of it, <c>pas</c> stops all of it, the mode hands
    /// the keyboard to the thing actually playing, and the gap between two parts is mpv's own
    /// rather than the time it takes Canger to notice one has ended.
    /// </remarks>
    internal void Start(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        Stop();

        // In the runtime directory, and short, because a unix socket address is about 108 bytes
        // and mpv reports nothing useful when it is longer. Named for the process so that a second
        // Canger does not take the first one's control channel.
        string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp";
        _socket = Path.Join(runtime, $"canger-mka-{Environment.ProcessId}.sock");

        try
        {
            File.Delete(_socket);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover socket from a killed session. mpv will fail to bind and say so, and
            // playback goes on without a control channel rather than not at all.
        }

        // --no-video so no window is asked for; --input-terminal=no so mpv never reads the
        // keyboard, which the browser owns; --idle=no so it exits at the end of the file rather
        // than sitting there. The user's own mpv.conf is still read, which is where the 1.5x
        // speed in the status line comes from.
        //
        // Not `--no-terminal`, and no `--msg-level`, though both would be the obvious way to
        // quieten it: measured, either one silences `--term-status-msg` as well, and the status
        // line is the whole point. `--no-terminal` gave nothing at all where
        // `--input-terminal=no` gives sixteen readings in two seconds, and every
        // `--msg-level=all=…` value tried — no, error, warn — gave nothing, because the status
        // message rides that same log level. What is left is three lines of start-up chatter on
        // standard output, which nothing looks at.
        _display = MpvConfiguration.StatusFormat();
        UseFormat(_display);

        string command =
            $"mpv --no-video --idle=no --input-terminal=no " +
            $"--input-ipc-server={Quote(_socket)} " +
            $"--term-status-msg={Quote(StatusFormat(_display))} " +
            string.Join(' ', paths.Select(Quote));

        _process = _fileManager.Runner.StartInBackground(new ProcessRequest(command));

        if (_process is null)
        {
            _fileManager.Notify("mka: could not start mpv", isError: true);
            return;
        }

        _text = null;
        _progress = null;
        _paused = false;
        _files = [.. paths];
        _durations = null;
        _index = 0;
        _position = 0;
        _speed = "1";
        _volume = string.Empty;
        _volumeMoved = DateTimeOffset.MinValue;

        // Measured off the interface's thread, because ffprobe on forty files takes longer than a
        // frame and the audio should not wait on arithmetic. Until the answer lands the status
        // line is the one mpv writes for the file playing now, which is the same line a single
        // file gets — so the queue reads correctly from the first frame and gains its total when
        // there is one to give.
        if (paths.Count > 1)
        {
            string[] measure = [.. paths];

            Task.Run(() =>
            {
                double[]? durations = Durations.Of(measure);

                // Only a real answer, and only if this is still the playback that asked. A
                // failed measurement leaves what is there rather than replacing it with nothing,
                // and a second `Enter` while the first measurement was in flight must not land
                // the old queue's totals on the new one.
                if (durations is not null && _files.SequenceEqual(measure))
                {
                    _durations = durations;
                    _fileManager.Redraw();
                }
            });
        }

        // The keyboard goes back with the playback it belonged to. Without this, a file reaching
        // its end would leave the keys pointed at a program that is no longer there.
        if (_handsOver)
        {
            _handsOver = false;
            _fileManager.KeyGrab = null;
        }

        // Said with the status line rather than with a message. A message outranks the activity
        // it is announcing, so "playing OSHO.mka" sat over the very clock it was telling the user
        // about until something else displaced it. Starting to play is visible the moment mpv
        // reports, which is well under a second; failing to start still says so.
        _fileManager.BackgroundActivity = this;
    }

    /// <summary>Pauses if playing, resumes if paused.</summary>
    internal void TogglePause()
    {
        if (!IsActive)
        {
            _fileManager.Notify("mka: nothing is playing", isError: true);
            return;
        }

        if (!Send("""{"command":["cycle","pause"]}"""))
        {
            _fileManager.Notify("mka: mpv is not answering its control socket", isError: true);
            return;
        }

        // A guess, and only until mpv answers: it writes a status line or two as it pauses, and
        // whatever they say is what the bar shows. Kept because the answer takes a moment to
        // arrive and the key should feel immediate.
        _paused = !_paused;

        // Asked for explicitly, because a paused mpv stops writing after those few lines and so
        // stops waking the loop that redraws. Without this the word appears whenever something
        // else happens to cause a frame, which may be minutes later.
        _fileManager.Redraw();
    }

    /// <summary>Stops playback and clears the status line.</summary>
    internal void Stop()
    {
        if (_process is not { } process)
        {
            return;
        }

        // Asked first, so mpv can put the audio device down tidily; killed only if it will not.
        if (!process.HasExited)
        {
            Send("""{"command":["quit"]}""");

            if (!process.WaitForExit(TimeSpan.FromMilliseconds(300)))
            {
                process.Kill();
            }
        }

        Forget();
    }

    /// <summary>Reads the last thing mpv said, and keeps it.</summary>
    /// <param name="output">Everything mpv has written so far.</param>
    /// <remarks>
    /// <para>
    /// The last occurrence, not the first: mpv rewrites its status line about eight times a
    /// second and every one of them is in this string.
    /// </para>
    /// <para>
    /// The end of the string counts as the end of a line, which matters more than it sounds.
    /// mpv separates its updates with a carriage return, so the newest one has no terminator
    /// until the one after it arrives — and while playback is paused there is no one after it.
    /// Waiting for a terminator therefore showed the reading from just *before* the pause and
    /// held it: the clock froze at the right second but never admitted it was paused, and the
    /// word appeared only once playback resumed. Completeness is judged by the line ending in
    /// the speed instead, which is the last thing the format writes, so a torn write is passed
    /// over without waiting on a terminator that may never come.
    /// </para>
    /// </remarks>
    private void Read(string output)
    {
        // The newest whole reading, working backwards. Only terminated ones can be seen at all:
        // the process is drained by the framework's line reader, which holds a line back until
        // its terminator arrives — and mpv marks the end of a reading by beginning the next, so
        // the one it writes as it pauses stays in that reader for as long as the pause lasts.
        // That is why whether mpv is paused is asked over the socket rather than read from here.
        int at = output.LastIndexOf(Marker, StringComparison.Ordinal);

        while (at >= 0)
        {
            int start = at + Marker.Length;
            int stop = output.IndexOfAny(['\r', '\n'], start);

            if (stop >= 0 && Apply(output[start..stop].TrimEnd()))
            {
                return;
            }

            at = at == 0 ? -1 : output.LastIndexOf(Marker, at - 1, StringComparison.Ordinal);
        }
    }

    /// <summary>Asks mpv whether it is paused.</summary>
    /// <returns>What mpv said, or <see langword="null"/> if it did not answer.</returns>
    /// <remarks>
    /// <para>
    /// Asked rather than read, because the reading that would say so never arrives: mpv writes it
    /// and then stops, and a line reader holds the last line until something terminates it. Every
    /// other figure on the bar is a figure that only moves while playing, so this is the one that
    /// has to be fetched.
    /// </para>
    /// <para>
    /// It is asked on every frame instead of after the keys that might have caused it, for two
    /// reasons: the keys are mpv's to interpret and it is mpv's <c>input.conf</c> that says which
    /// ones pause, and a reply asked for in the same breath as a keypress can be answered before
    /// the keypress is acted on — measured, and the reason this was reported as alternating in
    /// the first place.
    /// </para>
    /// </remarks>
    private bool? AskPause()
    {
        string? reply = Ask($$"""{"command":["get_property","pause"],"request_id":{{PauseRequest}}}""");

        return reply is null ? null : PauseFrom(reply);
    }

    /// <summary>The request number this plugin puts on its questions.</summary>
    private const int PauseRequest = 4711;

    /// <summary>Reads mpv's answer to a <c>pause</c> question.</summary>
    /// <param name="reply">Everything mpv sent back.</param>
    /// <returns>The state, or <see langword="null"/> where the answer was not one.</returns>
    /// <remarks>
    /// mpv answers with one JSON object per line and may send events on the same connection, so
    /// the answer is picked out by the number it was asked with rather than by being the only
    /// thing there. Read by hand rather than with a parser: it is two fields of a known shape,
    /// and a plugin that dragged in a JSON dependency for it would be a plugin nobody could copy.
    /// </remarks>
    internal static bool? PauseFrom(string reply)
    {
        foreach (string line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains($"\"request_id\":{PauseRequest}", StringComparison.Ordinal) ||
                !line.Contains("\"error\":\"success\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains("\"data\":true", StringComparison.Ordinal))
            {
                return true;
            }

            if (line.Contains("\"data\":false", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return null;
    }

    /// <summary>Takes one reading, if it is a whole one.</summary>
    /// <param name="line">Everything between the marker and the end of the line.</param>
    /// <returns>Whether it was used.</returns>
    /// <remarks>
    /// Whole is judged by shape rather than by a terminator: the bar between the machine-readable
    /// fields and the readable half has to be there, and there have to be as many fields as the
    /// format in force asks for. A half-written line is short of one or the other, and a line
    /// written under the previous format — the frame or two after the mode changes it — fails the
    /// count and is passed over for the one before it.
    /// </remarks>
    private bool Apply(string line)
    {
        int bar = line.IndexOf('|', StringComparison.Ordinal);

        if (bar < 0)
        {
            return false;
        }

        string[] fields = line[..bar].Split(';');

        if (fields.Length != FixedFields + _passthrough.Length)
        {
            return false;
        }

        _text = line[(bar + 1)..].Trim();

        _progress =
            double.TryParse(Field(fields, 0), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double percent)
                ? Math.Clamp(percent / 100, 0, 1)
                : null;

        // Left where they were when mpv gives something unreadable, which it does between files:
        // for a frame or two `playlist-pos` and `time-pos` can be blank, and a clock that dropped
        // to zero for that frame would jump backwards on screen.
        if (int.TryParse(Field(fields, 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                         out int index) && index >= 0)
        {
            _index = index;
        }

        if (double.TryParse(Field(fields, 2), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double position) && position >= 0)
        {
            _position = position;
        }

        if (Field(fields, 3) is { Length: > 0 } speed)
        {
            _speed = speed;
        }

        // mpv's own answer, which is the only one that knows. This side used to keep the state
        // itself, toggled by `pap` — and in the mode `p` and `Space` go straight to mpv, which
        // paused without this ever hearing. The word then depended on which of the two had been
        // used last and alternated between right and wrong, as reported.
        if (Field(fields, 5) is { Length: > 0 } paused)
        {
            _paused = paused == "1";
        }

        if (Field(fields, 4) is { Length: > 0 } volume)
        {
            // Not the first reading: that is the volume playback started at, which nobody
            // changed and which mpv would not announce either.
            if (_volume.Length > 0 && !string.Equals(volume, _volume, StringComparison.Ordinal))
            {
                _volumeMoved = DateTimeOffset.UtcNow;
            }

            _volume = volume;
        }

        _rendered = fields[FixedFields..];

        return true;
    }

    /// <summary>How many machine-readable fields are this side's own.</summary>
    private const int FixedFields = 6;

    /// <summary>Takes a format into use, and forgets what was read under the last one.</summary>
    /// <param name="display">The format mpv is being asked to fill in.</param>
    /// <remarks>
    /// The three move together or not at all: which format is in force, which of its parts mpv
    /// answers, and what it last answered. Setting one without the others is how a line comes to
    /// be built from one format and the values of another.
    /// </remarks>
    private void UseFormat(string display)
    {
        _effective = display;
        _passthrough = Passthrough(display);
        _rendered = [];
    }

    /// <summary>What to add to the line to show the volume, if anything.</summary>
    /// <param name="format">The format in force, which may already show it.</param>
    /// <param name="volume">The volume mpv reports.</param>
    /// <param name="moved">Whether it has changed within the last moment.</param>
    /// <returns>The text to append, or empty.</returns>
    /// <remarks>
    /// <para>
    /// A format that names the volume is answered by mpv and needs nothing here: someone who
    /// wants the figure on the line at all times says so in their own <c>mpv.conf</c>, and this
    /// must not say it twice.
    /// </para>
    /// <para>
    /// Otherwise it appears as it moves and then goes, which is what mpv does in a terminal. It
    /// was pinned to the line for as long as the mode lasted, which is neither what mpv does nor
    /// what the format asked for.
    /// </para>
    /// </remarks>
    internal static string VolumeSuffix(string format, string volume, bool moved) =>
        moved && volume.Length > 0 &&
        !format.Contains("volume", StringComparison.OrdinalIgnoreCase)
            ? "  vol " + volume + "%"
            : string.Empty;

    /// <summary>The speed as a number, or 1 where mpv said something unreadable.</summary>
    private static double Rate(string speed) =>
        double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
        && rate > 0
            ? rate
            : 1;

    /// <summary>One of the machine-readable fields, or empty where mpv sent fewer.</summary>
    private static string Field(string[] fields, int at) =>
        at < fields.Length ? fields[at].Trim() : string.Empty;

    /// <summary>Seconds as a clock, in the shape mpv writes them.</summary>
    /// <param name="seconds">A length or a position.</param>
    /// <returns><c>HH:MM:SS</c>.</returns>
    /// <remarks>
    /// mpv's own <c>${duration}</c> is <c>HH:MM:SS</c>, and a queue's total sits beside figures it
    /// wrote, so it is spelled the same way. Hours are kept even at nothing, because a total that
    /// changed shape as it crossed an hour would move the rest of the line sideways.
    /// </remarks>
    internal static string Clock(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(seconds, 0));

        return string.Create(CultureInfo.InvariantCulture,
                             $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}");
    }

    /// <summary>How far into the whole queue playback has got, in seconds.</summary>
    /// <param name="durations">How long each file lasts, in playing order.</param>
    /// <param name="index">The file being played, counted from zero.</param>
    /// <param name="position">How far into that file, in seconds.</param>
    /// <returns>Seconds from the start of the first file.</returns>
    /// <remarks>
    /// Everything before the current file counts in full, whatever mpv says about the current
    /// one: a file that has been played is played whether or not the reading that says so
    /// arrived. The index is clamped because mpv reports the playlist it has, and a queue whose
    /// last file was deleted mid-play would otherwise index past the end of what was measured.
    /// </remarks>
    internal static double Elapsed(IReadOnlyList<double> durations, int index, double position)
    {
        int at = Math.Clamp(index, 0, Math.Max(durations.Count - 1, 0));
        double before = 0;

        for (int i = 0; i < at && i < durations.Count; i++)
        {
            before += durations[i];
        }

        return before + Math.Clamp(position, 0, at < durations.Count ? durations[at] : position);
    }

    /// <summary>
    /// The properties this side answers for a queue, in place of mpv's answers for one file.
    /// </summary>
    /// <param name="inner">The text between <c>${</c> and <c>}</c>.</param>
    /// <returns>Whether the value comes from the queue rather than from mpv.</returns>
    /// <remarks>
    /// Every one of these is a figure mpv can only give for the file it is playing: the point of
    /// a queue's line is that they cover all of it. Anything else — the speed, the volume, the
    /// title, a conditional — means the same thing either way and is left to mpv, which is what
    /// keeps this from becoming a second implementation of a format language.
    /// </remarks>
    internal static bool Answered(string inner)
    {
        string name = inner.StartsWith('=') ? inner[1..] : inner;

        return name is "duration" or "time-pos" or "playback-time" or "time-remaining"
                    or "playtime-remaining" or "percent-pos";
    }

    /// <summary>The parts of a format mpv has to render.</summary>
    /// <param name="format">The user's status format.</param>
    /// <returns>Each expression, braces and all, in the order it appears.</returns>
    internal static string[] Passthrough(string format) =>
        [.. Tokens(format).Where(token => token.IsExpression && !Answered(token.Text))
                          .Select(token => "${" + token.Text + "}")];

    /// <summary>Builds a queue's line from the user's own format.</summary>
    /// <param name="format">The user's status format.</param>
    /// <param name="rendered">What mpv made of the parts it was asked for, in order.</param>
    /// <param name="elapsed">How far into the queue playback has got, in seconds.</param>
    /// <param name="total">How long the whole queue lasts, in seconds.</param>
    /// <param name="speed">The speed playback is running at.</param>
    /// <returns>The line to show, without the position in the queue in front of it.</returns>
    /// <remarks>
    /// <para>
    /// Asked for: a queue should read exactly as a single file does — the same fields, in the same
    /// order, counting the same way — with only the totals made bigger. A line of this side's own
    /// invention could not do that, and did not: it counted up where the user's format counts
    /// down, because <c>playtime-remaining</c> is what their mpv.conf asks for and nothing here
    /// was reading it.
    /// </para>
    /// <para>
    /// So the format is theirs and only the figures are replaced. Anything mpv must answer was
    /// asked of mpv and arrives in <paramref name="rendered"/>; a mismatch in how many arrived
    /// means the format changed between the asking and the reading, and the caller keeps the
    /// previous line rather than showing a torn one.
    /// </para>
    /// </remarks>
    internal static string Render(string format, IReadOnlyList<string> rendered, double elapsed,
                                  double total, double speed)
    {
        StringBuilder line = new();
        int at = 0;

        foreach ((bool isExpression, string text) in Tokens(format))
        {
            if (!isExpression)
            {
                line.Append(text);
            }
            else if (Answered(text))
            {
                line.Append(Value(text, elapsed, total, speed));
            }
            else if (at < rendered.Count)
            {
                line.Append(rendered[at++]);
            }
            else
            {
                at++;
            }
        }

        return line.ToString().Trim();
    }

    /// <summary>One queue-wide figure, spelled as mpv spells it.</summary>
    /// <remarks>
    /// A leading <c>=</c> is mpv's way of asking for the raw number rather than the formatted one,
    /// and is answered the same way here. <c>playtime-remaining</c> is divided by the speed
    /// because that is what it means in mpv — how long the listening has left, not how much of
    /// the recording.
    /// </remarks>
    private static string Value(string inner, double elapsed, double total, double speed)
    {
        bool raw = inner.StartsWith('=');
        string name = raw ? inner[1..] : inner;
        double remaining = Math.Max(total - elapsed, 0);
        double rate = speed > 0 ? speed : 1;

        double seconds = name switch
        {
            "duration" => total,
            "time-pos" or "playback-time" => elapsed,
            "time-remaining" => remaining,
            "playtime-remaining" => remaining / rate,
            _ => 0,
        };

        if (name == "percent-pos")
        {
            double percent = total > 0 ? elapsed / total * 100 : 0;

            return raw
                ? percent.ToString("0.######", CultureInfo.InvariantCulture)
                : Math.Round(percent).ToString("0", CultureInfo.InvariantCulture);
        }

        return raw ? seconds.ToString("0.######", CultureInfo.InvariantCulture) : Clock(seconds);
    }

    /// <summary>Splits a format into its literal text and its <c>${…}</c> expressions.</summary>
    /// <remarks>
    /// Brace-matched rather than found by the next <c>}</c>, because mpv's conditionals nest —
    /// <c>${?pause==yes:${speed}}</c> is one expression, and cutting it at the first closing brace
    /// would hand mpv half of it and leave the rest as literal text on the bar.
    /// </remarks>
    private static IEnumerable<(bool IsExpression, string Text)> Tokens(string format)
    {
        int at = 0;

        while (at < format.Length)
        {
            int open = format.IndexOf("${", at, StringComparison.Ordinal);

            if (open < 0)
            {
                yield return (false, format[at..]);
                yield break;
            }

            if (open > at)
            {
                yield return (false, format[at..open]);
            }

            int depth = 1;
            int scan = open + 2;

            while (scan < format.Length && depth > 0)
            {
                if (format[scan] == '}')
                {
                    depth--;
                }
                else if (format[scan] == '$' && scan + 1 < format.Length && format[scan + 1] == '{')
                {
                    depth++;
                    scan++;
                }

                scan++;
            }

            // An unclosed expression is a format still being edited. Passed on as it stands, so
            // the user sees their own text rather than nothing at all.
            if (depth > 0)
            {
                yield return (false, format[open..]);
                yield break;
            }

            yield return (true, format[(open + 2)..(scan - 1)]);
            at = scan;
        }
    }

    /// <summary>The status line for a queue: which file, then the user's own format.</summary>
    /// <param name="format">The user's status format.</param>
    /// <param name="rendered">What mpv made of the parts it was asked for, in order.</param>
    /// <param name="durations">How long each file lasts, in playing order.</param>
    /// <param name="index">The file being played, counted from zero.</param>
    /// <param name="position">How far into that file, in seconds.</param>
    /// <param name="speed">The speed playback is running at.</param>
    /// <returns>The line to show.</returns>
    /// <remarks>
    /// Which file of how many is worth the four columns it costs, and is the one thing here that
    /// a single file's line does not have. Without it a queue looks like one long file, and there
    /// is nothing on screen to say that <c>pas</c> would stop four hours of listening rather than
    /// forty minutes.
    /// </remarks>
    internal static string OverallLine(string format, IReadOnlyList<string> rendered,
                                       IReadOnlyList<double> durations, int index, double position,
                                       double speed)
    {
        int at = Math.Clamp(index, 0, Math.Max(durations.Count - 1, 0)) + 1;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{at}/{durations.Count}  " +
            $"{Render(format, rendered, Elapsed(durations, index, position), durations.Sum(), speed)}");
    }

    /// <summary>
    /// Puts the volume on the status line while the keyboard belongs to mpv, and takes it off
    /// again afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported: pressing <c>8</c> and <c>9</c> changed the volume with nothing on screen to show
    /// it, where mpv in a terminal says so. mpv does write <c>Volume: 98%</c> to standard output
    /// even through a pipe, but picking those lines out means telling them apart from its start-up
    /// chatter by their shape, which is a parser waiting to be wrong.
    /// </para>
    /// <para>
    /// <c>term-status-msg</c> can be set while mpv is running, so the line itself is extended for
    /// as long as the mode lasts and put back when it ends. The figure is then always there rather
    /// than flashing past, which suits a mode whose whole purpose is adjusting it.
    /// </para>
    /// <para>
    /// Speed is appended only where the user's own format does not already show it, so a format
    /// that says <c>${speed}x</c> — as the reporter's does — is not made to say it twice.
    /// </para>
    /// </remarks>
    private void ShowWhatTheModeIsFor()
    {
        string display = DisplayFor(_display, _handsOver);

        UseFormat(display);
        Send($$"""{"command":["set_property","term-status-msg","{{Escape(StatusFormat(display))}}"]}""");
    }

    /// <summary>The status format for a given state of the mode.</summary>
    /// <param name="display">The user's own format.</param>
    /// <param name="handsOver">Whether the keyboard belongs to mpv.</param>
    /// <returns>What mpv should be told to print.</returns>
    /// <remarks>
    /// <para>
    /// Volume and speed are what the mode exists to change, so they are added while it is on and
    /// taken away afterwards — the user's own format has no reason to carry them the rest of the
    /// time, when the keys that change them are not even forwarded.
    /// </para>
    /// <para>
    /// Neither is added where the format already shows it. Someone who wants the volume on the
    /// line at all times puts it in their own <c>term-status-msg</c>, which is the right place for
    /// it and makes mpv show it in a terminal too; this must not then say it twice.
    /// </para>
    /// </remarks>
    internal static string DisplayFor(string display, bool handsOver)
    {
        if (!handsOver)
        {
            return display;
        }

        string line = display;

        if (!line.Contains("speed", StringComparison.OrdinalIgnoreCase))
        {
            line += "  x${speed}";
        }

        return line;
    }

    /// <summary>Escapes a string for the JSON the IPC socket speaks.</summary>
    /// <param name="text">The text to embed.</param>
    /// <returns>The text, safe between quotation marks.</returns>
    /// <remarks>
    /// A format is the user's own text and may hold a quotation mark or a backslash; either would
    /// end the command early and leave mpv with a status line nobody asked for.
    /// </remarks>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Sends one command to mpv over its IPC socket.</summary>
    /// <param name="json">The command, as mpv's JSON IPC expects it.</param>
    /// <returns>Whether it was delivered.</returns>
    /// <remarks>
    /// A fresh connection each time rather than one held open: a command goes out perhaps twice a
    /// minute, connecting costs nothing measurable, and a socket held across a suspend is a socket
    /// that has to be revived. The reply is not read — mpv acts on the command whether or not
    /// anyone listens, and the next status line says what happened.
    /// </remarks>
    private bool Send(string json)
    {
        if (_socket.Length == 0)
        {
            return false;
        }

        try
        {
            using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            socket.Connect(new UnixDomainSocketEndPoint(_socket));
            socket.Send(Encoding.UTF8.GetBytes(json + "\n"));

            return true;
        }
        catch (Exception e) when (e is SocketException or IOException
                                       or ObjectDisposedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Sends a command and returns what mpv said back.</summary>
    /// <param name="json">The command, without its newline.</param>
    /// <returns>The reply, or <see langword="null"/> where there was none.</returns>
    /// <remarks>
    /// Bounded by a receive timeout, because this runs on the thread that draws: an mpv that has
    /// stopped answering must cost a frame, not the interface. A tenth of a second is two orders
    /// of magnitude more than a local socket needs.
    /// </remarks>
    private string? Ask(string json)
    {
        if (_socket.Length == 0)
        {
            return null;
        }

        try
        {
            using Socket socket = new(AddressFamily.Unix, SocketType.Stream,
                                      ProtocolType.Unspecified)
            {
                ReceiveTimeout = 100,
                SendTimeout = 100,
            };

            socket.Connect(new UnixDomainSocketEndPoint(_socket));
            socket.Send(Encoding.UTF8.GetBytes(json + "\n"));

            byte[] buffer = new byte[4096];
            int read = socket.Receive(buffer);

            return read > 0 ? Encoding.UTF8.GetString(buffer, 0, read) : null;
        }
        catch (Exception e) when (e is SocketException or IOException
                                       or ObjectDisposedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Lets go of a finished process and takes the line off the bar.</summary>
    private void Forget()
    {
        _process = null;
        _text = null;
        _progress = null;
        _paused = false;
        _files = [];
        _durations = null;

        // The keyboard goes back with the playback it belonged to. Without this, a file reaching
        // its end would leave the keys pointed at a program that is no longer there.
        if (_handsOver)
        {
            _handsOver = false;
            _fileManager.KeyGrab = null;
        }

        if (ReferenceEquals(_fileManager.BackgroundActivity, this))
        {
            _fileManager.BackgroundActivity = null;
        }

        try
        {
            if (_socket.Length > 0)
            {
                File.Delete(_socket);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The socket outliving the process is untidy, not harmful; the next start replaces it.
        }

        _socket = string.Empty;
    }

    /// <summary>Wraps an argument for the shell that runs the command line.</summary>
    private static string Quote(string text) => "'" + text.Replace("'", @"'\''") + "'";
}

/// <summary>What mpv's own configuration says about how a status line should read.</summary>
/// <remarks>
/// Read rather than imposed: the point of showing mpv's status line in the status bar is that it
/// is the line the user already knows, and they have said how it should look. A format of Canger's
/// own would be a second thing to configure and a second thing to disagree.
/// </remarks>
internal static class MpvConfiguration
{
    /// <summary>The <c>term-status-msg</c> in force, or a plain default.</summary>
    /// <returns>An mpv format string, never empty.</returns>
    /// <remarks>
    /// mpv looks in <c>$MPV_HOME</c>, then <c>$XDG_CONFIG_HOME/mpv</c>, then <c>~/.config/mpv</c>,
    /// and this looks in the same places and stops at the first file that has the option. Includes
    /// are not followed: a setting reached that way is rare, and reading the wrong one would be
    /// worse than falling back to something plain.
    /// </remarks>
    internal static string StatusFormat()
    {
        foreach (string directory in Directories())
        {
            string file = Path.Join(directory, "mpv.conf");

            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(file))
                {
                    if (Option(line) is { Length: > 0 } format)
                    {
                        return format;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Unreadable configuration is not a reason to refuse to play anything.
            }
        }

        return Playback.PlainStatusFormat;
    }

    /// <summary>Where mpv looks for its configuration.</summary>
    /// <returns>The directories to read, in order.</returns>
    /// <remarks>
    /// <c>MPV_HOME</c> <em>replaces</em> the configuration directory rather than being searched
    /// before it, which is how mpv itself treats it. Falling through to <c>~/.config/mpv</c>
    /// afterwards read a format the user had deliberately pointed away from — caught by the test
    /// for the plain fallback, which found the ambient configuration instead of the empty one it
    /// had just been given.
    /// </remarks>
    private static IEnumerable<string> Directories()
    {
        if (Environment.GetEnvironmentVariable("MPV_HOME") is { Length: > 0 } home)
        {
            return [home];
        }

        if (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg)
        {
            return [Path.Join(xdg, "mpv")];
        }

        return [Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "mpv")];
    }

    /// <summary>Reads a <c>term-status-msg</c> assignment out of one configuration line.</summary>
    /// <param name="line">One line of mpv.conf.</param>
    /// <returns>The format, or <see langword="null"/> when the line is something else.</returns>
    /// <remarks>
    /// mpv's configuration allows the value to be quoted with either kind of quote, and requires
    /// it whenever the value contains a <c>#</c> — which is why the comment is only stripped from
    /// an unquoted value.
    /// </remarks>
    internal static string? Option(string line)
    {
        string text = line.Trim();

        if (text.StartsWith('#') || !text.StartsWith("term-status-msg", StringComparison.Ordinal))
        {
            return null;
        }

        int equals = text.IndexOf('=', StringComparison.Ordinal);

        if (equals < 0 || text[..equals].Trim() != "term-status-msg")
        {
            return null;
        }

        string value = text[(equals + 1)..].Trim();

        if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
        {
            return value[1..^1];
        }

        int comment = value.IndexOf('#', StringComparison.Ordinal);

        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }
}

/// <summary>Keeps the one playback slot, and hands it to the commands.</summary>
/// <remarks>
/// A plugin's commands are constructed one per invocation, so the slot cannot live on a command.
/// It hangs off the file manager instead, keyed by nothing more than a static — there is one
/// browser per process.
/// </remarks>
internal static class Current
{
    private static Playback? _playback;

    /// <summary>The slot, made on first use.</summary>
    /// <param name="fileManager">What it acts on.</param>
    /// <returns>The one playback.</returns>
    internal static Playback For(IFileManager fileManager) =>
        _playback ??= new Playback(fileManager);
}

/// <summary>Plays the selected audio file in the background.</summary>
[Command("mka_play", Summary = "Play the selected audio files in the background.")]
public sealed class MkaPlayCommand : CangerCommand
{
    /// <inheritdoc />
    /// <remarks>
    /// The playable files of the selection, which is every marked file or the one under the
    /// cursor. Asked for by name, so a selection holding other things plays the audio in it and
    /// says nothing about the rest — unlike Enter, which declines a mixed selection outright
    /// rather than quietly swallowing the files it cannot play.
    /// </remarks>
    public override void Execute()
    {
        string[] paths =
            [.. FileManager.Selection
                           .Where(file => !file.IsDirectory && Playable.Matches(file.Path))
                           .Select(file => file.Path)];

        if (paths.Length == 0)
        {
            FileManager.Notify("mka_play: nothing to play", isError: true);
            return;
        }

        Current.For(FileManager).Start(paths);
    }
}

/// <summary>Pauses or resumes what is playing.</summary>
[Command("mka_pause", Summary = "Pause or resume background playback.")]
public sealed class MkaPauseCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Current.For(FileManager).TogglePause();
}

/// <summary>Hands the keyboard to mpv, or takes it back.</summary>
[Command("mka_mode", Summary = "Send keys to mpv until Escape.")]
public sealed class MkaModeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Current.For(FileManager).ToggleKeyboard();
}

/// <summary>Stops what is playing.</summary>
[Command("mka_stop", Summary = "Stop background playback.")]
public sealed class MkaStopCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Playback playback = Current.For(FileManager);

        if (!playback.IsActive)
        {
            FileManager.Notify("mka: nothing is playing", isError: true);
            return;
        }

        // Nothing to say: the clock leaving the status bar is the message, and a message would
        // only cover the bar it just vacated.
        playback.Stop();
    }
}

/// <summary>Names keys the way mpv names them.</summary>
/// <remarks>
/// mpv's <c>keypress</c> command takes a key by name, and the names are its own: a printable
/// character is itself, and everything else has a word. Only the keys worth having while listening
/// are translated — seeking, volume, speed and pause — because a key with no name is better
/// swallowed than guessed at.
/// </remarks>
internal static class MpvKeys
{
    /// <summary>What mpv calls the keys that are not printable characters.</summary>
    private static readonly Dictionary<int, string> Named = new()
    {
        [KeyCodes.Left] = "LEFT",
        [KeyCodes.Right] = "RIGHT",
        [KeyCodes.Up] = "UP",
        [KeyCodes.Down] = "DOWN",
        [KeyCodes.Space] = "SPACE",
        [KeyCodes.Enter] = "ENTER",
    };

    /// <summary>mpv's name for a key, or <see langword="null"/> where it has none.</summary>
    /// <param name="key">The key, as <see cref="KeyCodes"/> numbers them.</param>
    /// <returns>The name to send, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A printable character is sent as itself. The quotation mark and the backslash are left out
    /// rather than escaped: they are not mpv bindings worth having, and the alternative is
    /// building JSON by hand around a character that would break it.
    /// </remarks>
    internal static string? NameOf(int key)
    {
        if (Named.TryGetValue(key, out string? name))
        {
            return name;
        }

        return key is > 32 and < 127 and not '"' and not '\\' ? ((char)key).ToString() : null;
    }
}

/// <summary>Claims audio files, so Enter and the right arrow play them.</summary>
/// <remarks>
/// <para>
/// Registered with <see cref="IFileManager.FileOpeners"/> rather than bound over
/// <c>&lt;CR&gt;</c> and <c>&lt;RIGHT&gt;</c>, which is how this worked first and was a trap: the
/// configuration then named a command that only existed while the plugin did, so taking the
/// plugin away left neither key able to open anything — not a file, and not a folder either.
/// Nothing needs rebinding now, and removing this file restores the ordinary behaviour exactly.
/// </para>
/// <para>
/// One file at a time, and only a plain open. A selection of several is left to the ordinary
/// rules, and <c>:open_with</c> names its program on purpose.
/// </para>
/// </remarks>
public sealed class MkaPlugin : ICangerPlugin
{
    /// <inheritdoc />
    public void OnInit(IFileManager fileManager)
    {
        ArgumentNullException.ThrowIfNull(fileManager);

        fileManager.FileOpeners.Add(paths =>
        {
            // Every one of them, or none: a selection of audio plays as a queue, and a selection
            // holding anything else is left to the ordinary rules. Claiming a mixed selection
            // would mean silently dropping whatever is not audio, and refusing a selection of
            // several was what sent them all to rifle — which opened mpv in the terminal, over
            // the interface, which is what this plugin exists to avoid.
            if (paths.Count == 0 || !paths.All(Playable.Matches))
            {
                return false;
            }

            Current.For(fileManager).Start(paths);

            return true;
        });
    }
}
