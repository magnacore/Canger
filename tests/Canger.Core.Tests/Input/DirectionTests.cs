// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;

namespace Canger.Core.Tests.Input;

/// <summary>
/// The movement kernel. Expected values were produced by running ranger's own
/// <c>ext/direction.py</c> over the same inputs, so this pins Canger to ranger's arithmetic
/// rather than to a re-derivation of it.
/// </summary>
public class DirectionTests
{
    [Fact]
    public void Move_StepsOneRowInEitherDirection()
    {
        Assert.Equal(6, new Direction(down: 1).Move(1, current: 5, maximum: 20));
        Assert.Equal(4, new Direction(up: 1).Move(-1, current: 5, maximum: 20));
    }

    [Fact]
    public void Move_ClampsAtBothEnds()
    {
        Assert.Equal(19, new Direction(down: 1).Move(1, current: 19, maximum: 20));
        Assert.Equal(0, new Direction(up: 1).Move(-1, current: 0, maximum: 20));
    }

    [Fact]
    public void Move_TreatsToAsAnAbsolutePosition()
    {
        Direction first = new(to: 0);
        Assert.Equal(0, first.Move(first.Down, current: 5, maximum: 20));

        // A negative absolute position counts back from the end.
        Direction last = new(to: -1);
        Assert.Equal(19, last.Move(last.Down, current: 5, maximum: 20));
    }

    [Fact]
    public void Move_ScalesByThePageSize()
    {
        Assert.Equal(25, new Direction(down: 1, pages: true)
            .Move(1, current: 0, maximum: 100, pageSize: 25));
        Assert.Equal(25, new Direction(up: 1, pages: true)
            .Move(-1, current: 50, maximum: 100, pageSize: 25));
    }

    [Fact]
    public void Move_HandlesHalfPages()
    {
        // J and K move half a page.
        Assert.Equal(12, new Direction(down: 0.5, pages: true)
            .Move(0.5, current: 0, maximum: 100, pageSize: 25));
        Assert.Equal(38, new Direction(up: 0.5, pages: true)
            .Move(-0.5, current: 50, maximum: 100, pageSize: 25));
    }

    [Fact]
    public void Move_RoundsTowardTheOriginOnFractionalPages()
    {
        // With an odd page size the two directions round opposite ways, so a half page down and
        // then a half page up returns to where it started rather than drifting.
        Assert.Equal(20, new Direction(down: 0.5, pages: true)
            .Move(0.5, current: 17, maximum: 100, pageSize: 7));
        Assert.Equal(14, new Direction(up: 0.5, pages: true)
            .Move(-0.5, current: 17, maximum: 100, pageSize: 7));
    }

    [Fact]
    public void Move_ScalesByAPercentageOfTheTotal() =>
        Assert.Equal(50, new Direction(down: 50, percentage: true)
            .Move(50, current: 0, maximum: 100));

    [Fact]
    public void Move_WrapsAndCountsCyclesWhenCyclingIsOn()
    {
        // wrap_scroll turns this on, so moving past the last entry returns to the first.
        Direction forward = new(down: 1, cycle: true);
        Assert.Equal(0, forward.Move(1, current: 19, maximum: 20));
        Assert.Equal(1, forward.CycleCount);

        Direction backward = new(up: 1, cycle: true);
        Assert.Equal(19, backward.Move(-1, current: 0, maximum: 20));
        Assert.Equal(-1, backward.CycleCount);
    }

    [Fact]
    public void Move_MultipliesARelativeAmountByTheQuantifier()
    {
        // 3j moves three rows.
        Assert.Equal(3, new Direction(down: 1).Move(1, over: 3, current: 0, maximum: 100));
    }

    [Fact]
    public void Move_ReplacesAnAbsolutePositionWithTheQuantifier()
    {
        // 5G goes to entry five, rather than five times somewhere.
        Direction zeroIndexed = new(to: 0);
        Assert.Equal(5, zeroIndexed.Move(zeroIndexed.Down, over: 5, current: 0, maximum: 100));

        Direction oneIndexed = new(to: 0, oneIndexed: true);
        Assert.Equal(4, oneIndexed.Move(oneIndexed.Down, over: 5, current: 0, maximum: 100));
    }

    [Fact]
    public void UpAndDownAreTheSameAxisWithOppositeSigns()
    {
        Assert.Equal(3, new Direction(down: 3).Down);
        Assert.Equal(-3, new Direction(up: 3).Down);
        Assert.Equal(3, new Direction(up: 3).Up);
    }

    [Fact]
    public void LeftAndRightAreTheSameAxisWithOppositeSigns()
    {
        Assert.Equal(4, new Direction(right: 4).Right);
        Assert.Equal(-4, new Direction(left: 4).Right);
        Assert.Equal(4, new Direction(left: 4).Left);
    }

    [Fact]
    public void AxisIsDecidedByWhetherAnAmountWasGivenNotByItsValue()
    {
        // move right=0 is horizontal movement of zero, not an absence of horizontal movement.
        Assert.True(new Direction(right: 0).IsHorizontal);
        Assert.False(new Direction(right: 0).IsVertical);
        Assert.True(new Direction(down: 0).IsVertical);
        Assert.False(new Direction(down: 0).IsHorizontal);
        Assert.False(new Direction().IsVertical);
    }

    [Fact]
    public void MovementIsRelativeUnlessSaidOtherwise()
    {
        Assert.True(new Direction(down: 1).IsRelative);
        Assert.True(new Direction(down: 1, absolute: true).IsAbsolute);
        Assert.True(new Direction(down: 1, relative: false).IsAbsolute);
        Assert.True(new Direction(to: 0).IsAbsolute);
    }

    [Fact]
    public void SignReportsTheDirectionOfTravel()
    {
        Assert.Equal(1, new Direction(down: 5).VerticalSign);
        Assert.Equal(-1, new Direction(up: 5).VerticalSign);
        Assert.Equal(0, new Direction(down: 0).VerticalSign);
        Assert.Equal(1, new Direction(right: 2).HorizontalSign);
        Assert.Equal(-1, new Direction(left: 2).HorizontalSign);
    }

    [Fact]
    public void Select_ReturnsTheItemsBetweenHereAndTheDestination()
    {
        // dj cuts the entry under the cursor and the one below it.
        int[] items = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        (int destination, IReadOnlyList<int> selection) =
            new Direction(down: 1).Select(items, current: 3, pageSize: 10);

        Assert.Equal([3, 4], selection);
        Assert.Equal(4, destination);
    }

    [Fact]
    public void Select_SpansUpwardsToo()
    {
        int[] items = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        (_, IReadOnlyList<int> selection) =
            new Direction(up: 2).Select(items, current: 5, pageSize: 10);

        Assert.Equal([3, 4, 5], selection);
    }

    [Fact]
    public void Select_HonoursTheQuantifier()
    {
        int[] items = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        (_, IReadOnlyList<int> selection) =
            new Direction(down: 1).Select(items, current: 0, pageSize: 10, over: 3);

        Assert.Equal([0, 1, 2, 3], selection);
    }

    [Fact]
    public void FromArguments_ReadsACommandsNamedArguments()
    {
        // This is what "map <PAGEDOWN> move down=1 pages=True" produces.
        Direction direction = Direction.FromArguments(new Dictionary<string, string>
        {
            ["down"] = "1",
            ["pages"] = "True",
        });

        Assert.Equal(1, direction.Down);
        Assert.True(direction.Pages);
        Assert.Equal(25, direction.Move(direction.Down, current: 0, maximum: 100, pageSize: 25));
    }

    [Fact]
    public void FromArguments_ReadsAnAbsoluteDestination()
    {
        Direction direction = Direction.FromArguments(new Dictionary<string, string>
        {
            ["to"] = "-1",
        });

        Assert.True(direction.IsAbsolute);
        Assert.Equal(19, direction.Move(direction.Down, current: 0, maximum: 20));
    }
}
