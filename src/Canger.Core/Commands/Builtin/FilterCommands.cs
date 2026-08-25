// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Model.Filters;

using System.Text.RegularExpressions;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// The search-and-narrow command that several of Canger's keys are aliases for.
/// </summary>
/// <remarks>
/// <para>
/// One command covers searching, filtering, marking and jumping because in a file listing they
/// are the same operation with different consequences: find the entries matching a pattern, then
/// do something with them. The flags decide what.
/// </para>
/// <para>
/// The shipped configuration aliases it several ways — <c>filter</c> is <c>scout -prts</c>,
/// <c>find</c> is <c>scout -aets</c> — which is why keys that appear to do quite different things
/// all reach here.
/// </para>
/// </remarks>
[Command("scout", Summary = "Search, filter, mark or jump to matching entries.")]
public sealed class ScoutCommand : CangerCommand
{
    private IFileFilter? _applied;

    /// <inheritdoc />
    public override void Execute()
    {
        (string flags, string pattern) = Line.ParseFlags();

        if (pattern.Length == 0)
        {
            Clear();
            return;
        }

        // Remembered so `n` can repeat it. Ranger does the same at this point
        // (config/commands.py:1607-1608), which is what connects `/` to `search_next`.
        FileManager.CurrentTab.LastSearch = pattern;
        FileManager.SearchMethod = "search";

        // 'p' keeps the filter after the prompt closes; without it the narrowing is only a
        // preview and is undone when the console goes away.
        if (flags.Contains('p', StringComparison.Ordinal))
        {
            DirectoryNode directory = FileManager.CurrentDirectory;
            directory.PreviewFilter = null;
            directory.FilterStack.Push(BuildFilter(pattern, flags));
            directory.Refilter();
            _applied = null;
            return;
        }

        if (flags.Contains('m', StringComparison.Ordinal) ||
            flags.Contains('M', StringComparison.Ordinal))
        {
            MarkMatching(pattern, flags, marked: flags.Contains('m', StringComparison.Ordinal));
            return;
        }

        JumpToFirstMatch(pattern, flags);
    }

    /// <inheritdoc />
    public override bool Quick()
    {
        (string flags, string pattern) = Line.ParseFlags();

        // 't' means act as the user types, which is what makes a filter narrow the listing live.
        if (!flags.Contains('t', StringComparison.Ordinal) || pattern.Length == 0)
        {
            return false;
        }

        DirectoryNode directory = FileManager.CurrentDirectory;
        _applied = BuildFilter(pattern, flags);
        directory.PreviewFilter = _applied;
        directory.Refilter();

        // 'a' closes the prompt as soon as only one entry is left, so a unique match needs no
        // Enter. That is what makes travelling through a deep tree quick.
        return flags.Contains('a', StringComparison.Ordinal) && directory.Count == 1;
    }

    /// <inheritdoc />
    public override void Cancel()
    {
        if (_applied is not null)
        {
            FileManager.CurrentDirectory.PreviewFilter = null;
            FileManager.CurrentDirectory.Refilter();
            _applied = null;
        }
    }

    /// <summary>Builds the filter a pattern and flags describe.</summary>
    /// <remarks>
    /// <para>
    /// A scout pattern is a regular expression, but only the <c>r</c> flag hands it over as one:
    /// otherwise the text is escaped, so a name containing <c>(</c> or <c>.</c> matches itself.
    /// <c>g</c> reads it as a glob and <c>l</c> lets any characters fall between the ones typed.
    /// Ranger's <c>_build_regex</c> (<c>config/commands.py</c>), rule for rule.
    /// </para>
    /// <para>
    /// A leading <c>^</c> and a trailing <c>$</c> are taken off first and put back as anchors
    /// afterwards, whatever the method — which is the part that was missing. Canger matched the
    /// pattern as a plain substring, so <c>scout -m ^Series</c> looked for a literal caret and
    /// marked nothing at all, while still reporting that it had. That is what
    /// <c>file_select_similar</c> issues, and why <c>,</c> announced a pattern and selected
    /// nothing.
    /// </para>
    /// </remarks>
    private static IFileFilter BuildFilter(string pattern, string flags)
    {
        // Ranger's own special case: a lone dot matches everything.
        if (pattern == ".")
        {
            return new NameFilter(string.Empty, ignoreCase: true, display: pattern);
        }

        string body = pattern;
        bool anchorStart = body.StartsWith('^');

        if (anchorStart)
        {
            body = body[1..];
        }

        bool anchorEnd = body.EndsWith('$');

        if (anchorEnd)
        {
            body = body[..^1];
        }

        string core =
            flags.Contains('r', StringComparison.Ordinal) ? body
            : flags.Contains('g', StringComparison.Ordinal)
                ? Regex.Escape(body)
                       .Replace("\\*", ".*", StringComparison.Ordinal)
                       .Replace("\\?", ".", StringComparison.Ordinal)
            : flags.Contains('l', StringComparison.Ordinal)
                ? string.Join(".*", body.Select(c => Regex.Escape(c.ToString())))
            : Regex.Escape(body);

        string source = (anchorStart ? "^" : string.Empty)
                      + core
                      + (anchorEnd ? "$" : string.Empty);

        // Case-sensitive unless asked otherwise: `i` always, `s` only when nothing was typed in
        // capitals — which is what makes a lowercase search forgiving and a capitalised one
        // deliberate.
        bool ignoreCase = flags.Contains('i', StringComparison.Ordinal) ||
                          (flags.Contains('s', StringComparison.Ordinal) && IsAllLower(pattern));

        IFileFilter filter;

        try
        {
            filter = new NameFilter(source, ignoreCase, display: pattern);
        }
        catch (ArgumentException)
        {
            // Half-typed regular expressions are the normal state of a pattern being typed, and
            // ranger falls back to matching everything rather than refusing (`re.error` there).
            filter = new NameFilter(string.Empty, ignoreCase: true, display: pattern);
        }

        // 'v' inverts, which is how "hide" is built from the same command as "filter".
        return flags.Contains('v', StringComparison.Ordinal) ? new NotFilter(filter) : filter;
    }

    /// <summary>Whether a pattern has letters and none of them are capitals.</summary>
    /// <param name="pattern">The pattern as typed.</param>
    /// <returns><see langword="true"/> when smart case should ignore case.</returns>
    /// <remarks>
    /// Python's <c>str.islower()</c>, which wants at least one cased character — so <c>1234</c>
    /// is not "lower" and a smart-case search for it stays case-sensitive, as it does in ranger.
    /// </remarks>
    private static bool IsAllLower(string pattern)
    {
        bool anyCased = false;

        foreach (char c in pattern)
        {
            if (char.IsUpper(c))
            {
                return false;
            }

            anyCased |= char.IsLower(c);
        }

        return anyCased;
    }

    private void MarkMatching(string pattern, string flags, bool marked)
    {
        IFileFilter filter = BuildFilter(pattern, flags);

        foreach (FsNode entry in FileManager.CurrentDirectory.Entries)
        {
            if (filter.Accepts(entry))
            {
                entry.IsMarked = marked;
            }
        }
    }

    private void JumpToFirstMatch(string pattern, string flags)
    {
        IFileFilter filter = BuildFilter(pattern, flags);
        DirectoryNode directory = FileManager.CurrentDirectory;

        FsNode? match = directory.Entries.FirstOrDefault(filter.Accepts);
        if (match is not null)
        {
            FileManager.CurrentTab.MoveCursorTo(match);
        }
        else
        {
            FileManager.Notify($"no match: {pattern}");
        }
    }

    private void Clear()
    {
        DirectoryNode directory = FileManager.CurrentDirectory;
        directory.PreviewFilter = null;
        directory.FilterStack.Clear();
        directory.Refilter();
    }
}

/// <summary>Shows only the entries currently selected.</summary>
[Command("narrow", Summary = "Show only the selected entries.")]
public sealed class NarrowCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        DirectoryNode directory = FileManager.CurrentDirectory;

        // ":narrow False" is how the shipped configuration spells "stop narrowing".
        if (Argument(1).Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            directory.FilterStack.Clear();
            directory.Refilter();
            return;
        }

        directory.FilterStack.Push(
            new NarrowFilter(FileManager.Selection.Select(e => e.RelativePath)));
        directory.ClearMarks();
        directory.Refilter();
    }
}

/// <summary>Manages the stack of filters applied to the current directory.</summary>
[Command("filter_stack", Summary = "Add, remove or combine filters: add, pop, clear, show, ...")]
public sealed class FilterStackCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        DirectoryNode directory = FileManager.CurrentDirectory;
        FilterStack stack = directory.FilterStack;

        switch (Argument(1))
        {
            case "add":
                Add(stack);
                break;

            case "pop":
                stack.Pop();
                break;

            case "clear":
                stack.Clear();
                break;

            case "rotate":
                stack.Rotate(int.TryParse(Argument(2), out int count) ? count : 1);
                break;

            case "decompose":
                stack.Decompose();
                break;

            case "show":
                FileManager.Notify(stack.IsEmpty
                    ? "no filters"
                    : string.Join(" | ", stack.Describe()));
                return;

            default:
                FileManager.Notify($"filter_stack: unknown action: {Argument(1)}", isError: true);
                return;
        }

        directory.Refilter();
    }

    /// <summary>Adds one filter, or combines the two on top of the stack.</summary>
    private void Add(FilterStack stack)
    {
        string kind = Argument(2);
        string argument = Rest(3);

        switch (kind)
        {
            case "name":
                stack.Push(new NameFilter(argument));
                break;

            case "type":
                stack.Push(new InodeTypeFilter(argument));
                break;

            case "or":
                stack.Combine((left, right) => new OrFilter(left, right));
                break;

            case "and":
                stack.Combine((left, right) => new AndFilter(left, right));
                break;

            case "not":
                stack.Negate();
                break;

            default:
                FileManager.Notify($"filter_stack add: unknown filter: {kind}", isError: true);
                break;
        }
    }
}

/// <summary>Restricts the listing to particular kinds of entry.</summary>
[Command("filter_inode_type", Summary = "Show only directories (d), files (f) or links (l).")]
public sealed class FilterInodeTypeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        DirectoryNode directory = FileManager.CurrentDirectory;
        string types = Argument(1);

        if (types.Length == 0)
        {
            directory.FilterStack.Clear();
        }
        else
        {
            directory.FilterStack.Push(new InodeTypeFilter(types));
        }

        directory.Refilter();
    }
}

/// <summary>Folds the contents of subdirectories into the listing.</summary>
[Command("flat", Summary = "Fold subdirectories into the listing: flat N, or flat -1 for all.")]
public sealed class FlatCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!int.TryParse(Argument(1), out int level))
        {
            FileManager.Notify("flat: how many levels?", isError: true);
            return;
        }

        FileManager.CurrentDirectory.SetFlatLevel(level);
    }
}
