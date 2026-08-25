// SPDX-License-Identifier: GPL-3.0-or-later
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
}
