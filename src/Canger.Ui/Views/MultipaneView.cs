// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;
using Canger.Vcs;

namespace Canger.Ui.Views;

/// <summary>
/// Shows every tab at once, side by side, rather than the path leading to one of them.
/// </summary>
/// <remarks>
/// The miller view answers "where am I"; this one answers "how do these compare". It is what to
/// switch to when moving files between two places, since both ends are visible and the cursor in
/// each is remembered independently. Every pane is a main column — each shows its own sizes and
/// its own cursor — and the inactive ones are dimmed so the focused pane is never in doubt.
/// </remarks>
/// <param name="colorScheme">Decides what colour everything is drawn in.</param>
public sealed class MultipaneView(IColorScheme colorScheme)
{
    private readonly List<BrowserColumn> _panes = [];

    /// <summary>How many rows to keep visible above and below each pane's cursor.</summary>
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

    /// <inheritdoc cref="BrowserColumn.CopyBuffer"/>
    public IReadOnlySet<string>? CopyBuffer { get; set; }

    /// <inheritdoc cref="BrowserColumn.CopyBufferIsCut"/>
    public bool CopyBufferIsCut { get; set; }

    /// <summary>Whether columns other than the main one show tag markers.</summary>
    public bool DisplayTagsInAllColumns { get; set; } = true;

    /// <summary>Version-control status, or <see langword="null"/> when it is not wanted.</summary>
    public VcsService? Vcs { get; set; }

    /// <summary>
    /// Which borders to draw: <c>none</c>, <c>outline</c>, <c>separators</c>, <c>both</c>, or
    /// <c>active-pane</c>, which frames only the pane that has the focus.
    /// </summary>
    public string DrawBorders { get; set; } = "none";

    /// <summary>The panes, left to right.</summary>
    public IReadOnlyList<BrowserColumn> Panes => _panes;

    /// <summary>
    /// Lays the view out for a set of tabs and draws it.
    /// </summary>
    /// <param name="screen">Where to draw.</param>
    /// <param name="bounds">The region the browser occupies.</param>
    /// <param name="tabs">The tabs, in the order they should appear.</param>
    /// <param name="currentTabNumber">Which tab has the focus.</param>
    public void Render(ScreenBuffer screen, Rect bounds, IReadOnlyDictionary<int, Tab> tabs,
                       int currentTabNumber)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(tabs);

        if (tabs.Count == 0)
        {
            return;
        }

        // "active-pane" frames one pane rather than the view, so it needs no room around
        // the outside — but it does need the same vertical inset, so the frame has somewhere
        // to go above and below the listing.
        (bool outline, bool separators, bool activePane) = BorderTypes(DrawBorders);
        bool inset = outline || activePane;

        Rect inner = inset
            ? new Rect(bounds.X, bounds.Y + 1,
                       bounds.Width, Math.Max(bounds.Height - 2, 1))
            : bounds;

        int[] numbers = [.. tabs.Keys.Order()];
        IReadOnlyList<Rect> regions = ComputePanes(inner, numbers.Length);
        EnsurePaneCount(numbers.Length);

        for (int i = 0; i < numbers.Length; i++)
        {
            BrowserColumn pane = _panes[i];
            Tab tab = tabs[numbers[i]];

            pane.Layout(regions[i]);

            // Every pane is a main column: each shows its own cursor and its own sizes, which is
            // the whole point of seeing them together.
            pane.IsMainColumn = true;
            pane.IsActivePane = numbers[i] == currentTabNumber;
            pane.ScrollOffset = ScrollOffset;
            pane.ShowSize = ShowSize;
            pane.Linemodes = Linemodes;
            pane.LineNumbers = LineNumbers;
            pane.OneIndexed = OneIndexed;
            pane.RelativeCurrentZero = RelativeCurrentZero;
            pane.Vcs = Vcs;
            pane.Tags = Tags;
            pane.CopyBuffer = CopyBuffer;
            pane.CopyBufferIsCut = CopyBufferIsCut;
            pane.DisplayTagsInAllColumns = DisplayTagsInAllColumns;

            // Every pane, every frame: `LoadIfOutdated` handles the not-yet-loaded case itself,
            // and costs one statx when the listing is already current.
            tab.Current.LoadIfOutdated();

            pane.Directory = tab.Current;
            pane.Render(screen);
        }

        if (outline || separators || activePane)
        {
            DrawBorderLines(screen, bounds, regions, numbers, currentTabNumber,
                            outline, separators, activePane);
        }
    }

    /// <summary>
    /// Divides the width into equal panes with a single-cell gutter between them.
    /// </summary>
    /// <param name="bounds">The region to divide.</param>
    /// <param name="count">How many panes.</param>
    /// <returns>Where each pane goes.</returns>
    /// <remarks>
    /// Equal shares rather than the miller view's ratios: no pane is more important than another
    /// here, and a tab that happened to be opened second should not be harder to read.
    /// </remarks>
    public static IReadOnlyList<Rect> ComputePanes(Rect bounds, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        // The gutters are taken out of the total first, so the panes divide what is actually
        // left rather than overflowing by the number of gutters.
        int paneWidth = Math.Max((bounds.Width - count + 1) / count, 1);
        List<Rect> regions = new(count);
        int left = bounds.X;

        for (int i = 0; i < count; i++)
        {
            regions.Add(new Rect(left, bounds.Y, paneWidth, bounds.Height));
            left += paneWidth + 1;
        }

        return regions;
    }

    /// <summary>Reads the border setting into the three things it controls.</summary>
    private static (bool Outline, bool Separators, bool ActivePane) BorderTypes(string? setting) =>
        setting?.ToLowerInvariant() switch
        {
            // "true" is accepted for the sake of configurations written before the setting
            // grew its other values.
            "both" or "true" => (true, true, false),
            "outline" => (true, false, false),
            "separators" => (false, true, false),
            "active-pane" => (false, false, true),
            _ => (false, false, false),
        };

    /// <summary>Draws the frame and the rules between panes.</summary>
    private void DrawBorderLines(ScreenBuffer screen, Rect bounds, IReadOnlyList<Rect> regions,
                                 IReadOnlyList<int> numbers, int currentTabNumber,
                                 bool outline, bool separators, bool activePane)
    {
        CellStyle style = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InBrowser, ContextKey.Border));

        if (activePane)
        {
            // Only the focused pane is framed, which says where the keys will go without
            // dimming anything or drawing lines the eye has to ignore.
            int index = Math.Max(numbers.ToList().IndexOf(currentTabNumber), 0);

            if (index < regions.Count)
            {
                Rect pane = regions[index];
                Frame(screen, new Rect(pane.X, bounds.Y, pane.Width, bounds.Height), style);
            }

            return;
        }

        if (outline)
        {
            Frame(screen, bounds, style);
        }

        if (!separators)
        {
            return;
        }

        int right = bounds.Right - 1;

        // A rule goes in the gutter each pane leaves at its right; the last pane has the frame
        // or the screen edge there instead.
        for (int i = 0; i < regions.Count - 1; i++)
        {
            int x = regions[i].Right;

            if (x <= bounds.X || x > right)
            {
                continue;
            }

            screen.VerticalLine(x, bounds.Y, bounds.Height, style);

            // Where a rule meets the frame it becomes a tee rather than a crossing.
            if (outline)
            {
                screen.Write(x, bounds.Y, BoxDrawing.TopTee, style);
                screen.Write(x, bounds.Bottom - 1, BoxDrawing.BottomTee, style);
            }
        }
    }

    /// <summary>Draws a rectangle around a region.</summary>
    private static void Frame(ScreenBuffer screen, Rect region, CellStyle style)
    {
        int right = region.Right - 1;
        int bottom = region.Bottom - 1;

        screen.HorizontalLine(region.X, region.Y, region.Width, style);
        screen.HorizontalLine(region.X, bottom, region.Width, style);
        screen.VerticalLine(region.X, region.Y, region.Height, style);
        screen.VerticalLine(right, region.Y, region.Height, style);

        screen.Write(region.X, region.Y, BoxDrawing.TopLeft, style);
        screen.Write(right, region.Y, BoxDrawing.TopRight, style);
        screen.Write(region.X, bottom, BoxDrawing.BottomLeft, style);
        screen.Write(right, bottom, BoxDrawing.BottomRight, style);
    }

    /// <summary>Creates or discards panes so there is exactly one per tab.</summary>
    private void EnsurePaneCount(int count)
    {
        while (_panes.Count < count)
        {
            _panes.Add(new BrowserColumn(colorScheme));
        }

        if (_panes.Count > count)
        {
            _panes.RemoveRange(count, _panes.Count - count);
        }
    }
}
