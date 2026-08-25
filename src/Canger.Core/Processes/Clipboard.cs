// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Canger.Core.Processes;

/// <summary>
/// Puts text on the system clipboard.
/// </summary>
/// <remarks>
/// An interface because there is no clipboard to write to in a test, and because a headless
/// machine has no clipboard program at all — both need to be answerable without launching
/// anything.
/// </remarks>
public interface IClipboard
{
    /// <summary>Copies text to the clipboard.</summary>
    /// <param name="text">What to copy.</param>
    /// <returns>
    /// The name of the program that took it, or <see langword="null"/> when there was none.
    /// </returns>
    string? Copy(string text);
}

/// <summary>
/// The system clipboard, reached through whichever helper program is installed.
/// </summary>
/// <remarks>
/// <para>
/// There is no system call for this: X11 and Wayland both keep the selection in a client, so the
/// only way to set it is to hand the text to a program that will hold it. Ranger's table of
/// programs and its order of preference are reproduced exactly
/// (<c>config/commands.py:2060-2083</c>), so a machine where <c>yp</c> works in ranger behaves
/// the same here.
/// </para>
/// <para>
/// X11 has two independent selections and the helpers only set one each, which is why xclip and
/// xsel are each run twice: once for the primary selection that middle-click pastes, once for the
/// clipboard that Ctrl-V pastes. Copying to only one of them is the most common way for this to
/// look broken.
/// </para>
/// <para>
/// The text goes in on standard input rather than as an argument. A filename may contain
/// anything at all, including a leading dash or a newline, and nothing here should have to be
/// escaped for a shell.
/// </para>
/// </remarks>
public sealed class SystemClipboard : IClipboard
{
    /// <summary>How long a helper is given to accept the text before it is abandoned.</summary>
    /// <remarks>
    /// Generous, because these programs fork to hold the selection and the parent exits at once.
    /// Long enough to notice a failure, short enough not to stall a keystroke.
    /// </remarks>
    private const int Deadline = 2000;

    /// <summary>
    /// The helpers, in the order they are preferred, each with every invocation it needs.
    /// </summary>
    private static readonly (string Program, string[][] Invocations)[] Helpers =
    [
        ("pbcopy", [[]]),
        ("xclip", [[], ["-selection", "clipboard"]]),
        ("xsel", [[], ["-b"]]),
        ("wl-copy", [[]]),
    ];

    /// <inheritdoc />
    public string? Copy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach ((string program, string[][] invocations) in Helpers)
        {
            if (!Executables.Exists(program))
            {
                continue;
            }

            // One failing invocation does not condemn the helper: xclip without a display fails
            // for the clipboard selection exactly as it does for the primary one, and reporting
            // the program that was tried is still the useful answer.
            bool any = false;

            foreach (string[] arguments in invocations)
            {
                any |= Feed(program, arguments, text);
            }

            return any ? program : null;
        }

        return null;
    }

    /// <summary>Names the helpers this looks for, in order, for a diagnostic message.</summary>
    public static IReadOnlyList<string> KnownHelpers => [.. Helpers.Select(h => h.Program)];

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "A clipboard helper is an external program that may be "
                                   + "missing, misconfigured or unable to reach a display. None "
                                   + "of that should reach the interface as an exception.")]
    private static bool Feed(string program, string[] arguments, string text)
    {
        try
        {
            ProcessStartInfo startInfo = new(program)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();

            using (StreamWriter input = process.StandardInput)
            {
                input.Write(text);
            }

            // These helpers daemonise to hold the selection, so the parent returning is the
            // whole story. One that has not returned in time has still very likely taken the
            // text, so a timeout is not treated as a failure.
            return !process.WaitForExit(Deadline) || process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
