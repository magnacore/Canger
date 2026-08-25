// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

public class PagerTests
{
    private static (Pager Pager, ScreenBuffer Screen) Build(string text, int width = 20,
                                                            int height = 5)
    {
        Pager pager = new(new DefaultColorScheme());
        pager.Layout(new Rect(0, 0, width, height));
        pager.SetText(text);
        return (pager, new ScreenBuffer(width, height));
    }

    [Fact]
    public void SetText_SplitsIntoLines()
    {
        (Pager pager, _) = Build("one\ntwo\nthree");

        Assert.Equal(3, pager.LineCount);
    }

    [Fact]
    public void SetText_HandlesWindowsLineEndings()
    {
        (Pager pager, _) = Build("one\r\ntwo");

        Assert.Equal(2, pager.LineCount);
    }

    [Fact]
    public void Draw_ShowsTheText()
    {
        (Pager pager, ScreenBuffer screen) = Build("first\nsecond");

        pager.Render(screen);

        Assert.StartsWith("first", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith("second", screen.TextAt(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_KeepsTheColoursTheTextCarries()
    {
        // A syntax-highlighted preview with the highlighting stripped is far less useful than
        // the tool it came from.
        (Pager pager, ScreenBuffer screen) = Build("\e[31mred\e[0m plain");

        pager.Render(screen);

        Assert.Equal(Color.Red, screen[0, 0].Style.Foreground);
        Assert.Equal(Color.Default, screen[4, 0].Style.Foreground);
    }

    [Fact]
    public void Draw_ExpandsTabsSoColumnsLineUp()
    {
        // Passing a tab through would let the terminal apply its own tab stops, and the columns
        // would no longer agree with the widget.
        (Pager pager, ScreenBuffer screen) = Build("a\tb", width: 20);

        pager.Render(screen);

        Assert.Equal("a   b", screen.TextAt(0).TrimEnd());
    }

    [Fact]
    public void Draw_TruncatesLinesTooLongForTheWidth()
    {
        (Pager pager, ScreenBuffer screen) = Build("this line is far too long", width: 10);

        pager.Render(screen);

        Assert.Equal(10, screen.TextAt(0).Length);
    }

    [Fact]
    public void ScrollVertically_MovesThroughTheText()
    {
        (Pager pager, ScreenBuffer screen) = Build("one\ntwo\nthree\nfour\nfive\nsix", height: 2);

        pager.ScrollVertically(2);
        pager.Render(screen);

        Assert.StartsWith("three", screen.TextAt(0), StringComparison.Ordinal);
        Assert.Equal(2, pager.VerticalOffset);
    }

    [Fact]
    public void ScrollVertically_StopsAtEitherEnd()
    {
        (Pager pager, _) = Build("one\ntwo\nthree");

        pager.ScrollVertically(-10);
        Assert.Equal(0, pager.VerticalOffset);

        pager.ScrollVertically(100);
        Assert.Equal(2, pager.VerticalOffset);
    }

    [Fact]
    public void ScrollHorizontally_MovesSideways()
    {
        (Pager pager, ScreenBuffer screen) = Build("abcdefghij", width: 5);

        pager.ScrollHorizontally(3);
        pager.Render(screen);

        Assert.StartsWith("defgh", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollHorizontally_CountsCellsSoWideCharactersLineUp()
    {
        (Pager pager, ScreenBuffer screen) = Build("モヒカン", width: 6);

        pager.ScrollHorizontally(2);
        pager.Render(screen);

        Assert.StartsWith("ヒカン", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollToEdge_JumpsToTheTopOrBottom()
    {
        (Pager pager, _) = Build("one\ntwo\nthree\nfour");

        pager.ScrollToEdge(toEnd: true);
        Assert.Equal(3, pager.VerticalOffset);

        pager.ScrollToEdge(toEnd: false);
        Assert.Equal(0, pager.VerticalOffset);
    }

    [Fact]
    public void Wrap_ContinuesALongLineOnTheNextRow()
    {
        Pager pager = new(new DefaultColorScheme()) { WrapLines = true };
        pager.Layout(new Rect(0, 0, 5, 4));
        pager.SetText("abcdefghij");
        ScreenBuffer screen = new(5, 4);

        pager.Render(screen);

        Assert.Equal("abcde", screen.TextAt(0));
        Assert.Equal("fghij", screen.TextAt(1));
    }

    [Fact]
    public void Clear_ForgetsTheText()
    {
        (Pager pager, ScreenBuffer screen) = Build("something");

        pager.Clear();
        pager.Render(screen);

        Assert.Equal(0, pager.LineCount);
        Assert.Equal("                    ", screen.TextAt(0));
    }

    [Fact]
    public void Draw_HandlesTextWithNoContent()
    {
        (Pager pager, ScreenBuffer screen) = Build("");

        pager.Render(screen);

        Assert.Equal("                    ", screen.TextAt(0));
    }
}
