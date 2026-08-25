// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Tui;

/// <summary>
/// A terminal emulator, and how to ask it to run a command.
/// </summary>
/// <param name="Program">The executable.</param>
/// <param name="CommandFlag">The option that introduces the command to run.</param>
/// <param name="JoinsCommand">
/// Whether the emulator wants the command as a single argument rather than a program followed by
/// its arguments. A handful do, and passing them the usual shape silently opens an empty shell.
/// </param>
public readonly record struct TerminalEmulator(string Program, string CommandFlag, bool JoinsCommand)
{
    /// <summary>Builds the argument list for running a command in this emulator.</summary>
    /// <param name="shell">The shell that should interpret the command.</param>
    /// <param name="command">The command line.</param>
    /// <returns>The arguments to pass.</returns>
    public IReadOnlyList<string> BuildArguments(string shell, string command)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(command);

        return JoinsCommand
            ? [CommandFlag, $"{shell} -c {command}"]
            : [CommandFlag, shell, "-c", command];
    }
}

/// <summary>
/// Finds a terminal emulator to open a new window in.
/// </summary>
/// <remarks>
/// <para>
/// Emulators disagree about how to be told what to run. Most take <c>-e</c>, some take <c>-x</c>
/// or <c>--</c>, and a few insist on the whole command as one argument. Ranger carries the same
/// table (<c>ext/rifle.py:499-528</c>) because there is no way to derive it.
/// </para>
/// <para>
/// The user's own preference wins: <c>$TERMCMD</c> is consulted before anything is guessed.
/// </para>
/// </remarks>
public static class TerminalEmulators
{
    private static readonly Dictionary<string, TerminalEmulator> Known = BuildTable();

    /// <summary>The order emulators are tried in when nothing is configured.</summary>
    private static readonly string[] SearchOrder =
    [
        "x-terminal-emulator", "kitty", "alacritty", "wezterm", "foot", "ghostty", "gnome-terminal",
        "konsole", "xfce4-terminal", "mate-terminal", "terminator", "urxvt", "st", "lxterminal",
        "xterm",
    ];

    /// <summary>
    /// Finds an emulator, preferring the one the user configured.
    /// </summary>
    /// <returns>The emulator, or <see langword="null"/> when none could be found.</returns>
    public static TerminalEmulator? Detect()
    {
        if (Environment.GetEnvironmentVariable("TERMCMD") is { Length: > 0 } configured)
        {
            return Describe(configured);
        }

        foreach (string candidate in SearchOrder)
        {
            if (Exists(candidate))
            {
                return Describe(candidate);
            }
        }

        // $TERM names the terminal Canger is running in, which is a reasonable last guess.
        string? term = Environment.GetEnvironmentVariable("TERM");
        return term is { Length: > 0 } && Exists(term) ? Describe(term) : null;
    }

    /// <summary>How to invoke a named emulator.</summary>
    /// <param name="program">The executable name.</param>
    /// <returns>Its invocation shape, defaulting to <c>-e</c> for an unknown one.</returns>
    public static TerminalEmulator Describe(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);

        string name = Path.GetFileName(program);
        return Known.TryGetValue(name, out TerminalEmulator known)
            ? known with { Program = program }
            : new TerminalEmulator(program, "-e", JoinsCommand: false);
    }

    /// <summary>Whether a program is on the path.</summary>
    /// <param name="program">The executable name.</param>
    /// <returns><see langword="true"/> when it can be found.</returns>
    public static bool Exists(string program) => Core.Processes.Executables.Exists(program);

    private static Dictionary<string, TerminalEmulator> BuildTable()
    {
        Dictionary<string, TerminalEmulator> table = new(StringComparer.Ordinal);

        void Add(string flag, bool joins, params string[] programs)
        {
            foreach (string program in programs)
            {
                table[program] = new TerminalEmulator(program, flag, joins);
            }
        }

        Add("-e", false,
            "xterm", "urxvt", "rxvt", "lxterminal", "konsole", "lilyterm", "cool-retro-term",
            "pantheon-terminal", "st", "stterm", "x-terminal-emulator", "alacritty", "foot",
            "wezterm", "ghostty", "eterm", "aterm", "roxterm", "sakura", "qterminal", "deepin-terminal");

        Add("-x", false, "xfce4-terminal", "mate-terminal", "terminator");

        Add("--", false, "gnome-terminal", "kitty");

        Add("-c", false, "tilda");

        // These read the command as one argument; giving them the usual shape opens an empty
        // shell and silently drops what was asked for.
        Add("-e", true, "terminology", "termite");

        return table;
    }
}
