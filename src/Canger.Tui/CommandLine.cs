// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Tui;

/// <summary>
/// Splits a command line into words, when doing so is certainly equivalent to letting a shell
/// do it.
/// </summary>
/// <remarks>
/// <para>
/// Every external program is run as <c>sh -c "the whole line"</c>, which makes the line a single
/// argument — and Linux caps one argument at <c>MAX_ARG_STRLEN</c>, 32 pages, 131 072 bytes.
/// That is far below the limit on the whole of <c>argv</c>, which is <c>ARG_MAX</c>, two megabytes
/// on an ordinary system. Numbering 2 561 files built a line of 380 530 bytes: three times the
/// per-argument limit, and a fifth of the limit it would have had as separate arguments.
/// </para>
/// <para>
/// Measured rather than reasoned about: one argument of 131 000 bytes runs, one of 132 000 fails
/// with "Argument list too long", and the same 380 000 bytes spread across many arguments runs
/// perfectly well. So the fix is to stop making it one argument when nothing needs a shell.
/// </para>
/// <para>
/// The rule is conservative to the point of timidity, because the cost of being wrong is running
/// something other than what was asked. Anything with a pipe, a redirection, a variable, a glob
/// or a backslash goes to the shell as before; only a plain command with plain and quoted words
/// is split here. When in any doubt at all this answers <see langword="null"/>, which means "use
/// the shell", which is what always used to happen.
/// </para>
/// </remarks>
public static class CommandLine
{
    /// <summary>
    /// Characters that mean something to a shell, and so mean this line belongs to one.
    /// </summary>
    /// <remarks>
    /// Quotes are absent deliberately: they are handled here, and they are on nearly every line
    /// Canger builds, since filenames are quoted as macros expand. A line disqualified by its own
    /// quoting would leave the fast path unreachable.
    /// </remarks>
    private const string Meta = "|&;<>()$`\\*?[]{}~!#\n\r";

    /// <summary>
    /// Words that are the shell's own, and so mean the line is for the shell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two reasons, and both are about running exactly what used to be run. Most of these have no
    /// executable file anywhere — <c>cd</c>, <c>export</c>, <c>alias</c>, <c>ulimit</c> — so
    /// starting them directly reports that there is no such program, where a shell would have run
    /// them. And where a file does exist, it is not the same thing: the shell's <c>echo</c>,
    /// <c>printf</c> and <c>test</c> differ from the ones in <c>/usr/bin</c> in small ways that
    /// somebody's binding may depend on.
    /// </para>
    /// <para>
    /// Nothing is lost by sending them to the shell. The lines that grow past a hundred kilobytes
    /// are lines of filenames handed to a real program; no builtin is ever given two thousand
    /// arguments.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        ".", ":", "alias", "bg", "bind", "break", "builtin", "caller", "cd", "command",
        "compgen", "complete", "continue", "declare", "dirs", "disown", "echo", "enable",
        "eval", "exec", "exit", "export", "false", "fc", "fg", "getopts", "hash", "help",
        "history", "jobs", "let", "local", "logout", "mapfile", "popd", "printf", "pushd",
        "pwd", "read", "readarray", "readonly", "return", "set", "shift", "shopt", "source",
        "suspend", "test", "time", "times", "trap", "true", "type", "typeset", "ulimit",
        "umask", "unalias", "unset", "wait", "[",
    };

    /// <summary>
    /// Splits a command line into a program and its arguments.
    /// </summary>
    /// <param name="command">The line.</param>
    /// <returns>
    /// The words, or <see langword="null"/> when the line needs a shell — or when this cannot be
    /// certain that it does not.
    /// </returns>
    public static IReadOnlyList<string>? TrySplit(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        List<string> words = [];
        StringBuilder word = new();
        bool started = false;

        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];

            if (c is ' ' or '\t')
            {
                if (started)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    started = false;
                }

                continue;
            }

            if (c == '\'')
            {
                // Single quotes are the simple case and the common one: everything up to the next
                // quote is literal, with no escapes of any kind inside.
                int end = command.IndexOf('\'', i + 1);

                if (end < 0)
                {
                    return null;
                }

                word.Append(command, i + 1, end - i - 1);
                started = true;
                i = end;
                continue;
            }

            if (c == '"')
            {
                int end = command.IndexOf('"', i + 1);

                if (end < 0)
                {
                    return null;
                }

                string inside = command[(i + 1)..end];

                // A double-quoted string still expands variables and honours backslashes, so one
                // containing either is a line for the shell.
                if (inside.Contains('$') || inside.Contains('`') || inside.Contains('\\'))
                {
                    return null;
                }

                word.Append(inside);
                started = true;
                i = end;
                continue;
            }

            if (Meta.Contains(c, StringComparison.Ordinal))
            {
                return null;
            }

            word.Append(c);
            started = true;
        }

        if (started)
        {
            words.Add(word.ToString());
        }

        // An empty line, or one that was only whitespace, has no program to run.
        if (words.Count == 0)
        {
            return null;
        }

        return Builtins.Contains(words[0]) ? null : words;
    }
}
