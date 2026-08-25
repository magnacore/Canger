// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.Input;
using Canger.Core.Model;

namespace Canger.Core.Commands.Builtin;

/// <summary>Moves the cursor, or moves between directories.</summary>
/// <remarks>
/// One command covers both because in a file browser they are the same gesture: the arrow keys
/// move within a listing, and the same keys sideways move between listings. The direction
/// arguments decide which.
/// </remarks>
[Command("move", Summary = "Move the cursor, or enter and leave directories.")]
public sealed class MoveCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Direction direction = Direction.FromArguments(
            NamedArguments(),
            cycle: FileManager.Settings.WrapScroll,
            oneIndexed: FileManager.Settings.OneIndexed);

        if (direction.IsHorizontal)
        {
            // Rightward enters a directory or opens a file — the same gesture either way, which
            // is what makes Enter and the right arrow behave as expected on both.
            if (direction.HorizontalSign > 0)
            {
                if (!FileManager.CurrentTab.EnterSelected())
                {
                    OpenCommand.OpenSelection(FileManager, Quantifier ?? 0);
                }
            }
            else if (direction.HorizontalSign < 0)
            {
                FileManager.CurrentTab.GoUp();
            }

            return;
        }

        DirectoryNode directory = FileManager.CurrentDirectory;

        int destination = direction.Move(
            direction.Down,
            over: Quantifier,
            minimum: 0,
            maximum: Math.Max(directory.Count, 1),
            current: directory.Cursor.Index,
            pageSize: Math.Max(FileManager.BrowserHeight, 1));

        FileManager.CurrentTab.MoveCursor(destination);
    }
}

/// <summary>
/// Moves to the next or previous directory alongside this one.
/// </summary>
/// <remarks>
/// This is what <c>]</c> and <c>[</c> do: walk the parent's listing without leaving the depth you
/// are at. The cursor moves in the <em>parent</em> directory and Canger enters whatever it lands
/// on, so a run of sibling folders can be stepped through without going up and back down each
/// time. A non-directory sibling is skipped over rather than stopping the move, since entering it
/// is not possible and stopping there would be a dead end.
/// </remarks>
[Command("move_parent", Summary = "Move to a sibling directory: move_parent <offset>")]
public sealed class MoveParentCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        int offset = int.TryParse(Argument(1), out int parsed) ? parsed : 1;

        // A quantifier multiplies rather than replaces, so 3] moves three siblings along.
        if (Quantifier is { } repeat)
        {
            offset *= repeat;
        }

        if (offset == 0 || FileManager.CurrentTab.Parent is not { } parent)
        {
            return;
        }

        int from = parent.Cursor.Index;
        int step = Math.Sign(offset);

        // Walking one at a time rather than jumping by the offset, so that files in between are
        // stepped over instead of counted. Ranger indexes straight into the parent's listing and
        // silently does nothing when it lands on a file; skipping is what the key is for.
        for (int remaining = Math.Abs(offset), at = from; remaining > 0;)
        {
            at += step;

            if (at < 0 || at >= parent.Count)
            {
                return;
            }

            if (parent.Entries[at] is { IsDirectory: true } sibling)
            {
                remaining--;

                if (remaining == 0)
                {
                    parent.Cursor.MoveTo(at, parent.Entries);
                    FileManager.CurrentTab.Enter(sibling.Path);
                    return;
                }
            }
        }
    }
}

/// <summary>Changes directory.</summary>
/// <remarks>
/// Environment variables are expanded, which is what lets a binding name a path that differs per
/// machine or per user — <c>cd $XDG_MUSIC_DIR</c>, or ranger's
/// <c>eval fm.cd('/run/media/' + os.getenv('USER'))</c> written as <c>cd /run/media/$USER</c>.
/// </remarks>
[Command("cd", Summary = "Change directory; ~ and $VARIABLE are expanded.")]
public sealed class ChangeDirectoryCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string typed = Rest(1).Trim();

        // No argument means home, which is what a bare cd does in a shell.
        string target = typed.Length == 0
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Core.FileSystem.UserPath.Expand(typed, FileManager.CurrentDirectory.Path);

        if (!FileManager.FileSystem.DirectoryExists(target))
        {
            FileManager.Notify($"cd: no such directory: {target}", isError: true);
            return;
        }

        FileManager.CurrentTab.Enter(target);
    }

    /// <summary>
    /// Substitutes environment variables written as <c>$NAME</c> or <c>${NAME}</c>.
    /// </summary>
    /// <param name="path">The path as typed.</param>
    /// <returns>The path with variables replaced.</returns>
    /// <remarks>
    /// An unset variable expands to nothing, as a shell does. That can leave a path that does not
    /// exist, which <c>cd</c> then reports — better than silently going somewhere else.
    /// </remarks>
    internal static string ExpandVariables(string path)
    {
        if (!path.Contains('$', StringComparison.Ordinal))
        {
            return path;
        }

        StringBuilder result = new(path.Length);

        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] != '$')
            {
                result.Append(path[i]);
                continue;
            }

            bool braced = i + 1 < path.Length && path[i + 1] == '{';
            int start = braced ? i + 2 : i + 1;
            int end = start;

            while (end < path.Length &&
                   (braced ? path[end] != '}' : char.IsLetterOrDigit(path[end]) || path[end] == '_'))
            {
                end++;
            }

            // A lone $ or an unclosed brace is not a variable reference; it stays as typed.
            if (end == start || (braced && end >= path.Length))
            {
                result.Append(path[i]);
                continue;
            }

            result.Append(Environment.GetEnvironmentVariable(path[start..end]) ?? string.Empty);
            i = braced ? end : end - 1;
        }

        return result.ToString();
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) =>
        CompleteDirectories(FileManager.Settings.CdBookmarks);
}

/// <summary>Steps back and forward through the directories this tab has visited.</summary>
[Command("history_go", Summary = "Go back or forward in this tab's history.")]
public sealed class HistoryGoCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        int offset = int.TryParse(Argument(1), out int parsed) ? parsed : -1;
        FileManager.CurrentTab.GoInHistory(offset);
    }
}

/// <summary>Re-reads the directory being shown.</summary>
[Command("reload_cwd", Summary = "Re-read the current directory.")]
public sealed class ReloadCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.ReloadCurrentDirectory();
}

/// <summary>Repaints the screen.</summary>
[Command("redraw_window", Summary = "Repaint the screen.")]
public sealed class RedrawCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.Redraw();
}
