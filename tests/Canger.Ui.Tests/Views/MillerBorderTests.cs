// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Views;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Views;

/// <summary>
/// The frame and the rules between columns.
/// </summary>
public class MillerBorderTests
{
    private const int Width = 60;
    private const int Height = 10;

    /// <summary>Renders a tab into a screen with the given border setting.</summary>
    private static ScreenBuffer Render(string drawBorders)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/user/a.txt")
            .AddFile("/home/user/b.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/user", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        MillerView view = new(new DefaultColorScheme()) { DrawBorders = drawBorders };
        ScreenBuffer screen = new(Width, Height);
        view.Render(screen, new Rect(0, 0, Width, Height), tab);

        return screen;
    }

    /// <summary>The whole screen as one string, for asking whether a character appears at all.</summary>
    private static string AllText(ScreenBuffer screen) =>
        string.Concat(Enumerable.Range(0, Height).Select(screen.TextAt));

    [Fact]
    public void Render_DrawsNothingByDefault()
    {
        string text = AllText(Render("none"));

        Assert.DoesNotContain(BoxDrawing.Horizontal, text, StringComparison.Ordinal);
        Assert.DoesNotContain(BoxDrawing.Vertical, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_FramesTheViewOnOutline()
    {
        ScreenBuffer screen = Render("outline");

        Assert.StartsWith(BoxDrawing.TopLeft, screen.TextAt(0), StringComparison.Ordinal);
        Assert.EndsWith(BoxDrawing.TopRight, screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith(BoxDrawing.BottomLeft, screen.TextAt(Height - 1), StringComparison.Ordinal);
        Assert.EndsWith(BoxDrawing.BottomRight, screen.TextAt(Height - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DrawsNoFrameForSeparatorsAlone()
    {
        string text = AllText(Render("separators"));

        Assert.Contains(BoxDrawing.Vertical, text, StringComparison.Ordinal);
        Assert.DoesNotContain(BoxDrawing.TopLeft, text, StringComparison.Ordinal);
        Assert.DoesNotContain(BoxDrawing.Horizontal, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_JoinsTheRulesToTheFrameWithTeesOnBoth()
    {
        // A crossing drawn as two overlapping lines leaves a visible break; a tee does not.
        ScreenBuffer screen = Render("both");

        Assert.Contains(BoxDrawing.TopTee, screen.TextAt(0), StringComparison.Ordinal);
        Assert.Contains(BoxDrawing.BottomTee, screen.TextAt(Height - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_LeavesRoomForTheFrameRatherThanDrawingOverTheColumns()
    {
        // The frame occupies real cells, so the listing has to start inside it.
        ScreenBuffer framed = Render("outline");

        Assert.StartsWith(BoxDrawing.Vertical, framed.TextAt(1), StringComparison.Ordinal);
        Assert.EndsWith(BoxDrawing.Vertical, framed.TextAt(1), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("BOTH")]
    public void Render_AcceptsTheOlderSpellingsOfBoth(string setting)
    {
        // "true" predates the setting growing its other values, and the value is matched
        // case-insensitively as ranger does.
        string text = AllText(Render(setting));

        Assert.Contains(BoxDrawing.TopLeft, text, StringComparison.Ordinal);
        Assert.Contains(BoxDrawing.TopTee, text, StringComparison.Ordinal);
    }
}
