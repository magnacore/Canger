// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Processes;

/// <summary>A program to run, and how.</summary>
/// <param name="Command">The command line, interpreted by a shell.</param>
/// <param name="Flags">How to run it.</param>
/// <param name="WorkingDirectory">Where to run it, or <see langword="null"/> for the current one.</param>
public readonly record struct ProcessRequest(
    string Command,
    ProcessFlags Flags = default,
    string? WorkingDirectory = null);

/// <summary>
/// What happened when a program ran.
/// </summary>
/// <remarks>
/// <para>
/// Written out rather than as a positional record because of a trap that cost two working flags.
/// A record struct's primary-constructor defaults do <em>not</em> apply to <c>new T()</c>: that
/// zero-initialises the fields, so a <c>string Output = ""</c> parameter leaves <c>Output</c>
/// <see langword="null"/>. Both <c>shell -f</c> and <c>shell -t</c> returned <c>new
/// ProcessResult()</c>, and the caller's <c>result.Output.Length</c> threw — so every forked
/// binding in a real configuration failed with "Object reference not set to an instance of an
/// object".
/// </para>
/// <para>
/// <see cref="Output"/> therefore normalises: null and empty are stored the same way and read
/// back as an empty string, so a default-constructed value behaves like an explicit one and
/// compares equal to it.
/// </para>
/// </remarks>
public readonly record struct ProcessResult
{
    private readonly string? _output;

    /// <summary>Creates a result.</summary>
    /// <param name="exitCode">Its exit code, or <see langword="null"/> when it was not waited for.</param>
    /// <param name="output">What it printed, when the output was captured.</param>
    /// <param name="error">Why it could not be started, when it could not.</param>
    public ProcessResult(int? exitCode = null, string? output = null, string? error = null)
    {
        ExitCode = exitCode;
        Output = output ?? string.Empty;
        Error = error;
    }

    /// <summary>Its exit code, or <see langword="null"/> when it was not waited for.</summary>
    public int? ExitCode { get; init; }

    /// <summary>What it printed. Never <see langword="null"/>, however the value was made.</summary>
    public string Output
    {
        get => _output ?? string.Empty;

        // Stored as null when empty, so `new ProcessResult()` and `new ProcessResult(output: "")`
        // are the same value and compare equal.
        init => _output = string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Why it could not be started, when it could not.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the program started and, if waited for, succeeded.</summary>
    public bool Succeeded => Error is null && ExitCode is null or 0;
}

/// <summary>
/// Runs external programs on Canger's behalf.
/// </summary>
/// <remarks>
/// This is an interface because running a program means taking the terminal away from the
/// interface and giving it back afterwards, which only the real terminal can do. Commands and
/// rifle depend on the interface, so both can be tested without launching anything.
/// </remarks>
public interface IProcessRunner
{
    /// <summary>Runs a program.</summary>
    /// <param name="request">What to run, and how.</param>
    /// <returns>What happened.</returns>
    ProcessResult Run(ProcessRequest request);

    /// <summary>
    /// Runs a program that draws on the terminal but whose standard output is wanted.
    /// </summary>
    /// <param name="request">What to run. Flags other than the working directory are ignored.</param>
    /// <returns>What happened, with <see cref="ProcessResult.Output"/> filled in.</returns>
    /// <remarks>
    /// <para>
    /// This is the shape a chooser needs — fzf, or an ncurses menu — and it is neither of the two
    /// the ordinary <see cref="Run"/> offers. A program given the terminal has its output going to
    /// the screen, and a program whose output is captured is not given the terminal at all. Here
    /// the interface steps aside and *only* standard output is a pipe, so the program draws on the
    /// terminal through stderr or <c>/dev/tty</c> as these programs all do, and prints its answer
    /// where the caller can read it.
    /// </para>
    /// <para>
    /// Ranger reaches the same arrangement with <c>execute_command(cmd, stdout=PIPE)</c>
    /// (<c>config/commands.py</c>, the <c>fzf_select</c> family).
    /// </para>
    /// </remarks>
    ProcessResult RunCapturingOutput(ProcessRequest request);

    /// <summary>
    /// Starts a program in the background, without giving it the terminal.
    /// </summary>
    /// <param name="request">What to run. Only the working directory is taken from the flags.</param>
    /// <returns>A handle to poll, or <see langword="null"/> if it could not be started.</returns>
    /// <remarks>
    /// Both output streams are pipes and standard input is empty, so the program can neither draw
    /// on the screen nor block waiting to be typed at. Ranger's <c>CommandLoader</c> is explicit
    /// that this is the bargain — "ensure that the process doesn't ever ask for input, otherwise
    /// the loader will be blocked" (<c>core/loader.py:166-168</c>).
    /// </remarks>
    IBackgroundProcess? StartInBackground(ProcessRequest request);

    /// <summary>
    /// Raised just before a program is given the terminal.
    /// </summary>
    /// <remarks>
    /// An image drawn by an outside helper — ueberzug, or a graphics protocol — is not part of
    /// the screen buffer and does not go away when the buffer is cleared. Without this it stays
    /// on top of whatever program is handed the terminal next.
    /// </remarks>
    event EventHandler? Suspending;

    /// <summary>
    /// Raised after a program that took the terminal has given it back.
    /// </summary>
    /// <remarks>
    /// Part of the contract rather than an implementation detail: a program that had the terminal
    /// has left the screen in a state nothing else knows about, so whoever draws must be told to
    /// repaint from scratch. Without it the interface comes back blank — which is exactly what
    /// happens to every file opened through rifle, since that path never returns to the caller.
    /// </remarks>
    event EventHandler? Resumed;
}
