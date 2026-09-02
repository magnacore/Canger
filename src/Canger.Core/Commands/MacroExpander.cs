// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.Input;
using Canger.Core.Model;

namespace Canger.Core.Commands;

/// <summary>
/// Replaces the <c>%</c> placeholders in a command line with the files and paths they stand for.
/// </summary>
/// <remarks>
/// <para>
/// Macros are what let a key binding be written once and mean something different every time it
/// fires. <c>map DD shell rm -rf %s</c> works because <c>%s</c> is resolved against the selection
/// at the moment the key is pressed, not when the binding was read.
/// </para>
/// <para>
/// A macro that cannot be resolved — <c>%c</c> with an empty copy buffer, <c>%5d</c> with no
/// fifth tab — aborts the whole line rather than expanding to nothing. Silently running
/// <c>rm -rf</c> with no arguments is the kind of accident this prevents.
/// </para>
/// </remarks>
public sealed class MacroExpander(IFileManager fileManager)
{
    private readonly IFileManager _fileManager =
        fileManager ?? throw new ArgumentNullException(nameof(fileManager));

    /// <summary>
    /// Expands the macros in a line.
    /// </summary>
    /// <param name="line">The line as written.</param>
    /// <param name="wildcards">Keys captured by <c>&lt;any&gt;</c> in the binding that fired.</param>
    /// <param name="escapeForShell">
    /// Whether to quote each value so it survives the shell as a single argument. Commands that
    /// pass their line to a shell must set this, or a filename containing a space or a quote
    /// becomes several arguments — or another command.
    /// </param>
    /// <returns>The expanded line.</returns>
    /// <exception cref="MacroException">A macro had no value.</exception>
    public string Expand(string line, IReadOnlyList<int>? wildcards = null,
                         bool escapeForShell = false)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!line.Contains('%', StringComparison.Ordinal))
        {
            return line;
        }

        StringBuilder result = new(line.Length);
        int index = 0;

        while (index < line.Length)
        {
            char current = line[index];

            if (current != '%')
            {
                result.Append(current);
                index++;
                continue;
            }

            // "%%" is a literal per cent, so a line can contain one without being expanded.
            if (index + 1 < line.Length && line[index + 1] == '%')
            {
                result.Append('%');
                index += 2;
                continue;
            }

            (IReadOnlyList<string>? value, int length) = ReadMacro(line, index + 1, wildcards);

            if (length == 0)
            {
                result.Append('%');
                index++;
                continue;
            }

            if (value is null)
            {
                throw new MacroException(
                    $"'%{line.Substring(index + 1, length)}' has no value here");
            }

            // Quote each value separately, so a filename containing a space stays one argument
            // rather than becoming two.
            result.Append(escapeForShell
                ? string.Join(" ", value.Select(ShellQuote))
                : string.Join(" ", value));

            index += 1 + length;
        }

        return result.ToString();
    }

    /// <summary>Reads one macro name and resolves it.</summary>
    /// <returns>Its value, and how many characters the name occupied.</returns>
    private (IReadOnlyList<string>? Value, int Length) ReadMacro(string line, int start,
                                                  IReadOnlyList<int>? wildcards)
    {
        if (start >= line.Length)
        {
            return (null, 0);
        }

        // A digit selects a tab: %1d is the first tab's directory.
        if (char.IsAsciiDigit(line[start]) && start + 1 < line.Length)
        {
            int tabNumber = line[start] - '0';
            return (ResolveTabMacro(line[start + 1], tabNumber), 2);
        }

        // The longest names are checked first, so "any_path" is not read as "any".
        foreach (string name in (string[])["any_path", "cangerdir", "confdir", "datadir", "space",
                                           "any"])
        {
            if (line.AsSpan(start).StartsWith(name, StringComparison.Ordinal))
            {
                // A trailing digit selects which wildcard: %any1 is the second captured key.
                int digitAt = start + name.Length;
                int? which = digitAt < line.Length && char.IsAsciiDigit(line[digitAt])
                    ? line[digitAt] - '0'
                    : null;

                IReadOnlyList<string>? value = ResolveNamedMacro(name, which ?? 0, wildcards);
                return (value, name.Length + (which is null ? 0 : 1));
            }
        }

        return (ResolveSingleMacro(line[start]), 1);
    }

    /// <summary>Resolves a one-letter macro.</summary>
    private IReadOnlyList<string>? ResolveSingleMacro(char macro) => macro switch
    {
        // The file under the cursor, as displayed.
        'f' => Single(_fileManager.CurrentFile?.RelativePath),

        // The selection, as displayed names.
        's' => Many(_fileManager.Selection.Select(f => f.RelativePath)),

        // The selection, as absolute paths.
        'p' => Many(_fileManager.Selection.Select(f => f.Path)),

        // The copy buffer, as absolute paths.
        'c' => Many(_fileManager.CopyBuffer),

        // The current directory.
        'd' => Single(_fileManager.CurrentDirectory.Path),

        // The tagged files in the current directory.
        't' => Many(_fileManager.CurrentDirectory.Entries
                        .Where(e => e.IsMarked)
                        .Select(e => e.RelativePath)),

        // The next tab's directory and selection, for moving things between two panes.
        'D' => Single(NextTab()?.Current.Path),
        'F' => Single(NextTab()?.Selected?.Path),
        'P' => NextTab() is { } tab ? Many(tab.Selection.Select(f => f.Path)) : null,
        'S' => NextTab() is { } tab2 ? Many(tab2.Selection.Select(f => f.RelativePath)) : null,

        _ => null,
    };

    /// <summary>Resolves a macro naming a particular tab, such as <c>%2d</c>.</summary>
    private IReadOnlyList<string>? ResolveTabMacro(char macro, int tabNumber)
    {
        if (!_fileManager.Tabs.TryGetValue(tabNumber, out Tab? tab))
        {
            return null;
        }

        return macro switch
        {
            'd' => Single(tab.Current.Path),
            'f' => Single(tab.Selected?.Path),
            'p' => Many(tab.Selection.Select(f => f.Path)),
            's' => Many(tab.Selection.Select(f => f.RelativePath)),
            _ => null,
        };
    }

    /// <summary>Resolves a spelled-out macro.</summary>
    private IReadOnlyList<string>? ResolveNamedMacro(
        string name, int which, IReadOnlyList<int>? wildcards)
    {
        switch (name)
        {
            case "space":
                // Configuration lines are split on whitespace, so a literal space in a command
                // has to be written as a macro to survive being read.
                return [" "];

            case "cangerdir":
                return [Configuration.CangerPaths.InstallDirectory];

            case "confdir":
                return [new Configuration.CangerPaths().ConfigDirectory];

            case "datadir":
                return [new Configuration.CangerPaths().DataDirectory];

            case "any":
                return wildcards is not null && which < wildcards.Count
                    ? [KeyCodes.ToDisplayString(wildcards[which])]
                    : null;

            case "any_path":
                // The captured key names a bookmark, and the macro is the path it points to.
                return wildcards is not null && which < wildcards.Count
                    ? Single(ResolveBookmark(wildcards[which]))
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Looks up where a bookmark points, so a binding can act on the key the user pressed.
    /// </summary>
    /// <remarks>
    /// This is what makes <c>map p'&lt;any&gt; paste dest=%any_path</c> work: the captured key
    /// names a bookmark, and the macro becomes the directory it leads to.
    /// </remarks>
    private string? ResolveBookmark(int key) =>
        key is >= 0 and <= char.MaxValue ? _fileManager.Bookmarks.Get((char)key) : null;

    private Tab? NextTab()
    {
        if (_fileManager.Tabs.Count < 2)
        {
            return null;
        }

        int[] numbers = [.. _fileManager.Tabs.Keys.Order()];
        int position = Array.IndexOf(numbers, _fileManager.CurrentTabNumber);

        return position < 0
            ? null
            : _fileManager.Tabs[numbers[(position + 1) % numbers.Length]];
    }

    /// <summary>One value, or nothing when it was absent.</summary>
    private static IReadOnlyList<string>? Single(string? value) => value is null ? null : [value];

    /// <summary>Several values, or nothing when there were none.</summary>
    private static IReadOnlyList<string>? Many(IEnumerable<string> values)
    {
        string[] items = [.. values];
        return items.Length == 0 ? null : items;
    }

    /// <summary>
    /// Wraps a value so a shell treats it as one argument whatever it contains.
    /// </summary>
    /// <remarks>
    /// Single quotes suspend every shell metacharacter, so the only thing needing care is a
    /// single quote itself: the quoting is closed, an escaped quote emitted, and the quoting
    /// reopened. Without this, a file named <c>; rm -rf ~</c> would do exactly what it says
    /// when passed to <c>:shell</c>.
    /// </remarks>
    /// <param name="value">The value to quote.</param>
    /// <returns>The quoted value.</returns>
    public static string ShellQuote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// Puts a value into a command line that will itself be macro-expanded before it runs.
    /// </summary>
    /// <param name="value">The value to embed, usually a filename.</param>
    /// <returns>The value, quoted for the shell and proof against a second expansion.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="ShellQuote"/> is right for a command handed straight to a process runner. It is
    /// <em>not</em> enough for a line built up and then passed to
    /// <c>IFileManager.Execute</c>, because that expands macros over the whole line — including
    /// the part that was just quoted. A per cent in the value is read as the start of a macro,
    /// and the substitution it triggers brings its own quotes, which close the quoting around the
    /// value and leave the rest of it bare.
    /// </para>
    /// <para>
    /// A file called <c>My%20Docs</c> is enough to make such a line fail; a file called
    /// <c>x%sy.txt</c> beside one called <c>;id;.txt</c> is enough to make it run something. So
    /// the per cent is doubled to mean itself, and this is the function to reach for whenever the
    /// result is going to <c>Execute</c> rather than to a runner.
    /// </para>
    /// </remarks>
    public static string QuoteForCommandLine(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return ShellQuote(value).Replace("%", "%%", StringComparison.Ordinal);
    }
}

/// <summary>Raised when a macro has no value, so the line must not run.</summary>
public sealed class MacroException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Which macro failed and why.</param>
    public MacroException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner cause.</summary>
    /// <param name="message">Which macro failed and why.</param>
    /// <param name="innerException">The underlying cause.</param>
    public MacroException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with no detail. Prefer the descriptive overloads.</summary>
    public MacroException() : base("A macro had no value.")
    {
    }
}
