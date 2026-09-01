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

        // The cursor moves first, and whatever the flags say. Ranger opens its `execute` with
        // `count = self._count(move=True)` before it marks, filters or anything else — so
        // `:mark foo` puts the cursor on the first match as well as marking the rest, and `fm`
        // takes you to what you searched for. Canger moved only when it was doing nothing else,
        // so marking left the cursor where it was and the search appeared to have missed.
        bool found = MoveToFirstMatch(pattern, flags);

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

        if (!found)
        {
            FileManager.Notify($"no match: {pattern}");
        }
    }

    /// <inheritdoc />
    public override bool Quick()
    {
        (string flags, string pattern) = Line.ParseFlags();

        // 't' means act as the user types. Without it nothing happens until Enter.
        if (!flags.Contains('t', StringComparison.Ordinal))
        {
            return false;
        }

        DirectoryNode directory = FileManager.CurrentDirectory;

        // Whether the listing narrows, which is *not* the same question as whether to act as the
        // user types. Ranger narrows only for 'f' (a temporary filter) or for 'p' with 't' (a
        // permanent one) — `config/commands.py`, `scout.quick`. Canger narrowed whenever 't' was
        // set, which is every one of these aliases, so `find` and `search_inc` hid the listing
        // they were meant to be walking. Both are ranger's most-used searches:
        //
        //     find       scout -aets     no 'f', no 'p' — moves the cursor, never narrows
        //     search_inc scout -rts      the same
        //     travel     scout -aefklst  'f', so it narrows *and* moves
        //     filter     scout -prts     'p' with 't', so it narrows
        //
        // Both narrowing kinds go through `PreviewFilter` here, which is Canger's one live-filter
        // mechanism; 'p' is made permanent by `Execute` pushing it onto the filter stack when the
        // prompt is accepted, so a cancelled `:filter` still leaves nothing behind.
        bool narrows = flags.Contains('f', StringComparison.Ordinal) ||
                       (flags.Contains('p', StringComparison.Ordinal) &&
                        flags.Contains('t', StringComparison.Ordinal));

        if (narrows)
        {
            _applied = BuildFilter(pattern, flags);
            directory.PreviewFilter = _applied;
            directory.Refilter();
        }

        // The cursor follows the pattern as it is typed. Ranger counts and moves in one pass —
        // `self._count(move=asyoutype)` — so the count is over whatever the listing is *now*,
        // after any narrowing above.
        int matches = CountMatches(pattern, flags, move: true);

        // 'a' closes the prompt as soon as exactly one thing matches, so a unique match needs no
        // Enter. That is what makes travelling through a deep tree quick. The test is the number
        // of *matches*, not the number of rows left: without narrowing the listing never shrinks,
        // so counting rows meant `find` could only ever auto-open in a directory of one file.
        return matches == 1 && flags.Contains('a', StringComparison.Ordinal);
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

    private bool MoveToFirstMatch(string pattern, string flags) =>
        CountMatches(pattern, flags, move: true) > 0;

    /// <summary>Counts what the pattern matches, and optionally goes to the first of them.</summary>
    /// <param name="pattern">The pattern as typed.</param>
    /// <param name="flags">The scout flags, which decide how the pattern is read.</param>
    /// <param name="move">Whether the cursor moves to the first match.</param>
    /// <returns>The number of matches, counted no further than two.</returns>
    /// <remarks>
    /// <para>
    /// Ranger's <c>_count</c>, which is one pass doing both jobs: it rotates the listing to start
    /// at the cursor, moves there on the first match when asked, and stops as soon as it knows
    /// there is more than one — the only two answers any caller needs are "exactly one" and "more
    /// than one".
    /// </para>
    /// <para>
    /// An empty pattern and a lone <c>.</c> count nothing, as they do there. Ranger also counts
    /// <c>..</c> as exactly one, which with <c>-a</c> closes the prompt on a unique match; that is
    /// not copied, because no listing here holds an entry called <c>..</c>, so the prompt would
    /// close and the command that followed would report finding nothing.
    /// </para>
    /// </remarks>
    private int CountMatches(string pattern, string flags, bool move)
    {
        DirectoryNode directory = FileManager.CurrentDirectory;
        IReadOnlyList<FsNode> entries = directory.Entries;

        if (entries.Count == 0 || pattern.Length == 0 ||
            string.Equals(pattern, ".", StringComparison.Ordinal))
        {
            return 0;
        }

        IFileFilter filter = BuildFilter(pattern, flags);

        // From where the cursor is, wrapping — not from the top of the listing. Ranger rotates
        // the entries by the cursor's position before looking (`_count`), so a search finds the
        // next match rather than jumping backwards to an earlier one, and searching for what you
        // are already standing on leaves you there. Starting at the top instead would walk
        // backwards every time `n` was pressed on a pattern with a match above.
        int from = directory.Cursor.Index;

        int count = 0;

        for (int step = 0; step < entries.Count; step++)
        {
            FsNode candidate = entries[(from + step) % entries.Count];

            if (!filter.Accepts(candidate))
            {
                continue;
            }

            count++;

            if (move && count == 1)
            {
                FileManager.CurrentTab.MoveCursorTo(candidate);
            }

            if (count > 1)
            {
                return count;
            }
        }

        return count;
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
