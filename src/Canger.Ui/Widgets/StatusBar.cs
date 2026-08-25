// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;
using Canger.Vcs;

namespace Canger.Ui.Widgets;

/// <summary>
/// The bottom line: details of the file under the cursor on the left, and where you are in the
/// listing on the right.
/// </summary>
/// <remarks>
/// This also carries transient messages. A message replaces the usual contents until it expires,
/// so errors and confirmations appear where the user is already looking rather than in a dialog.
/// </remarks>
public sealed class StatusBar(IColorScheme colorScheme) : Widget
{
    /// <summary>The tab being shown.</summary>
    public Tab? Tab { get; set; }

    /// <summary>Free space on the current filesystem, when known.</summary>
    public long? FreeBytes { get; set; }

    /// <summary>Whether to show the free space.</summary>
    public bool ShowFreeSpace { get; set; } = true;

    /// <summary>Whether to show the size of the file under the cursor.</summary>
    public bool ShowSize { get; set; } = true;

    /// <summary>A message shown in place of the usual contents.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// What background work is doing, shown in place of the usual contents while it runs.
    /// </summary>
    /// <remarks>
    /// A copy reports its percentage, throughput and remaining time here, so the progress is
    /// visible without opening the task view.
    /// </remarks>
    public string? TaskDescription { get; set; }

    /// <summary>Whether the message reports a problem.</summary>
    public bool MessageIsError { get; set; }

    /// <summary>Progress of outstanding work, drawn as a tint across the bar.</summary>
    public double? Progress { get; set; }

    /// <summary>Whether to draw the progress tint.</summary>
    public bool ShowProgressBar { get; set; } = true;

    /// <summary>The latest commit, when the directory is under version control.</summary>
    public VcsCommit? Head { get; set; }

    /// <summary>How much of the commit summary to show.</summary>
    public int VcsMessageLength { get; set; } = 50;

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        CellStyle baseStyle = colorScheme.Resolve(StyleContext.Of(ContextKey.InStatusbar));
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, 1, baseStyle);

        // A message the user asked for outranks a running task, which will still be there
        // afterwards.
        string? headline = Message is { Length: > 0 } ? Message : TaskDescription;

        if (headline is { Length: > 0 } message)
        {
            CellStyle style = colorScheme.Resolve(
                StyleContext.Of(ContextKey.InStatusbar, ContextKey.Message)
                            .With(MessageIsError, ContextKey.Bad)
                            .With(!MessageIsError, ContextKey.Good));

            screen.Write(Bounds.X, Bounds.Y, new WideString(message).Truncate(Bounds.Width), style);
            return;
        }

        // The right side is measured first so the left can be told where to stop. Both sides
        // grow with what they describe — a long commit summary on one, a marked-file total on
        // the other — and without a limit the longer simply overwrote the shorter.
        int right = DrawRight(screen, baseStyle);
        DrawLeft(screen, baseStyle, Bounds.Right - right);
        DrawProgress(screen);
    }

    /// <summary>Draws permissions, ownership, size and time for the file under the cursor.</summary>
    private void DrawLeft(ScreenBuffer screen, CellStyle baseStyle, int limit)
    {
        if (Tab?.Selected is not { } entry || entry.Status is not { } status)
        {
            return;
        }

        string permissions = PermissionString(status);
        bool ownedByUser = status.Uid == UserDatabase.CurrentUserId;

        CellStyle permissionStyle = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InStatusbar, ContextKey.Permissions)
                        .With(ownedByUser, ContextKey.Good)
                        .With(!ownedByUser, ContextKey.Bad));

        int x = Bounds.X;
        x += screen.Write(x, Bounds.Y, permissions, permissionStyle);
        x += screen.Write(x, Bounds.Y, " ", baseStyle);

        x += screen.Write(x, Bounds.Y,
                          $"{UserDatabase.UserName(status.Uid)} {UserDatabase.GroupName(status.Gid)}",
                          baseStyle);

        if (ShowSize && !entry.IsDirectory)
        {
            x += screen.Write(x, Bounds.Y, " " + HumanReadable.Format(status.Size), baseStyle);
        }

        x += screen.Write(x, Bounds.Y,
                          " " + status.ModifyTime.ToLocalTime()
                                      .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                          baseStyle);

        DrawCommit(screen, x, baseStyle, limit);
    }

    /// <summary>
    /// Draws the latest commit's date and summary after the file's own details.
    /// </summary>
    /// <remarks>
    /// Truncated to <c>vcs_msg_length</c>, because a commit message can be a paragraph and this
    /// is one line shared with everything else. The date and the summary are coloured separately,
    /// which is what the <c>vcsdate</c> and <c>vcscommit</c> contexts exist for.
    /// </remarks>
    private void DrawCommit(ScreenBuffer screen, int x, CellStyle baseStyle, int limit)
    {
        if (Head is not { } head)
        {
            return;
        }

        string date = head.Date.ToLocalTime()
                          .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        // The date alone is worth nothing without room for at least some of the summary, so if
        // it will not all fit, none of it is drawn.
        if (x + date.Length + 3 >= limit)
        {
            return;
        }

        x += screen.Write(x, Bounds.Y, " ", baseStyle);
        x += screen.Write(x, Bounds.Y, date,
                          colorScheme.Resolve(StyleContext.Of(ContextKey.InStatusbar,
                                                              ContextKey.VcsDate)));

        x += screen.Write(x, Bounds.Y, " ", baseStyle);

        int room = Math.Min(VcsMessageLength, Math.Max(limit - x, 0));

        screen.Write(x, Bounds.Y, new WideString(head.Summary).Truncate(room),
                     colorScheme.Resolve(StyleContext.Of(ContextKey.InStatusbar,
                                                         ContextKey.VcsCommit)));
    }

    /// <summary>Draws the position in the listing, and free space.</summary>
    /// <returns>How many cells it used, so the left side knows where to stop.</returns>
    private int DrawRight(ScreenBuffer screen, CellStyle baseStyle)
    {
        if (Tab?.Current is not { } directory)
        {
            return 0;
        }

        List<string> parts = [];

        // Read once. `MarkedEntries` walks the whole filtered listing and builds a new list on
        // every access, and this method used to ask for it four times.
        IReadOnlyList<FsNode> marked = directory.MarkedEntries;

        if (marked.Count > 0)
        {
            parts.Add($"{HumanReadable.Format(MarkedSize(directory, marked))}/" +
                      marked.Count.ToString(CultureInfo.InvariantCulture));

            // Where the position indicator would be. Marks are easy to scroll away from and then
            // forget about, and this is the reminder that some are set (ranger's comment at
            // `gui/widgets/statusbar.py:305-306` says exactly that).
            parts.Add("Mrk");
        }
        else
        {
            string sum = $"{HumanReadable.Format(directory.DiskUsage)} sum";

            parts.Add(ShowFreeSpace && FreeBytes is { } free
                ? $"{sum}, {HumanReadable.Format(free)} free"
                : sum);

            parts.Add(directory.Count == 0
                ? "0/0"
                : $"{(directory.Cursor.Index + 1).ToString(CultureInfo.InvariantCulture)}/" +
                  $"{directory.Count.ToString(CultureInfo.InvariantCulture)}");

            parts.Add(ScrollIndicator(directory));
        }

        string text = string.Join("  ", parts) + " ";
        int width = CellWidth.Of(text);
        if (width >= Bounds.Width)
        {
            return 0;
        }

        CellStyle style = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InStatusbar, ContextKey.Scroll)
                        .With(marked.Count > 0, ContextKey.Marked));

        screen.Write(Bounds.Right - width, Bounds.Y, text, style);
        return width;
    }

    /// <summary>The total size of what is marked.</summary>
    /// <param name="directory">The listing the marks are in.</param>
    /// <param name="marked">Its marked entries.</param>
    /// <returns>Their combined size in bytes.</returns>
    /// <remarks>
    /// <para>
    /// A directory counts only once <c>:get_cumulative_size</c> (<c>dc</c>) has measured it.
    /// Until then there is no answer to give: a directory has no byte size of its own, and
    /// <see cref="FsNode.Size"/> reports its entry count instead — so summing that produced
    /// "16 B marked" for two folders holding sixteen things between them. Ranger draws the same
    /// line, with the same condition, at <c>gui/widgets/statusbar.py:288-289</c>.
    /// </para>
    /// <para>
    /// With everything marked the directory's own total is used instead, which is ranger's
    /// shortcut at <c>statusbar.py:285-286</c> — the same figure the unmarked line shows, so the
    /// number does not appear to change when you press <c>v</c>.
    /// </para>
    /// </remarks>
    private static long MarkedSize(DirectoryNode directory, IReadOnlyList<FsNode> marked)
    {
        if (marked.Count == directory.Count)
        {
            return directory.DiskUsage;
        }

        long total = 0;

        foreach (FsNode entry in marked)
        {
            if (!entry.IsDirectory)
            {
                total += entry.Size ?? 0;
            }
            else if (entry.CumulativeSize is { } measured)
            {
                total += measured;
            }
        }

        return total;
    }

    /// <summary>Whether the whole listing is visible, or how far down it the cursor is.</summary>
    private static string ScrollIndicator(DirectoryNode directory)
    {
        if (directory.Count == 0)
        {
            return "All";
        }

        if (directory.Cursor.Index == 0)
        {
            return "Top";
        }

        if (directory.Cursor.Index >= directory.Count - 1)
        {
            return "Bot";
        }

        int percent = directory.Cursor.Index * 100 / Math.Max(directory.Count - 1, 1);
        return percent.ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Tints the left of the bar in proportion to outstanding work.</summary>
    private void DrawProgress(ScreenBuffer screen)
    {
        if (!ShowProgressBar || Progress is not { } progress || progress is <= 0 or >= 1)
        {
            return;
        }

        int width = (int)(Bounds.Width * progress);
        if (width > 0)
        {
            screen.Recolor(Bounds.X, Bounds.Y, width,
                           colorScheme.Resolve(
                               StyleContext.Of(ContextKey.InStatusbar, ContextKey.Loaded)));
        }
    }

    /// <summary>Renders the mode bits the way <c>ls -l</c> does.</summary>
    /// <param name="status">The file's metadata.</param>
    /// <returns>A ten-character permission string.</returns>
    public static string PermissionString(FileStatus status)
    {
        char type = status.Kind switch
        {
            FileKind.Directory => 'd',
            FileKind.SymbolicLink => 'l',
            FileKind.Fifo => 'p',
            FileKind.Socket => 's',
            FileKind.CharacterDevice => 'c',
            FileKind.BlockDevice => 'b',
            _ => '-',
        };

        UnixFileMode mode = status.Permissions;

        return string.Create(10, (type, mode), static (span, state) =>
        {
            (char kind, UnixFileMode bits) = state;
            span[0] = kind;
            span[1] = bits.HasFlag(UnixFileMode.UserRead) ? 'r' : '-';
            span[2] = bits.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-';
            span[3] = bits.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-';
            span[4] = bits.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-';
            span[5] = bits.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-';
            span[6] = bits.HasFlag(UnixFileMode.GroupExecute) ? 'x' : '-';
            span[7] = bits.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-';
            span[8] = bits.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-';
            span[9] = bits.HasFlag(UnixFileMode.OtherExecute) ? 'x' : '-';
        });
    }

}
