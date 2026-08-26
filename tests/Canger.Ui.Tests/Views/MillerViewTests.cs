// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Previews;
using Canger.Ui.Views;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Views;

/// <summary>
/// The column width arithmetic. Getting this wrong is immediately visible as columns that
/// overlap, leave a ragged right edge, or drift as the terminal is resized.
/// </summary>
public class MillerViewTests
{
    private static IReadOnlyList<Rect> Columns(int width, params int[] ratios) =>
        MillerView.ComputeColumns(new Rect(0, 0, width, 20), ratios);

    [Fact]
    public void Columns_DivideTheWidthByTheRatios()
    {
        // 1,3,4 over 80 cells: shares of 10, 30 and 40.
        IReadOnlyList<Rect> columns = Columns(80, 1, 3, 4);

        Assert.Equal(3, columns.Count);
        Assert.Equal(0, columns[0].X);
        Assert.Equal(10, columns[1].X);
        Assert.Equal(40, columns[2].X);
    }

    [Fact]
    public void Columns_LeaveAGutterBetweenThem()
    {
        // Each column is one cell narrower than its share, and the next starts a full share
        // along, which is what puts a blank column between them.
        IReadOnlyList<Rect> columns = Columns(80, 1, 3, 4);

        Assert.Equal(9, columns[0].Width);
        Assert.Equal(29, columns[1].Width);
        Assert.True(columns[0].Right < columns[1].X);
        Assert.True(columns[1].Right < columns[2].X);
    }

    [Fact]
    public void Columns_LetTheLastOneAbsorbTheRemainder()
    {
        // 100 divided by three ratios does not come out even; the last column takes what is
        // left so the right edge stays put rather than shifting with the terminal width.
        IReadOnlyList<Rect> columns = MillerView.ComputeColumns(
            new Rect(0, 0, 100, 20), [1, 3, 4], paddingRight: false);

        Assert.Equal(100, columns[^1].Right);
    }

    [Fact]
    public void Columns_ReserveAPaddingColumnWhenAsked()
    {
        IReadOnlyList<Rect> withPadding = MillerView.ComputeColumns(
            new Rect(0, 0, 100, 20), [1, 3, 4], paddingRight: true);
        IReadOnlyList<Rect> without = MillerView.ComputeColumns(
            new Rect(0, 0, 100, 20), [1, 3, 4], paddingRight: false);

        Assert.Equal(without[^1].Width - 1, withPadding[^1].Width);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(120)]
    [InlineData(200)]
    public void Columns_NeverOverlapAtAnyWidth(int width)
    {
        IReadOnlyList<Rect> columns = Columns(width, 1, 3, 4);

        for (int i = 0; i < columns.Count - 1; i++)
        {
            Assert.True(columns[i].Right <= columns[i + 1].X,
                        $"column {i} overruns column {i + 1} at width {width}");
        }
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(200)]
    public void Columns_StayWithinTheAvailableWidth(int width)
    {
        IReadOnlyList<Rect> columns = Columns(width, 1, 3, 4);

        Assert.All(columns, column => Assert.True(column.Right <= width));
    }

    [Fact]
    public void Columns_HandleATerminalTooNarrowToDivide()
    {
        // Nothing should throw or produce a negative width, however cramped the terminal.
        IReadOnlyList<Rect> columns = Columns(4, 1, 3, 4);

        Assert.All(columns, column => Assert.True(column.Width >= 0));
    }

    [Fact]
    public void Columns_SupportAnyNumberOfRatios()
    {
        Assert.Equal(2, Columns(80, 1, 1).Count);
        Assert.Equal(4, Columns(80, 1, 1, 2, 3).Count);
    }

    [Fact]
    public void Columns_AreEmptyWhenThereIsNoRoomOrNoRatios()
    {
        Assert.Empty(MillerView.ComputeColumns(new Rect(0, 0, 0, 0), [1, 2]));
        Assert.Empty(MillerView.ComputeColumns(new Rect(0, 0, 80, 20), []));
    }

    [Fact]
    public void Columns_KeepTheHeightAndVerticalOffsetTheyWereGiven()
    {
        IReadOnlyList<Rect> columns = MillerView.ComputeColumns(
            new Rect(0, 1, 80, 18), [1, 3, 4]);

        Assert.All(columns, column =>
        {
            Assert.Equal(1, column.Y);
            Assert.Equal(18, column.Height);
        });
    }

    [Fact]
    public void ComputeColumns_CollapsedGivesThePreviewsWidthToTheMainColumn()
    {
        // `collapse_preview`, on by default and doing nothing: the listing stayed squeezed into
        // its share while the other half of the window showed nothing at all. Ranger's
        // `stretch_ratios` hand the width to the column *before* the preview rather than
        // spreading it around (`gui/widgets/view_miller.py:60-65`).
        IReadOnlyList<Rect> normal =
            MillerView.ComputeColumns(new Rect(0, 0, 100, 20), [1, 3, 4]);
        IReadOnlyList<Rect> collapsed =
            MillerView.ComputeColumns(new Rect(0, 0, 100, 20), [1, 3, 4], collapse: true);

        Assert.Equal(normal[0].Width, collapsed[0].Width);
        Assert.True(collapsed[1].Width > normal[1].Width,
                    $"main column should widen: {normal[1].Width} -> {collapsed[1].Width}");
        Assert.True(collapsed[2].Width < normal[2].Width,
                    $"preview should shrink: {normal[2].Width} -> {collapsed[2].Width}");
    }

    [Fact]
    public void ComputeColumns_CollapsedKeepsATenthOfThePreviewAsPadding()
    {
        // A sliver rather than nothing, so the listing does not run into the window's edge.
        IReadOnlyList<Rect> collapsed =
            MillerView.ComputeColumns(new Rect(0, 0, 100, 20), [1, 3, 4], collapse: true);

        Assert.Equal(5, collapsed[2].Width);
    }

    [Fact]
    public void ComputeColumns_CollapsedKeepsNothingWithoutPaddingRight()
    {
        IReadOnlyList<Rect> collapsed = MillerView.ComputeColumns(
            new Rect(0, 0, 100, 20), [1, 3, 4], paddingRight: false, collapse: true);

        Assert.Equal(0, collapsed[2].Width);
    }

    [Fact]
    public void ComputeColumns_CollapsedStillFillsTheWidth()
    {
        IReadOnlyList<Rect> collapsed =
            MillerView.ComputeColumns(new Rect(0, 0, 100, 20), [1, 3, 4], collapse: true);

        Assert.True(collapsed[^1].Right <= 100,
                    $"columns must not overrun the window: {collapsed[^1].Right}");
    }

    [Fact]
    public void Collapse_KeepsTheColumnOpenWhileAPreviewIsBeingGenerated()
    {
        // The flicker: a preview that takes a fifth of a second to generate answered `None` for
        // the frames in between, the column collapsed on that answer, and it came back the moment
        // the image arrived. Two frames out of forty, and visible as a twitch down the right.
        Assert.True(MillerView.CountsAsPreview(PreviewKind.Pending, collapsedLastFrame: false));
    }

    [Fact]
    public void Collapse_LeavesACollapsedColumnCollapsedWhilePending()
    {
        // The same rule the other way up: pending repeats the last decision rather than making
        // one, so a column that was closed does not open on a maybe.
        Assert.False(MillerView.CountsAsPreview(PreviewKind.Pending, collapsedLastFrame: true));
    }

    [Fact]
    public void Collapse_ClosesTheColumnWhenThereIsGenuinelyNothing()
    {
        // `None` is a real answer and must still collapse, whatever the previous frame decided.
        Assert.False(MillerView.CountsAsPreview(PreviewKind.None, collapsedLastFrame: false));
        Assert.False(MillerView.CountsAsPreview(PreviewKind.None, collapsedLastFrame: true));
    }

    [Theory]
    [InlineData(PreviewKind.Text)]
    [InlineData(PreviewKind.Image)]
    [InlineData(PreviewKind.DirectImage)]
    public void Collapse_OpensTheColumnForARealPreview(PreviewKind kind)
    {
        Assert.True(MillerView.CountsAsPreview(kind, collapsedLastFrame: true));
    }
}
