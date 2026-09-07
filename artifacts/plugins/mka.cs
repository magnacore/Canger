// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Net.Sockets;
using System.Text;
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
internal sealed class Playback : IBackgroundActivity
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
    private const string StatusFormat =
        Marker + "${=percent-pos}|${time-pos} / ${duration} (${percent-pos}%) ${speed}x";

    /// <summary>What is shown in front of the clock while playback is held.</summary>
    /// <remarks>
    /// Kept here rather than taken from mpv's own <c>${?pause==yes:…}</c>, which reads better on
    /// paper and is useless in practice: a paused mpv writes nothing further, so the last line to
    /// arrive is the one from just *before* the pause and the word only turned up when playback
    /// resumed — by which time it was wrong. This side of the socket knows the answer the instant
    /// the command is sent.
    /// </remarks>
    private const string PausedPrefix = "(paused) ";

    private readonly IFileManager _fileManager;

    private IBackgroundProcess? _process;
    private string _socket = string.Empty;
    private string? _text;
    private double? _progress;
    private bool _paused;

    /// <summary>Prepares the slot.</summary>
    /// <param name="fileManager">Used to start mpv and to say what happened.</param>
    internal Playback(IFileManager fileManager) => _fileManager = fileManager;

    /// <summary>What is playing, for a message.</summary>
    internal string? Basename { get; private set; }

    /// <summary>Whether something is playing or paused.</summary>
    internal bool IsActive => _process is { HasExited: false };

    /// <inheritdoc />
    public double? Progress => _progress;

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

        return _text is null ? null : _paused ? PausedPrefix + _text : _text;
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
            $"--term-status-msg={Quote(StatusFormat)} {Quote(path)}";

        _process = _fileManager.Runner.StartInBackground(new ProcessRequest(command));

        if (_process is null)
        {
            _fileManager.Notify("mka: could not start mpv", isError: true);
            return;
        }

        Basename = Path.GetFileName(path);
        _text = null;
        _progress = null;
        _paused = false;

        _fileManager.BackgroundActivity = this;
        _fileManager.Notify($"playing {Basename}");
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
        int at = output.LastIndexOf(Marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return;
        }

        int start = at + Marker.Length;
        int end = output.IndexOfAny(['\r', '\n'], start);
        string line = (end < 0 ? output[start..] : output[start..end]).TrimEnd();

        int bar = line.IndexOf('|', StringComparison.Ordinal);

        // Half-written: the percentage and the speed bracket the whole line, so a line with both
        // is a line that arrived entire.
        if (bar < 0 || !line.EndsWith('x'))
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
        Basename = null;

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

        string? name = playback.Basename;
        playback.Stop();
        FileManager.Notify($"stopped {name}");
    }
}

/// <summary>
/// Enter: plays an audio file, and does whatever Enter did for everything else.
/// </summary>
/// <remarks>
/// Bound over <c>&lt;CR&gt;</c> in place of <c>move right=1</c>. Anything that is not one of the
/// audio formats is handed straight back to <c>move right=1</c>, so a directory still opens and a
/// document still goes to rifle — the binding adds a case rather than replacing the key.
/// </remarks>
[Command("mka_open", Summary = "Play an audio file, or open anything else as usual.")]
public sealed class MkaOpenCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.CurrentFile is { IsDirectory: false } file &&
            Playable.Matches(file.Basename))
        {
            Current.For(FileManager).Start(file.Path);
            return;
        }

        FileManager.Execute("move right=1");
    }
}
