using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Which tab you land on after closing one.
/// </summary>
/// <remarks>
/// Ranger moves off the tab before deleting it: backwards when the tab is last in the list,
/// forwards otherwise (<c>core/actions.py:1283-1288</c>). Stated in terms of the tabs that
/// survive — which is the shape <see cref="Browser.NeighbourOf(IReadOnlyList{int}, int)"/>
/// works in — that is "the next one, or the previous one when there is no next".
///
/// Canger used to jump to the lowest-numbered tab, so closing tab 5 of five landed on tab 1.
/// </remarks>
public sealed class TabClosingTests
{
    [Fact]
    public void ClosingTheLastTabStepsBack()
    {
        Assert.Equal(4, Browser.NeighbourOf([1, 2, 3, 4], closed: 5));
    }

    [Fact]
    public void ClosingAMiddleTabStepsForward()
    {
        Assert.Equal(4, Browser.NeighbourOf([1, 2, 4, 5], closed: 3));
    }

    [Fact]
    public void ClosingTheFirstTabStepsForward()
    {
        Assert.Equal(2, Browser.NeighbourOf([2, 3, 4, 5], closed: 1));
    }

    [Fact]
    public void ClosingRepeatedlyWalksBackwardsFromTheEnd()
    {
        // Holding `q` down on tab 5 should count down 5, 4, 3, 2 rather than bouncing to 1.
        List<int> remaining = [1, 2, 3, 4, 5];
        List<int> landed = [];

        for (int closed = 5; remaining.Count > 1; )
        {
            remaining.Remove(closed);
            closed = Browser.NeighbourOf(remaining, closed);
            landed.Add(closed);
        }

        Assert.Equal([4, 3, 2, 1], landed);
    }

    [Fact]
    public void GapsInTheNumberingAreSkipped()
    {
        // Tab numbers need not be contiguous: `:tab_new 7` and `renumber_tabs_on_tab_close false`
        // both leave holes, and the neighbour is the nearest surviving number, not `closed + 1`.
        Assert.Equal(7, Browser.NeighbourOf([1, 7], closed: 4));
        Assert.Equal(4, Browser.NeighbourOf([1, 4], closed: 7));
    }

    [Fact]
    public void TheOnlyOtherTabIsAlwaysChosen()
    {
        Assert.Equal(2, Browser.NeighbourOf([2], closed: 1));
        Assert.Equal(1, Browser.NeighbourOf([1], closed: 2));
    }
}
