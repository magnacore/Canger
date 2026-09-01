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

            // Underneath the line, not instead of it. This returned here, so the one moment the
            // bar had something to say — a copy running, its description filling the bar — was
            // the moment it was skipped. Ranger tints after printing, over whatever is there
            // (`gui/widgets/statusbar.py:332-341`).
            //
            // Not under a message the user asked for, though: ranger draws those through
            // `_draw_message`, which does no tinting, and a notice about something that has
            // already happened is not progress.
            if (Message is not { Length: > 0 })
            {
                DrawProgress(screen);
            }

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

        // `l` for a link, with the permission bits of whatever it points at. That is what ranger
        // shows — the type character comes from `is_link` while the bits come from the followed
        // stat (`container/fsobject.py:347-358`) — and it is why a linked directory reads
        // `lrwxr-xr-x` there and read `drwxr-xr-x` here, saying nothing about being a link.
        string permissions = PermissionString(status, entry.IsSymbolicLink);
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

        // Where it points, in place of the size and the date rather than beside them. Ranger
        // makes the same trade (`gui/widgets/statusbar.py:180-186`): for a link the destination
        // is the one fact worth the room, and the size and time belong to the target and are
        // already a keystroke away.
        if (entry.IsSymbolicLink)
        {
            CellStyle linkStyle = colorScheme.Resolve(
                StyleContext.Of(ContextKey.InStatusbar, ContextKey.Link)
                            .With(!entry.IsBrokenSymbolicLink, ContextKey.Good)
                            .With(entry.IsBrokenSymbolicLink, ContextKey.Bad));

            // A question mark when the link cannot be read, as ranger does — the row still says
            // that it is a link and that where it goes could not be found out.
            x += screen.Write(x, Bounds.Y, " -> " + (entry.LinkTarget ?? "?"), linkStyle);
        }
        else
        {
            if (ShowSize && !entry.IsDirectory)
            {
                x += screen.Write(x, Bounds.Y, " " + Size(status.Size), baseStyle);
            }

            x += screen.Write(x, Bounds.Y,
                              " " + status.ModifyTime.ToLocalTime()
                                          .ToString("yyyy-MM-dd HH:mm",
                                                    CultureInfo.InvariantCulture),
                              baseStyle);
        }

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

        // Each piece carries its own colour, which is the whole point: ranger's `right.add(text,
        // *contexts)` tags every fragment separately and `_print_result` recolours between them
        // (`gui/widgets/statusbar.py:253-327`). Writing the joined line in one style instead
        // spreads whichever flag is set across everything beside it — and `marked` is
        // `bold | reverse`, so a single mark turned the entire right-hand side into a solid block.
        List<(string Text, StyleContext Context)> parts = [];

        // Read once. `MarkedEntries` walks the whole filtered listing and builds a new list on
        // every access, and this method used to ask for it four times.
        IReadOnlyList<FsNode> marked = directory.MarkedEntries;

        // Sizes are plain text in ranger — `right.add(...)` with no contexts at all. Only the
        // indicators beside them are flagged, so the eye goes to the flag and not the arithmetic.
        StyleContext plain = StyleContext.Of(ContextKey.InStatusbar);
        StyleContext scroll = plain.With(ContextKey.Scroll);

        if (marked.Count > 0)
        {
            parts.Add(($"{Size(MarkedSize(directory, marked))}/" +
                       marked.Count.ToString(CultureInfo.InvariantCulture), plain));

            // Where the position indicator would be. Marks are easy to scroll away from and then
            // forget about, and this is the reminder that some are set (ranger's comment at
            // `gui/widgets/statusbar.py:305-306` says exactly that).
            parts.Add(("Mrk", scroll.With(ContextKey.Marked)));
        }
        else
        {
            string sum = $"{Size(directory.DiskUsage)} sum";

            parts.Add((ShowFreeSpace && FreeBytes is { } free
                ? $"{sum}, {Size(free)} free"
                : sum, plain));

            parts.Add((directory.Count == 0
                ? "0/0"
                : $"{(directory.Cursor.Index + 1).ToString(CultureInfo.InvariantCulture)}/" +
                  $"{directory.Count.ToString(CultureInfo.InvariantCulture)}", scroll));

            (string Text, ContextKey Key) indicator = ScrollIndicator(directory);
            parts.Add((indicator.Text, scroll.With(indicator.Key)));
        }

        // Beside the mark count, because that is what it governs: while this is showing, the
        // selection is a live range and anything appearing between its ends joins it. Ranger has
        // no counterpart — it shows the mode on the left, in place of the permissions — so it
        // borrows `marked`, being the same kind of statement about the same set of files.
        if (IsVisualMode)
        {
            parts.Add((IsVisualReverse ? "UNVIS" : "VIS", scroll.With(ContextKey.Marked)));
        }

        // Said plainly, because a listing that has stopped updating is indistinguishable from a
        // broken one. Ranger puts it in the same place (`gui/widgets/statusbar.py:322-325`).
        if (Frozen)
        {
            parts.Add(("FROZEN", scroll.With(ContextKey.Frozen)));
        }

        // Measured before anything is drawn: a right-aligned line has to know its full width to
        // find its own starting column, and a partial one would be worse than none.
        const string Separator = "  ";
        int width = CellWidth.Of(Separator) * (parts.Count - 1) + 1;
        foreach ((string text, StyleContext _) in parts)
        {
            width += CellWidth.Of(text);
        }

        if (width >= Bounds.Width)
        {
            return 0;
        }

        int x = Bounds.Right - width;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                x += screen.Write(x, Bounds.Y, Separator, baseStyle);
            }

            x += screen.Write(x, Bounds.Y, parts[i].Text, colorScheme.Resolve(parts[i].Context));
        }

        // The margin that keeps the last indicator off the right edge. Unstyled, so a mark does
        // not trail a stripe of colour past the word it belongs to.
        screen.Write(x, Bounds.Y, " ", baseStyle);
        return width;
    }

    /// <summary>
    /// Whether a visual selection is open, which the bar says out loud.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visual mode marks a <em>range</em> — everything between the anchor and the cursor — swept
    /// afresh each time the cursor moves. It is not swept at any other time: a file appearing
    /// between the two ends while the cursor sits still does not join the selection. This comment
    /// once claimed the opposite was ranger's behaviour and therefore right; it is not. Ranger
    /// sweeps from inside <c>move</c> (<c>core/actions.py:522-559</c>) and nowhere else, so a
    /// listing that changes on its own never re-marks anything.
    /// </para>
    /// <para>
    /// Ranger shows no sign that the mode is open — its status bar knows about the filter, the
    /// marks, the position and frozen files, but never the mode. Worth a word of width here all
    /// the same: with a range still live, the next movement extends it, and a later <c>dD</c>
    /// acts on everything it has swept rather than on what was chosen deliberately.
    /// </para>
    /// </remarks>
    public bool IsVisualMode { get; set; }

    /// <summary>Whether that selection unmarks as it moves, which <c>uV</c> starts.</summary>
    /// <remarks>
    /// Shown as <c>UNVIS</c>, following the <c>u</c>-means-undo convention the bindings already
    /// use — <c>uv</c> unmarks everything, <c>uV</c> starts a range that unmarks.
    /// </remarks>
    public bool IsVisualReverse { get; set; }

    /// <summary>Whether listings are frozen, which the bar says out loud.</summary>
    public bool Frozen { get; set; }

    /// <summary>Whether to divide by 1024 and use binary prefixes.</summary>
    /// <remarks>The <c>binary_size_prefix</c> setting, which reached the row but not this bar.</remarks>
    public bool BinaryPrefix { get; set; }

    /// <summary>Whether byte counts are written out in full, per <c>size_in_bytes</c>.</summary>
    public bool ExactBytes { get; set; }

    /// <summary>Formats a byte count the way the rest of the interface is formatting them.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The rendered size.</returns>
    private string Size(long bytes) => HumanReadable.Format(bytes, BinaryPrefix, exact: ExactBytes);

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
    /// <param name="directory">The listing being described.</param>
    /// <returns>
    /// The word to show and the context that colours it. They travel together because a scheme is
    /// free to give each of the four its own colour — ranger's own does not, but
    /// <c>gui/widgets/statusbar.py:311-318</c> still tags them separately so one can.
    /// </returns>
    private static (string Text, ContextKey Key) ScrollIndicator(DirectoryNode directory)
    {
        if (directory.Count == 0)
        {
            return ("All", ContextKey.All);
        }

        if (directory.Cursor.Index == 0)
        {
            return ("Top", ContextKey.Top);
        }

        if (directory.Cursor.Index >= directory.Count - 1)
        {
            return ("Bot", ContextKey.Bot);
        }

        int percent = directory.Cursor.Index * 100 / Math.Max(directory.Count - 1, 1);
        return (percent.ToString(CultureInfo.InvariantCulture) + "%", ContextKey.Percentage);
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
    /// <param name="isLink">
    /// Whether the entry is a symbolic link, which decides the type character alone. The
    /// permission bits still come from <paramref name="status"/>, which describes what the link
    /// points at — the same split ranger makes.
    /// </param>
    public static string PermissionString(FileStatus status, bool isLink = false)
    {
        char type = isLink ? 'l' : status.Kind switch
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
