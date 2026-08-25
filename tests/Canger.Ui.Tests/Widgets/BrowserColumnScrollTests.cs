// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The scroll algorithm from <c>gui/widgets/browsercolumn.py:553-588</c>. The behaviour it
/// produces — the view holding still until the cursor nears an edge, then following one line at a
/// time — is what makes moving through a long listing feel steady rather than jumpy.
/// </summary>
public class BrowserColumnScrollTests
{
    private const int Height = 20;
    private const int Margin = 8;

    private static int Scroll(int cursor, int count, int current = 0) =>
        BrowserColumn.ComputeScroll(cursor, count, Height, current, Margin);

    [Fact]
    public void DoesNotScrollWhenEverythingFits() =>
        Assert.Equal(0, Scroll(cursor: 5, count: 10));

    [Fact]
    public void StaysAtTheTopWhileTheCursorIsWithinTheMargin()
    {
        // Moving down through the first rows must not scroll, or the listing would slide away
        // from under the cursor the moment it moved.
        Assert.Equal(0, Scroll(cursor: 0, count: 100));
        Assert.Equal(0, Scroll(cursor: 5, count: 100));
        Assert.Equal(0, Scroll(cursor: 11, count: 100));
    }

    [Fact]
    public void FollowsTheCursorOnceItReachesTheLowerMargin()
    {
        // With a height of 20 and a margin of 8, the cursor may reach row 11 before the view
        // has to move; beyond that it scrolls by exactly enough to restore the margin.
        Assert.Equal(1, Scroll(cursor: 12, count: 100));
        Assert.Equal(2, Scroll(cursor: 13, count: 100, current: 1));
    }

    [Fact]
    public void FollowsTheCursorUpwardTheSameWay()
    {
        Assert.Equal(20, Scroll(cursor: 28, count: 100, current: 20));
        Assert.Equal(19, Scroll(cursor: 27, count: 100, current: 20));
    }

    [Fact]
    public void StopsAtTheEndOfTheListing()
    {
        // The last screenful should show the end of the list, not scroll past into blank space.
        Assert.Equal(80, Scroll(cursor: 99, count: 100, current: 80));
        Assert.Equal(80, Scroll(cursor: 99, count: 100, current: 95));
    }

    [Fact]
    public void KeepsTheCursorVisibleWhereverItJumps()
    {
        // Jumping with gg or G, or moving by a page, must always leave the cursor on screen.
        foreach (int cursor in (int[])[0, 1, 12, 50, 87, 98, 99])
        {
            int start = Scroll(cursor, count: 100, current: 40);
            Assert.InRange(cursor, start, start + Height - 1);
        }
    }

    [Fact]
    public void CentresTheCursorWhenTheColumnIsTooShortForTheMargin()
    {
        // A margin of 8 cannot be honoured on both sides of a five-row column, so the cursor is
        // centred instead of the rule producing nonsense.
        int start = BrowserColumn.ComputeScroll(cursor: 50, count: 100, height: 5, current: 0,
                                                margin: 8);

        Assert.InRange(50, start, start + 4);
        Assert.Equal(48, start);
    }

    [Fact]
    public void HandlesEmptyAndDegenerateListings()
    {
        Assert.Equal(0, BrowserColumn.ComputeScroll(0, 0, Height, 0, Margin));
        Assert.Equal(0, BrowserColumn.ComputeScroll(0, 10, 0, 0, Margin));
    }

    [Fact]
    public void RecoversFromAScrollPositionPastTheEnd()
    {
        // The listing can shrink under the view, for instance when a filter is applied.
        Assert.Equal(80, Scroll(cursor: 90, count: 100, current: 500));
    }
}
