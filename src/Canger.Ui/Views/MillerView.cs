// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;
using Canger.Core.Previews;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;
using Canger.Vcs;

namespace Canger.Ui.Views;

/// <summary>
/// The browser's main layout: the path leading here in columns to the left, the current directory
/// in the middle, and room for a preview on the right.
/// </summary>
/// <remarks>
/// Showing the ancestry alongside the current directory is what makes navigation feel like moving
/// through a tree rather than jumping between unrelated listings, and it is the layout the file
/// manager is named for.
/// </remarks>
public sealed class MillerView(IColorScheme colorScheme)
{
    private readonly List<BrowserColumn> _columns = [];
    private readonly Pager _preview = new(colorScheme);

    /// <summary>The relative widths of the columns.</summary>
    public IReadOnlyList<int> ColumnRatios { get; set; } = [1, 3, 4];

    /// <summary>
    /// Which borders to draw: <c>none</c>, <c>outline</c>, <c>separators</c>, or <c>both</c>.
    /// </summary>
    /// <remarks>
    /// Separators make the column boundaries explicit, which matters most when names are long
    /// enough to run to the edge of their column. The outline frames the whole browser, which
    /// helps when Canger shares a screen with something else.
    /// </remarks>
    public string DrawBorders { get; set; } = "none";

    /// <summary>Whether to leave a column of padding on the right when there is no preview.</summary>
    public bool PaddingRight { get; set; } = true;

    /// <summary>How many rows to keep visible above and below the cursor.</summary>
    public int ScrollOffset { get; set; } = 8;

    /// <summary>Whether to show each entry's size.</summary>
    public bool ShowSize { get; set; } = true;

    /// <summary>Decides how each row is rendered.</summary>
    public LinemodeSelector? Linemodes { get; set; }

    /// <summary>How to number the rows: <c>false</c>, <c>absolute</c>, or <c>relative</c>.</summary>
    public string LineNumbers { get; set; } = "false";

    /// <summary>Whether the first row is numbered one rather than zero.</summary>
    public bool OneIndexed { get; set; }

    /// <summary>Whether the cursor's own row shows zero under relative numbering.</summary>
    public bool RelativeCurrentZero { get; set; }

    /// <summary>The tag store, passed to every column for the marker at the left of each row.</summary>
    public Tags? Tags { get; set; }

    /// <summary>Whether columns other than the main one show tag markers.</summary>
    public bool DisplayTagsInAllColumns { get; set; } = true;

    /// <summary>Version-control status, or <see langword="null"/> when it is not wanted.</summary>
    public VcsService? Vcs { get; set; }

    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<BrowserColumn> Columns => _columns;

    /// <summary>Where previews come from, or <see langword="null"/> to show none.</summary>
    public IPreviewProvider? PreviewProvider { get; set; }

    /// <summary>Whether long preview lines wrap.</summary>
    public bool WrapPreviews { get; set; }

    /// <summary>
    /// An image the terminal should draw over the preview column, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Images cannot go into the screen buffer: the terminal draws them itself, from escape
    /// sequences written past the buffer entirely. The view reports where and what, and the
    /// caller arranges it.
    /// </remarks>
    public (string Path, Rect Bounds)? PendingImage { get; private set; }

    /// <summary>
    /// Lays the view out for a tab and draws it.
    /// </summary>
    /// <param name="screen">Where to draw.</param>
    /// <param name="bounds">The region the browser occupies.</param>
    /// <param name="tab">The tab being shown.</param>
    public void Render(ScreenBuffer screen, Rect bounds, Tab tab)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(tab);

        // An outline takes a cell on every side, so the columns are laid out inside it.
        (bool outline, bool separators) = BorderTypes(DrawBorders);
        Rect inner = outline
            ? new Rect(bounds.X + 1, bounds.Y + 1,
                       Math.Max(bounds.Width - 2, 1), Math.Max(bounds.Height - 2, 1))
            : bounds;

        IReadOnlyList<Rect> regions = ComputeColumns(inner, ColumnRatios, PaddingRight);
        EnsureColumnCount(regions.Count);

        // The last region is the preview; the one before it shows the current directory, and the
        // rest walk back up the path. A shallow path leaves the leading columns empty.
        int mainIndex = regions.Count - 2;

        for (int i = 0; i < regions.Count; i++)
        {
            BrowserColumn column = _columns[i];
            int depthFromMain = i - mainIndex;

            column.Layout(regions[i]);
            column.IsMainColumn = depthFromMain == 0;
            column.ScrollOffset = ScrollOffset;

            // Only the main column shows sizes. The ancestry columns are narrow, and spending
            // their width on a size would leave no room for the names they exist to show.
            column.ShowSize = ShowSize && depthFromMain == 0;
            column.Linemodes = Linemodes;
            column.LineNumbers = LineNumbers;
            column.OneIndexed = OneIndexed;
            column.RelativeCurrentZero = RelativeCurrentZero;
            column.Vcs = Vcs;
            column.Tags = Tags;
            column.DisplayTagsInAllColumns = DisplayTagsInAllColumns;

            DirectoryNode? directory = DirectoryFor(tab, depthFromMain);

            // Scanned before it can be drawn — the preview column shows a directory the user has
            // not entered — and re-scanned when it has changed underneath us. Ranger checks the
            // same thing from the same place (`gui/widgets/view_miller.py:98`,
            // `gui/widgets/browsercolumn.py:188`); it costs one statx of the directory itself and
            // is what makes a file created by a forked `cp`, or moved in from another pane, appear
            // without the user having to ask.
            directory?.LoadIfOutdated();

            // The rightmost column previews whatever is selected. A directory is shown as a
            // listing by the column itself; anything else goes to the preview provider.
            if (depthFromMain > 0 && directory is null)
            {
                column.Directory = null;
                RenderPreview(screen, regions[i], tab);
                continue;
            }

            column.Directory = directory;
            column.Render(screen);
        }

        if (outline || separators)
        {
            DrawBorderLines(screen, bounds, regions, outline, separators);
        }
    }

    /// <summary>Reads the <c>draw_borders</c> setting into the two things it actually controls.</summary>
    /// <param name="setting">The setting's value.</param>
    /// <returns>Whether to frame the view, and whether to rule between the columns.</returns>
    private static (bool Outline, bool Separators) BorderTypes(string? setting) =>
        setting?.ToLowerInvariant() switch
        {
            // "true" is accepted for the sake of configurations written before the setting
            // grew its other values.
            "both" or "true" => (true, true),
            "outline" => (true, false),
            "separators" => (false, true),
            _ => (false, false),
        };

    /// <summary>Draws the frame and the rules between columns.</summary>
    /// <param name="screen">Where to draw.</param>
    /// <param name="bounds">The region the browser occupies, outline included.</param>
    /// <param name="regions">Where the columns were laid out.</param>
    /// <param name="outline">Whether to frame the view.</param>
    /// <param name="separators">Whether to rule between the columns.</param>
    private void DrawBorderLines(ScreenBuffer screen, Rect bounds, IReadOnlyList<Rect> regions,
                                 bool outline, bool separators)
    {
        CellStyle style = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InBrowser, ContextKey.Border));

        int right = bounds.Right - 1;
        int bottom = bounds.Bottom - 1;

        if (outline)
        {
            screen.HorizontalLine(bounds.X, bounds.Y, bounds.Width, style);
            screen.HorizontalLine(bounds.X, bottom, bounds.Width, style);
            screen.VerticalLine(bounds.X, bounds.Y, bounds.Height, style);
            screen.VerticalLine(right, bounds.Y, bounds.Height, style);

            screen.Write(bounds.X, bounds.Y, BoxDrawing.TopLeft, style);
            screen.Write(right, bounds.Y, BoxDrawing.TopRight, style);
            screen.Write(bounds.X, bottom, BoxDrawing.BottomLeft, style);
            screen.Write(right, bottom, BoxDrawing.BottomRight, style);
        }

        if (!separators)
        {
            return;
        }

        // A rule goes in the gutter each column leaves at its right; the last column has the
        // frame or the screen edge there instead.
        for (int i = 0; i < regions.Count - 1; i++)
        {
            int x = regions[i].Right;

            if (x <= bounds.X || x >= right)
            {
                continue;
            }

            screen.VerticalLine(x, bounds.Y, bounds.Height, style);

            // Where a rule meets the frame it becomes a tee rather than a crossing.
            if (outline)
            {
                screen.Write(x, bounds.Y, BoxDrawing.TopTee, style);
                screen.Write(x, bottom, BoxDrawing.BottomTee, style);
            }
        }
    }

    /// <summary>Draws a preview of the selected file into the rightmost column.</summary>
    /// <summary>
    /// Scrolls the preview without moving the cursor.
    /// </summary>
    /// <param name="lines">How far, negative for up.</param>
    public void ScrollPreview(int lines) => _preview.ScrollVertically(lines);

    private void RenderPreview(ScreenBuffer screen, Rect bounds, Tab tab)
    {
        if (PreviewProvider is not { } provider || tab.Selected is not { } selected ||
            bounds.IsEmpty)
        {
            return;
        }

        PreviewResult preview = provider.Preview(
            selected.Path, new PreviewSize(bounds.Width, bounds.Height));

        switch (preview.Kind)
        {
            case PreviewKind.Text:
                _preview.Layout(bounds);
                _preview.WrapLines = WrapPreviews;

                // Only when it has actually changed. Setting it returns the pager to the top, so
                // doing it every frame would undo `:scroll_preview` before it could be seen —
                // and re-splitting the text on each repaint is pure waste besides.
                if (!string.Equals(_preview.Text, preview.Text, StringComparison.Ordinal))
                {
                    _preview.SetText(preview.Text);
                }

                _preview.Render(screen);
                break;

            case PreviewKind.Image or PreviewKind.DirectImage when preview.ImagePath is { } image:
                // Left blank in the buffer so the terminal's own image is not painted over.
                PendingImage = (image, bounds);
                break;

            default:
                break;
        }
    }

    /// <summary>Forgets any image left over from the previous frame.</summary>
    public void ClearPendingImage() => PendingImage = null;

    /// <summary>Which directory a column shows, relative to the current one.</summary>
    private static DirectoryNode? DirectoryFor(Tab tab, int depthFromMain)
    {
        if (depthFromMain > 0)
        {
            // To the right of the current directory: the selected entry, when it is a directory —
            // taken from the cache so it is the same node entering it would use, and therefore
            // carries the cursor the user left there.
            return tab.SelectedDirectory;
        }

        // At or left of the current directory: walk back along the path.
        int index = tab.Pathway.Count - 1 + depthFromMain;
        return index >= 0 && index < tab.Pathway.Count ? tab.Pathway[index] : null;
    }

    private void EnsureColumnCount(int count)
    {
        while (_columns.Count < count)
        {
            _columns.Add(new BrowserColumn(colorScheme));
        }

        while (_columns.Count > count)
        {
            _columns.RemoveAt(_columns.Count - 1);
        }
    }

    /// <summary>
    /// Divides the available width between the columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each column is given its share of the width minus one cell, and the next column starts a
    /// full share along. That one-cell difference is the gutter between columns, and it is why
    /// the arithmetic is not simply "width times ratio".
    /// </para>
    /// <para>
    /// The last column absorbs whatever integer division left over, so the columns always fill
    /// the width exactly rather than leaving a ragged edge that shifts as the terminal resizes.
    /// This follows <c>gui/widgets/view_miller.py:208-250</c>.
    /// </para>
    /// </remarks>
    /// <param name="bounds">The region to divide.</param>
    /// <param name="ratios">The relative widths.</param>
    /// <param name="paddingRight">Whether to leave a padding column on the right.</param>
    /// <returns>One region per column, left to right.</returns>
    public static IReadOnlyList<Rect> ComputeColumns(Rect bounds, IReadOnlyList<int> ratios,
                                                     bool paddingRight = true)
    {
        ArgumentNullException.ThrowIfNull(ratios);

        if (ratios.Count == 0 || bounds.IsEmpty)
        {
            return [];
        }

        int total = ratios.Sum();
        if (total <= 0)
        {
            return [];
        }

        List<Rect> regions = [];
        int left = bounds.X;

        for (int i = 0; i < ratios.Count; i++)
        {
            bool isLast = i == ratios.Count - 1;
            int share = (int)((double)ratios[i] / total * bounds.Width);

            // The final column takes the remainder, so rounding never leaves a gap.
            int width = isLast
                ? Math.Max(bounds.Right - left - (paddingRight ? 1 : 0), 0)
                : Math.Max(share - 1, 0);

            regions.Add(new Rect(left, bounds.Y, width, bounds.Height));
            left += share;
        }

        return regions;
    }
}
