// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Ui.Widgets;
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Where the status bar starts, so it lines up with the listing above it.
/// </summary>
/// <remarks>
/// Reported as the bar not lining up with the browser's vertical rule. With <c>draw_borders</c>
/// drawing an outline the listing starts one column in, while the status bar began at the very
/// edge — so the permissions on the left and the free space on the right both overhung the frame
/// by a column. Ranger has the same offset; this is a deliberate divergence.
/// </remarks>
public class StatusBarInsetTests
{
    [Theory]
    [InlineData("both")]
    [InlineData("outline")]
    [InlineData("true")]      // accepted for configurations older than the setting's other values
    [InlineData("BOTH")]      // the views read it case-insensitively, and so does this
    public void AFramedViewMovesTheBarInByTheFrame(string borders) =>
        Assert.Equal(1, Browser.StatusBarInset("miller", borders, null));

    [Theory]
    [InlineData("none")]
    [InlineData("separators")]   // rules between columns, but no outline to line up with
    [InlineData("")]
    [InlineData(null)]
    public void AnUnframedViewLeavesItAtTheEdge(string? borders) =>
        Assert.Equal(0, Browser.StatusBarInset("miller", borders, null));

    [Fact]
    public void MultipaneObeysItsOwnSetting()
    {
        Assert.Equal(1, Browser.StatusBarInset("multipane", "none", "both"));
        Assert.Equal(0, Browser.StatusBarInset("multipane", "both", "none"));
    }

    [Fact]
    public void MultipaneUnsetMeansWhateverDrawBordersSays()
    {
        // Null is not "no borders": it is "the other setting decides", which is what lets one
        // setting govern both views.
        Assert.Equal(1, Browser.StatusBarInset("multipane", "both", null));
        Assert.Equal(0, Browser.StatusBarInset("multipane", "none", null));
    }

    [Fact]
    public void TheBarIsLaidOutWhereTheRuleSays()
    {
        // The rule being right is not the same as the layout using it: taking the inset out of the
        // layout while leaving the rule in place broke no test at all.
        Rect bottom = new(0, 23, 100, 1);

        Rect framed = Browser.StatusBarBounds(bottom, false, 100, "miller", "both", null);
        Rect plain = Browser.StatusBarBounds(bottom, false, 100, "miller", "none", null);

        Assert.Equal(1, framed.X);
        Assert.Equal(98, framed.Width);
        Assert.Equal(0, plain.X);
        Assert.Equal(100, plain.Width);
    }

    [Fact]
    public void TheBarOnTopIsInsetToo()
    {
        Rect bottom = new(0, 23, 100, 1);

        Assert.Equal(1, Browser.StatusBarBounds(bottom, true, 100, "miller", "both", null).X);
        Assert.Equal(0, Browser.StatusBarBounds(bottom, true, 100, "miller", "none", null).X);
    }

    [Fact]
    public void ANarrowScreenIsNotGivenANegativeWidth()
    {
        Rect bottom = new(0, 0, 1, 1);

        Assert.Equal(0, Browser.StatusBarBounds(bottom, false, 1, "miller", "both", null).Width);
    }

    [Fact]
    public void TheMillerViewIgnoresTheMultipaneSetting()
    {
        Assert.Equal(0, Browser.StatusBarInset("miller", "none", "both"));
    }
}
