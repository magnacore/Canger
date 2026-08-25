// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;

namespace Canger.Core.Commands.Builtin;

/// <summary>Jumps to a bookmarked directory.</summary>
[Command("enter_bookmark", Summary = "Jump to the directory a bookmark key points at.")]
public sealed class EnterBookmarkCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (Argument(1) is not { Length: > 0 } argument)
        {
            FileManager.Notify("which bookmark?", isError: true);
            return;
        }

        char key = argument[0];

        if (FileManager.Bookmarks.Get(key) is not { } destination)
        {
            FileManager.Notify($"no bookmark on '{key}'", isError: true);
            return;
        }

        if (!FileManager.FileSystem.DirectoryExists(destination))
        {
            FileManager.Notify($"bookmark '{key}' points at nothing: {destination}", isError: true);
            return;
        }

        FileManager.CurrentTab.Enter(destination);
    }
}

/// <summary>Points a bookmark key at the current directory.</summary>
[Command("set_bookmark", Summary = "Point a bookmark key at the current directory.")]
public sealed class SetBookmarkCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (Argument(1) is not { Length: > 0 } argument)
        {
            FileManager.Notify("which key?", isError: true);
            return;
        }

        char key = argument[0];

        if (!Bookmarks.IsValidKey(key))
        {
            FileManager.Notify($"'{key}' cannot hold a bookmark", isError: true);
            return;
        }

        FileManager.Bookmarks.Set(key, FileManager.CurrentDirectory.Path);
        FileManager.Notify($"bookmark '{key}' set to {FileManager.CurrentDirectory.Path}");
    }
}

/// <summary>Clears a bookmark key.</summary>
[Command("unset_bookmark", Summary = "Clear a bookmark key.")]
public sealed class UnsetBookmarkCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (Argument(1) is not { Length: > 0 } argument)
        {
            return;
        }

        FileManager.Notify(FileManager.Bookmarks.Remove(argument[0])
            ? $"bookmark '{argument[0]}' cleared"
            : $"no bookmark on '{argument[0]}'");
    }
}

/// <summary>Shows the bookmark list.</summary>
/// <remarks>
/// <para>
/// Bound as <c>map '&lt;bg&gt; draw_bookmarks</c> and <c>map m&lt;bg&gt; draw_bookmarks</c>:
/// <c>&lt;bg&gt;</c> means "run this while the sequence is still pending and keep listening", so
/// the list appears the moment <c>'</c> is pressed and the next key chooses from it.
/// </para>
/// <para>
/// This used to write the bookmarks onto the status line, all of them on one row, shortened to
/// fit. With more than a handful that is unreadable, and it is not what ranger does: ranger draws
/// a proper list over the bottom of the listing, one bookmark per row, under a heading.
/// </para>
/// </remarks>
[Command("draw_bookmarks", Summary = "Show the bookmarks.")]
public sealed class DrawBookmarksCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.Bookmarks.Entries.Count == 0)
        {
            FileManager.Notify("no bookmarks");
            return;
        }

        FileManager.ShowBookmarks();
    }
}

/// <summary>Adds or removes a persistent tag on the selection.</summary>
/// <remarks>
/// Tags are recorded against <see cref="FsNode.RealPath"/>, following ranger
/// (<c>core/actions.py:880</c>): tagging a symbolic link tags what it points at, so the mark is
/// there however the file is reached.
/// </remarks>
[Command("tag_toggle", Summary = "Add or remove a tag on the selection.")]
public sealed class TagToggleCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        char tag = TagFrom(Line) ?? Tags.DefaultTag;

        FileManager.Tags.Toggle([.. FileManager.Selection.Select(e => e.RealPath)], tag);
        AdvanceIfSingle(FileManager);
    }

    /// <summary>Reads a <c>tag=x</c> argument, which is how a binding names the tag.</summary>
    internal static char? TagFrom(CommandLine line)
    {
        for (int i = 1; i < line.Count; i++)
        {
            string word = line.Word(i);

            if (word.StartsWith("tag=", StringComparison.Ordinal) && word.Length > 4)
            {
                return word[4];
            }

            if (word.Length == 1)
            {
                return word[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Moves on after tagging one file, so a run of files can be tagged by holding a key.
    /// </summary>
    internal static void AdvanceIfSingle(IFileManager fileManager)
    {
        if (fileManager.CurrentDirectory.MarkedEntries.Count == 0)
        {
            fileManager.CurrentTab.MoveCursor(fileManager.CurrentDirectory.Cursor.Index + 1);
        }
    }
}

/// <summary>Tags the selection.</summary>
[Command("tag_add", Summary = "Tag the selection.")]
public sealed class TagAddCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Tags.Add(
            [.. FileManager.Selection.Select(e => e.RealPath)],
            TagToggleCommand.TagFrom(Line) ?? Tags.DefaultTag);

        TagToggleCommand.AdvanceIfSingle(FileManager);
    }
}

/// <summary>Removes tags from the selection.</summary>
[Command("tag_remove", Summary = "Remove tags from the selection.")]
public sealed class TagRemoveCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Tags.Remove([.. FileManager.Selection.Select(e => e.RealPath)]);
        TagToggleCommand.AdvanceIfSingle(FileManager);
    }
}

/// <summary>Marks every tagged entry in the current directory.</summary>
[Command("mark_tag", Summary = "Mark the entries carrying a tag.")]
public sealed class MarkTagCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Apply(FileManager, Line, marked: true);

    /// <summary>Marks or unmarks the entries carrying the named tags.</summary>
    internal static void Apply(IFileManager fileManager, CommandLine line, bool marked)
    {
        char[] wanted = [.. line.Rest(1).Where(Tags.IsValidTag)];

        foreach (FsNode entry in fileManager.CurrentDirectory.Entries)
        {
            if (fileManager.Tags.TagOf(entry.RealPath) is not { } tag)
            {
                continue;
            }

            if (wanted.Length == 0 || wanted.Contains(tag))
            {
                entry.IsMarked = marked;
            }
        }
    }
}

/// <summary>Unmarks every tagged entry in the current directory.</summary>
[Command("unmark_tag", Summary = "Unmark the entries carrying a tag.")]
public sealed class UnmarkTagCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => MarkTagCommand.Apply(FileManager, Line, marked: false);
}
