// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Core.State;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;
using Canger.Vcs;

namespace Canger.Ui.Widgets;

/// <summary>
/// One column of the browser: a directory's entries, one per row, with the cursor and any marks
/// highlighted.
/// </summary>
/// <remarks>
/// The same widget draws every column. The one showing the current directory is the main column
/// and gets the cursor and the size readout; the columns to its left are the path leading here
/// and are drawn the same way but without those extras.
/// </remarks>
public sealed class BrowserColumn(IColorScheme colorScheme) : Widget
{
    private int _scrollOffset;

    /// <summary>The directory this column shows, or <see langword="null"/> when there is none.</summary>
    public DirectoryNode? Directory { get; set; }

    /// <summary>Whether this is the column the cursor is in.</summary>
    public bool IsMainColumn { get; set; }

    /// <summary>Whether this column belongs to the active pane, in multipane view.</summary>
    public bool IsActivePane { get; set; } = true;

    /// <summary>How many rows to keep visible above and below the cursor.</summary>
    public int ScrollOffset { get; set; } = 8;

    /// <summary>Whether to show each entry's size.</summary>
    public bool ShowSize { get; set; } = true;

    /// <summary>
    /// Decides how each row is rendered. When absent every row shows its size, which is what the
    /// default linemode does anyway.
    /// </summary>
    public LinemodeSelector? Linemodes { get; set; }

    /// <summary>
    /// How to number the rows: <c>false</c>, <c>absolute</c>, or <c>relative</c>.
    /// </summary>
    /// <remarks>
    /// Relative numbering exists to feed the quantifier: the number beside a row is exactly what
    /// to type before <c>j</c> or <c>k</c> to land on it, which is what makes counted movement
    /// usable without counting by eye.
    /// </remarks>
    public string LineNumbers { get; set; } = "false";

    /// <summary>Whether the first row is numbered one rather than zero.</summary>
    public bool OneIndexed { get; set; }

    /// <summary>Whether the cursor's own row shows zero under relative numbering.</summary>
    public bool RelativeCurrentZero { get; set; }

    /// <summary>
    /// The tag store, consulted for the marker at the left of each row.
    /// </summary>
    /// <remarks>
    /// Optional so a column can be drawn without one — the marker cell is then simply blank,
    /// which is also what an untagged file looks like, so the layout does not depend on it.
    /// </remarks>
    public Tags? Tags { get; set; }

    /// <summary>
    /// Whether this column shows tag markers even when it is not the main column.
    /// </summary>
    /// <remarks>
    /// <c>display_tags_in_all_columns</c>. The cell it controls is reserved whether or not the
    /// file is tagged, so tagging something does not shift the whole listing sideways.
    /// </remarks>
    public bool DisplayTagsInAllColumns { get; set; } = true;

    /// <summary>
    /// Version-control status, or <see langword="null"/> when <c>vcs_aware</c> is off.
    /// </summary>
    public VcsService? Vcs { get; set; }

    private DateTimeOffset _renderedAt = DateTimeOffset.Now;

    /// <summary>Which row of the listing is drawn at the top of the column.</summary>
    public int ScrollPosition => _scrollOffset;

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        // One timestamp per frame, so every row agrees on what "today" means and a
        // listing cannot straddle midnight.
        _renderedAt = DateTimeOffset.Now;

        if (Directory is not { } directory)
        {
            return;
        }

        if (!directory.IsLoaded)
        {
            DrawNotice(screen, "...", StyleContext.Of(ContextKey.InBrowser));
            return;
        }

        if (directory.LoadError is not null)
        {
            DrawNotice(screen, "not accessible",
                       StyleContext.Of(ContextKey.InBrowser, ContextKey.Error));
            return;
        }

        if (directory.Count == 0)
        {
            DrawNotice(screen, "empty", StyleContext.Of(ContextKey.InBrowser, ContextKey.Empty));
            return;
        }

        _scrollOffset = ComputeScroll(directory.Cursor.Index, directory.Count, Bounds.Height,
                                      _scrollOffset, ScrollOffset);

        for (int row = 0; row < Bounds.Height; row++)
        {
            int index = _scrollOffset + row;
            if (index >= directory.Count)
            {
                break;
            }

            DrawEntry(screen, directory, directory.Entries[index], index, Bounds.Y + row);
        }
    }

    /// <summary>Draws a short message in place of a listing.</summary>
    private void DrawNotice(ScreenBuffer screen, string text, StyleContext context)
    {
        CellStyle style = colorScheme.Resolve(context);
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, 1, style);
        screen.Write(Bounds.X, Bounds.Y, new WideString(text).Truncate(Bounds.Width), style);
    }

    /// <summary>Draws one entry.</summary>
    private void DrawEntry(ScreenBuffer screen, DirectoryNode directory, FsNode entry, int index,
                           int row)
    {
        // Every column marks its own selected row, not just the main one. Ranger's
        // `_get_index_of_selected_file` returns the column's own directory pointer regardless of
        // which column it is (`browsercolumn.py:453-456`), which is what shows you where you were
        // in the directory you just left — visible in the preview column after going up, and in
        // the ancestry column as the directory you are inside.
        bool isCursor = index == directory.Cursor.Index;
        StyleContext context = ContextFor(entry, isCursor);
        CellStyle style = colorScheme.Resolve(context);

        // Fill first, so the cursor highlight spans the whole row rather than just the text.
        screen.Fill(Bounds.X, row, Bounds.Width, 1, style);

        // The line number takes its space from the left, before anything else is measured.
        int left = Bounds.X;
        int available = Bounds.Width;
        string number = LineNumberFor(directory, index);

        if (number.Length > 0 && available - number.Length > 2)
        {
            screen.Write(left, row, number, colorScheme.Resolve(NumberContext(isCursor)));

            // One more cell for the gap between the number and the tag marker.
            left += number.Length + 1;
            available -= number.Length + 1;
        }

        // The tag marker: one cell, immediately left of the name, showing the tag character or
        // nothing. The cell is claimed whether or not this file is tagged
        // (browsercolumn.py:479-487), which is what keeps names aligned down the column and
        // stops the listing jumping when a tag is added.
        if ((IsMainColumn || DisplayTagsInAllColumns) && available > 2)
        {
            char tag = Tags?.TagOf(entry.RealPath) ?? ' ';

            screen.Write(left, row, tag.ToString(),
                         colorScheme.Resolve(context.With(ContextKey.TagMarker)));

            left += 1;
            available -= 1;
        }

        // The version-control marker sits between the number and the name, where the eye
        // scanning down a listing finds it without having to read across.
        if (Vcs is not null && VcsMarker(entry) is { } marker && available > 3)
        {
            screen.Write(left, row, marker.Text,
                         colorScheme.Resolve(StyleContext.Of(ContextKey.InBrowser,
                                                             ContextKey.VcsFile, marker.Context)));

            left += 2;
            available -= 2;
        }

        // The detail is laid out from the right, so the name gets whatever is left.
        (string title, string detail) = Render(entry);
        string right = ShowSize ? detail : string.Empty;
        int detailWidth = right.Length == 0 ? 0 : new WideString(right).Width + 1;

        // A detail that would leave the name barely legible is dropped instead. The name is
        // what the row is for; a description squeezed in beside one truncated character helps
        // nobody, and the fileinfo linemode routinely produces details longer than the column.
        if (detailWidth > 0 && available - detailWidth <= 2)
        {
            detailWidth = 0;
        }

        int nameWidth = Math.Max(available - detailWidth, 1);
        string name = new WideString(Prefix(entry) + title).Truncate(nameWidth);
        screen.Write(left, row, name, style);

        if (detailWidth > 0)
        {
            screen.Write(Bounds.Right - detailWidth, row, right, style);
        }
    }

    /// <summary>Asks the entry's linemode what to draw on each side of the row.</summary>
    private (string Title, string Detail) Render(FsNode entry)
    {
        if (Linemodes is not { } selector)
        {
            return (entry.RelativePath, SizeText(entry));
        }

        (ILinemode mode, FileMetadata metadata) = selector.Resolve(entry);
        LinemodeContext context = selector.ContextAt(_renderedAt);

        // A mode returning null is declining to fill the space, because only the column knows
        // how much of it there is; it gets the usual size and symbolic-link marker instead.
        return (mode.Title(entry, metadata, context),
                mode.Detail(entry, metadata, context) ?? SizeText(entry));
    }

    /// <summary>
    /// The number shown at the left of a row, right-aligned to a common width.
    /// </summary>
    /// <param name="directory">The listing, which supplies the cursor for relative numbering.</param>
    /// <param name="index">Which entry.</param>
    /// <returns>The text, or an empty string when rows are not numbered.</returns>
    private string LineNumberFor(DirectoryNode directory, int index)
    {
        // Only the main column is numbered: the ancestry columns are narrow, and a number there
        // would mean nothing anyway since the quantifier applies to the cursor's own listing.
        if (!IsMainColumn || LineNumbers is "false" or "")
        {
            return string.Empty;
        }

        int offset = OneIndexed ? 1 : 0;
        int cursor = directory.Cursor.Index;
        int lastVisible = _scrollOffset + Math.Min(Bounds.Height, directory.Entries.Count) - 1;
        int number;
        int widest;

        if (LineNumbers is "relative")
        {
            number = Math.Abs(cursor - index);

            // The cursor's own row shows its absolute position rather than a useless zero,
            // unless the user has asked for the zero.
            if (number == 0 && !RelativeCurrentZero)
            {
                number = cursor + offset;
            }

            // Wide enough for the furthest row either way, and for the cursor's own number.
            widest = Math.Max(cursor - _scrollOffset, lastVisible - cursor);

            if (!RelativeCurrentZero)
            {
                widest = Math.Max(widest, cursor + offset);
            }
        }
        else
        {
            number = index + offset;
            widest = lastVisible + offset;
        }

        return number.ToString(CultureInfo.InvariantCulture)
                     .PadLeft(Digits(widest));
    }

    /// <summary>How many characters a number needs.</summary>
    private static int Digits(int value) =>
        Math.Abs(value).ToString(CultureInfo.InvariantCulture).Length;

    /// <summary>The contexts that colour a line number.</summary>
    private static StyleContext NumberContext(bool isCursor) =>
        isCursor
            ? StyleContext.Of(ContextKey.InBrowser, ContextKey.LineNumber, ContextKey.Selected)
            : StyleContext.Of(ContextKey.InBrowser, ContextKey.LineNumber);

    /// <summary>
    /// The single character standing for an entry's version-control status.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The character and its colour context, or <see langword="null"/> to show nothing.</returns>
    /// <remarks>
    /// A file in sync gets nothing at all. Marking every clean file would fill the column with
    /// noise and hide the handful that actually differ, which is the only reason to look.
    /// </remarks>
    private (string Text, ContextKey Context)? VcsMarker(FsNode entry)
    {
        if (Vcs?.RepositoryFor(Directory?.Path ?? entry.Path) is not { IsLoaded: true } repository)
        {
            return null;
        }

        return repository.StatusOf(entry.Path, entry.IsDirectory) switch
        {
            VcsStatus.Conflict => ("=", ContextKey.VcsConflict),
            VcsStatus.Untracked => ("?", ContextKey.VcsUntracked),
            VcsStatus.Deleted => ("-", ContextKey.VcsChanged),
            VcsStatus.Changed => ("+", ContextKey.VcsChanged),
            VcsStatus.Staged => ("*", ContextKey.VcsChanged),
            VcsStatus.Ignored => ("!", ContextKey.VcsIgnored),
            VcsStatus.Unknown => ("|", ContextKey.VcsUnknown),
            _ => null,
        };
    }

    /// <summary>
    /// What precedes a name: a space for a marked entry, otherwise nothing.
    /// </summary>
    /// <remarks>
    /// This used to be an asterisk, which was wrong twice over. Ranger shifts a marked name one
    /// cell right and colours it (<c>browsercolumn.py:367-369</c>) rather than prefixing a
    /// glyph — and the asterisk it borrowed is the *tag* marker, so the one character a user
    /// looks for to mean "tagged" meant "marked" instead.
    /// </remarks>
    private static string Prefix(FsNode entry) => entry.IsMarked ? " " : "";

    /// <summary>
    /// What is shown on the right of a row: a file's size, or a directory's entry count.
    /// </summary>
    /// <remarks>
    /// A directory the user has not opened is read to count its entries when
    /// <c>automatically_count_files</c> is on, which is a single shallow read per visible row
    /// rather than a walk of the tree. With the setting off, nothing is shown.
    /// </remarks>
    private string SizeText(FsNode entry)
    {
        if (entry.IsBrokenSymbolicLink)
        {
            return "->?";
        }

        // Read from the selector rather than assumed: this path was hard-coded to decimal
        // prefixes and exact-off, so `binary_size_prefix` gave two renderings of the same
        // quantity on one screen and `size_in_bytes` did nothing here at all.
        bool countFiles = Linemodes?.CountFiles ?? true;
        bool binary = Linemodes?.BinaryPrefix ?? false;
        bool exact = Linemodes?.ExactBytes ?? false;

        return entry.IsDirectory
            ? LinemodeText.Size(entry, binary, countFiles, exact)
            : entry.Size is { } size
                ? HumanReadable.Format(size, binary, exact: exact)
                : string.Empty;
    }

    /// <summary>Works out which contexts apply to an entry.</summary>
    private StyleContext ContextFor(FsNode entry, bool isCursor)
    {
        FileKind kind = entry.Status?.Kind ?? FileKind.Unknown;

        return StyleContext.Of(ContextKey.InBrowser)
            .With(IsMainColumn, ContextKey.MainColumn)
            .With(!IsActivePane, ContextKey.InactivePane)
            .With(isCursor, ContextKey.Selected)
            .With(entry.IsMarked, ContextKey.Marked)
            .With(Tags?.Contains(entry.RealPath) ?? false, ContextKey.Tagged)
            .With(entry.IsDirectory, ContextKey.Directory)
            .With(!entry.IsDirectory, ContextKey.File)
            .With(entry.IsExecutable, ContextKey.Executable)

            // What a file is, by name. The colourscheme has had rules for these all along —
            // media magenta, images yellow, archives red — but nothing produced the keys, so
            // every file fell through to the terminal's default and a listing was three colours
            // instead of ranger's eight.
            .With(entry.IsMedia, ContextKey.Media)
            .With(entry.IsImage, ContextKey.Image)
            .With(entry.IsVideo, ContextKey.Video)
            .With(entry.IsAudio, ContextKey.Audio)
            .With(entry.IsContainer, ContextKey.Container)
            .With(entry.IsDocument, ContextKey.Document)
            .With(entry.IsSymbolicLink, ContextKey.Link)
            .With(entry.IsSymbolicLink && !entry.IsBrokenSymbolicLink, ContextKey.Good)
            .With(entry.IsBrokenSymbolicLink, ContextKey.Bad)
            .With(kind == FileKind.Fifo, ContextKey.Fifo)
            .With(kind == FileKind.Socket, ContextKey.Socket)
            .With(kind is FileKind.CharacterDevice or FileKind.BlockDevice, ContextKey.Device);

        // Deliberately no `error` key for an unreadable entry. In the colourscheme `error` paints
        // the background red, and ranger uses it only for the whole-column notice — a listing
        // that cannot be read at all — never for one row (browsercolumn.py:517-551). Setting it
        // here put a red block behind every broken symbolic link, which ranger shows as plain
        // magenta.
    }

    /// <summary>
    /// Decides which row of the listing appears at the top of the column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule, from <c>gui/widgets/browsercolumn.py:553-588</c>, is to leave the view alone
    /// while the cursor stays comfortably inside it, and otherwise to scroll by just enough to
    /// restore the margin. That is what makes moving down a long listing scroll one line at a
    /// time rather than jumping the view around the cursor.
    /// </para>
    /// <para>
    /// When the column is too short to honour the margin on both sides, the cursor is centred
    /// instead, which is the only sensible behaviour in a two-row column.
    /// </para>
    /// </remarks>
    /// <param name="cursor">Which entry the cursor is on.</param>
    /// <param name="count">How many entries there are.</param>
    /// <param name="height">How many rows the column has.</param>
    /// <param name="current">Where the view currently starts.</param>
    /// <param name="margin">How many rows to keep either side of the cursor.</param>
    /// <returns>The row of the listing to draw at the top.</returns>
    public static int ComputeScroll(int cursor, int count, int height, int current, int margin)
    {
        if (height <= 0 || count <= height)
        {
            return 0;
        }

        int maximum = count - height;

        // Too short to keep the margin on both sides, so centre instead.
        if (height / 2 < margin)
        {
            return Math.Clamp(cursor - (height / 2), 0, maximum);
        }

        int start = Math.Clamp(current, 0, maximum);
        int offsetInView = cursor - start;

        if (offsetInView < margin)
        {
            start = cursor - margin;
        }
        else if (offsetInView > height - 1 - margin)
        {
            start = cursor - (height - 1 - margin);
        }

        return Math.Clamp(start, 0, maximum);
    }
}
