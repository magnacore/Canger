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
    public void Wrap_BreaksBetweenWordsRatherThanAtTheColumn()
    {
        // Ranger cuts the line at whatever character the width lands on, which reads as text that
        // was not wrapped at all. A deliberate divergence, recorded on Pager.Wrap.
        // Twelve, so the edge of the pane falls inside "brown" rather than on a space, where
        // breaking by word and breaking by column cannot be told apart.
        Pager pager = new(new DefaultColorScheme()) { WrapLines = true };
        pager.Layout(new Rect(0, 0, 12, 4));
        pager.SetText("the quick brown fox");
        ScreenBuffer screen = new(12, 4);

        pager.Render(screen);

        Assert.Equal("the quick   ", screen.TextAt(0));
        Assert.Equal("brown fox   ", screen.TextAt(1));
    }

    [Fact]
    public void Wrap_KeepsTheColoursAcrossABreak()
    {
        // The break is worked out on the visible text but drawn from the original, so a run of
        // colour that spans it stays coloured on the row after.
        Pager pager = new(new DefaultColorScheme()) { WrapLines = true };
        pager.Layout(new Rect(0, 0, 6, 4));
        pager.SetText("\e[31malpha beta\e[0m");
        ScreenBuffer screen = new(6, 4);

        pager.Render(screen);

        Assert.Equal("beta  ", screen.TextAt(1));
        Assert.Equal(Color.Red, screen[0, 1].Style.Foreground);
    }

    [Fact]
    public void Wrap_BreaksAWordThatIsWiderThanThePane()
    {
        // Not breaking it would mean not showing it.
        Pager pager = new(new DefaultColorScheme()) { WrapLines = true };
        pager.Layout(new Rect(0, 0, 6, 4));
        pager.SetText("hi abcdefghijk");
        ScreenBuffer screen = new(6, 4);

        pager.Render(screen);

        Assert.Equal("hi    ", screen.TextAt(0));
        Assert.Equal("abcdef", screen.TextAt(1));
        Assert.Equal("ghijk ", screen.TextAt(2));
    }

    [Fact]
    public void Wrap_ReportsOneSegmentForALineThatFits()
    {
        Assert.Equal([(0, 9)], Pager.Wrap("the quick", 10));
    }

    [Fact]
    public void Wrap_MeasuresInCellsSoWideCharactersBreakWhereTheyLook()
    {
        // Six characters, twelve cells. Counting characters would fit five of them on a row this
        // wide and draw over the pane's edge.
        Assert.Equal([(0, 4), (4, 4), (8, 4)], Pager.Wrap("\u4e00\u4e8c\u4e09\u56db\u4e94\u516d", 5));
    }

    [Fact]
    public void Wrap_KeepsTheIndentationOfTheFirstRow()
    {
        // Indentation is what makes a nested list or a block of code readable, so the line's own
        // is kept — and never broken on, which would put out a row holding nothing but it.
        Assert.Equal([(0, 7), (7, 8)], Pager.Wrap("    ab cd  efgh", 8));
        Assert.Equal([(0, 10), (10, 5)], Pager.Wrap("    unbreakable", 10));
    }

    [Fact]
    public void Wrap_DropsTheSpacesABreakLandsOn()
    {
        // Carrying them over would start the continuation adrift of the rows around it.
        Assert.Equal([(0, 6), (9, 2)], Pager.Wrap("abcdef   gh", 6));
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
