// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Net.Sockets;
using System.Text;
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
    /// percentage at all.
    /// </remarks>
    private static string StatusFormat(string display) =>
        Marker + "${=percent-pos}|" + display;

    /// <summary>What is shown in front of the clock while playback is held.</summary>
    /// <remarks>
    /// Added by this side rather than taken from mpv's own <c>${?pause==yes:…}</c>, which reads
    /// better on paper and is useless in practice: a paused mpv writes nothing further, so the
    /// last line to arrive is the one from just *before* the pause, and the word only turned up
    /// once playback resumed — by which time it was wrong. This side of the socket knows the
    /// answer the instant the command is sent.
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

    /// <summary>The format used where mpv's configuration names none.</summary>
    internal static string PlainStatusFormat => DefaultStatusFormat;

    /// <summary>Prepares the slot.</summary>
    /// <param name="fileManager">Used to start mpv and to say what happened.</param>
    internal Playback(IFileManager fileManager) => _fileManager = fileManager;

    /// <summary>Whether something is playing or paused.</summary>
    internal bool IsActive => _process is { HasExited: false };

    /// <inheritdoc />
    public double? Progress => _progress;

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

        return _paused && !_text.Contains("paused", StringComparison.OrdinalIgnoreCase)
            ? PausedPrefix + _text
            : _text;
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

            return key == KeyCodes.Escape;
        }

        if (MpvKeys.NameOf(key) is { } name)
        {
            Send($$"""{"command":["keypress","{{name}}"]}""");
        }

        return true;
    }

    /// <summary>Starts playing a file, replacing whatever was playing.</summary>
    /// <param name="path">The file to play.</param>
    internal void Start(string path)
    {
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
        string command =
            $"mpv --no-video --idle=no --input-terminal=no " +
            $"--input-ipc-server={Quote(_socket)} " +
            $"--term-status-msg={Quote(StatusFormat(MpvConfiguration.StatusFormat()))} " +
            $"{Quote(path)}";

        _process = _fileManager.Runner.StartInBackground(new ProcessRequest(command));

        if (_process is null)
        {
            _fileManager.Notify("mka: could not start mpv", isError: true);
            return;
        }

        _text = null;
        _progress = null;
        _paused = false;

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

        _paused = !_paused;

        // Asked for explicitly, because a paused mpv stops writing and so stops waking the loop
        // that redraws. Without this the word appears whenever something else happens to cause a
        // frame, which may be minutes later.
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
        // Searched back from the last separator rather than from the end, so that a part-line
        // still being written does not hide the finished one in front of it. Looking for the last
        // marker outright found the unfinished one and then gave up for want of a terminator,
        // which left the clock on whatever it had shown before.
        int terminator = output.LastIndexOfAny(['\r', '\n']);

        if (terminator < 0)
        {
            return;
        }

        int at = output.LastIndexOf(Marker, terminator, StringComparison.Ordinal);

        if (at < 0)
        {
            return;
        }

        int start = at + Marker.Length;
        int end = output.IndexOfAny(['\r', '\n'], start);

        // Terminated, or not used. Anything after the final separator is a line still being
        // written, and showing half of one is worse than showing the previous one for another
        // frame. There is no need to reach for it: while playback is held, the reading from just
        // before the pause is exactly what should be on screen, and the word "paused" in front of
        // it comes from this side rather than from mpv.
        //
        // Not judged by what the line ends with, either. That was tried — the format used to end
        // in ${speed}x, so a line ending in "x" was taken to be whole — and it stopped working
        // the moment the format became the user's own, whose line ends in ${?pause==yes:(Paused)}.
        if (end < 0)
        {
            return;
        }

        string line = output[start..end].TrimEnd();
        int bar = line.IndexOf('|', StringComparison.Ordinal);

        if (bar < 0)
        {
            return;
        }

        _text = line[(bar + 1)..].Trim();

        _progress =
            double.TryParse(line[..bar], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double percent)
                ? Math.Clamp(percent / 100, 0, 1)
                : null;
    }

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

    /// <summary>Lets go of a finished process and takes the line off the bar.</summary>
    private void Forget()
    {
        _process = null;
        _text = null;
        _progress = null;
        _paused = false;

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
[Command("mka_play", Summary = "Play the selected audio file in the background.")]
public sealed class MkaPlayCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.CurrentFile is not { IsDirectory: false } file)
        {
            FileManager.Notify("mka_play: no file selected", isError: true);
            return;
        }

        Current.For(FileManager).Start(file.Path);
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
            if (paths.Count != 1 || !Playable.Matches(paths[0]))
            {
                return false;
            }

            Current.For(fileManager).Start(paths[0]);

            return true;
        });
    }
}
