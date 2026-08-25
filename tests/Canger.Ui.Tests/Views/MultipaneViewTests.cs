// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Views;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Views;

/// <summary>
/// Every tab side by side, which is the view to use when moving files between two places.
/// </summary>
public class MultipaneViewTests
{
    private const int Width = 60;
    private const int Height = 10;

    /// <summary>Builds two tabs over two directories and renders them.</summary>
    private static (MultipaneView View, ScreenBuffer Screen) Render(
        string drawBorders = "none", int currentTab = 1, int tabCount = 2)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/left/alpha.txt")
            .AddFile("/right/beta.txt");

        DirectoryCache cache = new(fs);
        Dictionary<int, Tab> tabs = [];

        for (int i = 1; i <= tabCount; i++)
        {
            tabs[i] = new Tab(cache, i == 1 ? "/left" : "/right", 20);
            tabs[i].Current.Load(TestContext.Current.CancellationToken);
        }

        MultipaneView view = new(new DefaultColorScheme()) { DrawBorders = drawBorders };
        ScreenBuffer screen = new(Width, Height);
        view.Render(screen, new Rect(0, 0, Width, Height), tabs, currentTab);

        return (view, screen);
    }

    private static string AllText(ScreenBuffer screen) =>
        string.Concat(Enumerable.Range(0, Height).Select(screen.TextAt));

    [Fact]
    public void ComputePanes_DividesTheWidthEqually()
    {
        // No pane is more important than another, so none gets a bigger share — including the
        // last, which is why 61 cells across 3 panes leaves two spare rather than widening one.
        IReadOnlyList<Rect> panes = MultipaneView.ComputePanes(new Rect(0, 0, 61, 10), 3);

        Assert.Equal(3, panes.Count);
        Assert.All(panes, pane => Assert.Equal(19, pane.Width));
    }

    [Fact]
    public void ComputePanes_LeavesAGutterBetweenPanes()
    {
        IReadOnlyList<Rect> panes = MultipaneView.ComputePanes(new Rect(0, 0, 61, 10), 3);

        Assert.True(panes[0].Right < panes[1].X);
        Assert.True(panes[1].Right < panes[2].X);
    }

    [Fact]
    public void ComputePanes_StaysWithinTheWidthItWasGiven()
    {
        // The gutters come out of the total first, so the panes cannot overflow by their count.
        IReadOnlyList<Rect> panes = MultipaneView.ComputePanes(new Rect(0, 0, 61, 10), 3);

        Assert.True(panes[^1].Right <= 61);
    }

    [Fact]
    public void ComputePanes_GivesEveryPaneAtLeastOneCell()
    {
        // Nine tabs in a narrow terminal must still produce nine panes, not zero-width ones.
        IReadOnlyList<Rect> panes = MultipaneView.ComputePanes(new Rect(0, 0, 10, 10), 9);

        Assert.Equal(9, panes.Count);
        Assert.All(panes, pane => Assert.True(pane.Width >= 1));
    }

    [Fact]
    public void Render_ShowsEveryTabAtOnce()
    {
        (_, ScreenBuffer screen) = Render();
        string text = AllText(screen);

        Assert.Contains("alpha.txt", text, StringComparison.Ordinal);
        Assert.Contains("beta.txt", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MakesEveryPaneAMainColumn()
    {
        // Each pane shows its own cursor and its own sizes; that is the point of the view.
        (MultipaneView view, _) = Render();

        Assert.All(view.Panes, pane => Assert.True(pane.IsMainColumn));
    }

    [Fact]
    public void Render_MarksOnlyTheFocusedPaneAsActive()
    {
        (MultipaneView view, _) = Render(currentTab: 2);

        Assert.False(view.Panes[0].IsActivePane);
        Assert.True(view.Panes[1].IsActivePane);
    }

    [Fact]
    public void Render_DiscardsPanesWhenATabIsClosed()
    {
        MultipaneView view = new(new DefaultColorScheme());
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/left/alpha.txt");
        DirectoryCache cache = new(fs);
        ScreenBuffer screen = new(Width, Height);

        Dictionary<int, Tab> tabs = new()
        {
            [1] = new Tab(cache, "/left", 20),
            [2] = new Tab(cache, "/left", 20),
        };

        view.Render(screen, new Rect(0, 0, Width, Height), tabs, 1);
        Assert.Equal(2, view.Panes.Count);

        tabs.Remove(2);
        view.Render(screen, new Rect(0, 0, Width, Height), tabs, 1);
        Assert.Single(view.Panes);
    }

    [Fact]
    public void Render_DrawsNoBordersByDefault()
    {
        string text = AllText(Render().Screen);

        Assert.DoesNotContain(BoxDrawing.Vertical, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RulesBetweenPanesOnSeparators()
    {
        string text = AllText(Render("separators").Screen);

        Assert.Contains(BoxDrawing.Vertical, text, StringComparison.Ordinal);
        Assert.DoesNotContain(BoxDrawing.TopLeft, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_FramesOnlyTheFocusedPaneOnActivePane()
    {
        // One frame, not two: it says where the keys will go without dimming anything.
        ScreenBuffer screen = Render("active-pane", currentTab: 2).Screen;
        string top = screen.TextAt(0);

        Assert.Equal(1, top.Count(c => c == BoxDrawing.TopLeft[0]));

        // And it is the second pane that is framed, not the first.
        Assert.True(top.IndexOf(BoxDrawing.TopLeft[0], StringComparison.Ordinal) > Width / 3);
    }

    [Fact]
    public void Render_FramesAndRulesOnBoth()
    {
        ScreenBuffer screen = Render("both").Screen;

        Assert.Contains(BoxDrawing.TopLeft, screen.TextAt(0), StringComparison.Ordinal);
        Assert.Contains(BoxDrawing.TopTee, screen.TextAt(0), StringComparison.Ordinal);
        Assert.Contains(BoxDrawing.BottomTee, screen.TextAt(Height - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DoesNothingWithNoTabs()
    {
        MultipaneView view = new(new DefaultColorScheme());
        ScreenBuffer screen = new(Width, Height);

        view.Render(screen, new Rect(0, 0, Width, Height), new Dictionary<int, Tab>(), 1);

        Assert.Empty(view.Panes);
    }
}
