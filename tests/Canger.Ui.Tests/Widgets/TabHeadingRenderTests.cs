// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The tab list at the right of the title bar, and the <c>dirname_in_tabs</c> label.
/// </summary>
/// <remarks>
/// The setting was defined and read but nothing ever consumed it, so tabs showed only their
/// number no matter what the configuration said — the same shape of defect as the missing tag
/// marker. Ranger's rule is <c>gui/widgets/titlebar.py:151-161</c>.
/// </remarks>
public class TabHeadingRenderTests
{
    private const int Width = 60;

    private static string Render(TitleBar bar)
    {
        ScreenBuffer screen = new(Width, 1);
        bar.Layout(new Rect(0, 0, Width, 1));
        bar.Render(screen);
        return screen.TextAt(0).TrimEnd();
    }

    private static TitleBar Build(bool dirnames, params string[] paths)
    {
        return new TitleBar(new DefaultColorScheme())
        {
            Tabs = [.. paths.Select((p, i) => new TabHeading(i + 1, p))],
            ActiveTabNumber = 1,
            DirnameInTabs = dirnames,
            ShowSelection = false,
        };
    }

    [Fact]
    public void Draw_NamesEachTabAfterItsDirectory()
    {
        // What the user sees in ranger: `2:Books`.
        string line = Render(Build(dirnames: true, "/home/manuj", "/home/manuj/Books"));

        Assert.EndsWith(" 1:manuj 2:Books", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsOnlyNumbersWhenTheSettingIsOff()
    {
        string line = Render(Build(dirnames: false, "/home/manuj", "/home/manuj/Books"));

        Assert.EndsWith(" 1 2", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_HidesTheListEntirelyForASingleTab()
    {
        // One tab needs no list: the path on the left already says where you are.
        string line = Render(Build(dirnames: true, "/home/manuj/Books"));

        Assert.DoesNotContain("1:Books", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_WritesTheRootDirectoryAsASlash()
    {
        // The root has no name of its own, so `basename` is empty and ranger substitutes `/`.
        string line = Render(Build(dirnames: true, "/", "/home"));

        Assert.EndsWith(" 1:/ 2:home", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_CutsALongNameShortWithAnEllipsis()
    {
        // Sixteen characters: one too many, so fourteen survive plus the marker.
        string line = Render(Build(dirnames: true, "/a/AbcdefghijklmnoP", "/b"));

        Assert.EndsWith(" 1:Abcdefghijklmn~ 2:b", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_KeepsANameOfExactlyFifteenIntact()
    {
        string line = Render(Build(dirnames: true, "/a/Abcdefghijklmno", "/b"));

        Assert.EndsWith(" 1:Abcdefghijklmno 2:b", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_UsesTheUnicodeEllipsisWhenAsked()
    {
        TitleBar bar = Build(dirnames: true, "/a/AbcdefghijklmnoP", "/b");
        bar.Ellipsis = "…";

        Assert.EndsWith(" 1:Abcdefghijklmn… 2:b", Render(bar), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_CutsWideNamesByCellsRatherThanByCharacters()
    {
        // Nine CJK characters are eighteen cells wide, which is what runs out on a terminal
        // even though Python's `len` would call the name short enough to keep whole. Seven fit.
        string line = Render(Build(dirnames: true, "/a/日本語日本語日本語", "/b"));

        Assert.EndsWith(" 1:日本語日本語日~ 2:b", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_KeepsTheNumberInFrontOfALabel()
    {
        // `map bk eval fm.tab_new(narg='Books', path="~/Books")`. Ranger shows ` Books` because
        // the string *is* its tab key; Canger keeps the number, which is what `2gt` acts on.
        TitleBar bar = new(new DefaultColorScheme())
        {
            Tabs =
            [
                new TabHeading(1, "/home/manuj"),
                new TabHeading(2, "/home/manuj/Books", Label: "Books"),
            ],
            DirnameInTabs = true,
            ShowSelection = false,
        };

        Assert.EndsWith(" 1:manuj 2:Books", Render(bar), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsALabelEvenWithoutDirnames()
    {
        TitleBar bar = new(new DefaultColorScheme())
        {
            Tabs =
            [
                new TabHeading(1, "/home/manuj"),
                new TabHeading(2, "/home/manuj/Books", Label: "Books"),
            ],
            DirnameInTabs = false,
            ShowSelection = false,
        };

        // A label is a name someone chose, not a directory name, so `dirname_in_tabs` has
        // no say over whether it is drawn.
        Assert.EndsWith(" 1 2:Books", Render(bar), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_LeavesALongLabelWhole()
    {
        // The fifteen-cell limit is ranger's rule for the *directory* name, not for the tab's
        // own name: `map bz eval fm.tab_new(narg='Task Capture Bin', ...)` is shown in full.
        TitleBar bar = new(new DefaultColorScheme())
        {
            Tabs =
            [
                new TabHeading(1, "/a", Label: "Task Capture Bin"),
                new TabHeading(2, "/b"),
            ],
            DirnameInTabs = false,
            ShowSelection = false,
        };

        Assert.EndsWith(" 1:Task Capture Bin 2", Render(bar), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_KeepsTheActiveTabWhenTheLineIsTooNarrowForAll()
    {
        // The whole right-hand side used to be dropped when it did not fit, so a narrow terminal
        // showed no tabs at all and no hint that any were open.
        TitleBar bar = new(new DefaultColorScheme())
        {
            Tabs = [.. Enumerable.Range(1, 8).Select(n => new TabHeading(n, $"/d{n}", $"label{n}"))],
            ActiveTabNumber = 7,
            ShowSelection = false,
        };

        ScreenBuffer screen = new(30, 1);
        bar.Layout(new Rect(0, 0, 30, 1));
        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.Contains("7:label7", line, StringComparison.Ordinal);
        Assert.DoesNotContain("1:label1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_GrowsTheListOutwardsFromTheActiveTab()
    {
        TitleBar bar = new(new DefaultColorScheme())
        {
            Tabs = [.. Enumerable.Range(1, 8).Select(n => new TabHeading(n, $"/d{n}", $"label{n}"))],
            ActiveTabNumber = 4,
            ShowSelection = false,
        };

        ScreenBuffer screen = new(30, 1);
        bar.Layout(new Rect(0, 0, 30, 1));
        bar.Render(screen);
        string line = screen.TextAt(0);

        // Neighbours on both sides, and nothing from either far end.
        Assert.Contains("3:label3 4:label4 5:label5", line, StringComparison.Ordinal);
        Assert.DoesNotContain("1:label1", line, StringComparison.Ordinal);
        Assert.DoesNotContain("8:label8", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_DropsTheBranchBeforeAnyTab()
    {
        // Order of least value: the branch first, then the key buffer, then tabs.
        TitleBar bar = new(new DefaultColorScheme())
        {
            Tabs = [new TabHeading(1, "/a", "one"), new TabHeading(2, "/b", "two")],
            ActiveTabNumber = 2,
            Branch = "some-long-branch-name",
            ShowSelection = false,
        };

        ScreenBuffer screen = new(24, 1);
        bar.Layout(new Rect(0, 0, 24, 1));
        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.Contains("1:one 2:two", line, StringComparison.Ordinal);
        Assert.DoesNotContain("some-long-branch-name", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_PutsTheThrobberInTheLeadingSpaceOfTheTabList()
    {
        // Ranger writes it at `wid - right_sumsize` (`titlebar.py:44-46`) — the first cell of the
        // right-hand group, which is always a leading space — so it costs no width at all.
        TitleBar bar = Build(dirnames: false, "/a", "/b");
        bar.Throbber = "/";

        string line = Render(bar);

        Assert.EndsWith("/1 2", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheThrobberWithNoTabsToSitBeside()
    {
        // One tab means no list, so there is no leading space to borrow and it takes the last
        // column instead. Ranger never reaches this: its bar always carries a key buffer slot.
        TitleBar bar = Build(dirnames: false, "/a");
        bar.Throbber = "\\";

        Assert.EndsWith("\\", Render(bar), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsNoThrobberWhileNothingIsRunning()
    {
        TitleBar bar = Build(dirnames: false, "/a", "/b");

        Assert.EndsWith(" 1 2", Render(bar), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_HighlightsTheActiveTabOnly()
    {
        TitleBar bar = Build(dirnames: true, "/a/one", "/b/two");
        bar.ActiveTabNumber = 2;

        ScreenBuffer screen = new(Width, 1);
        bar.Layout(new Rect(0, 0, Width, 1));
        bar.Render(screen);

        string line = screen.TextAt(0);
        int second = line.IndexOf("2:two", StringComparison.Ordinal);
        int first = line.IndexOf("1:one", StringComparison.Ordinal);

        Assert.Equal(Color.Green, screen[second, 0].Style.Background);
        Assert.NotEqual(Color.Green, screen[first, 0].Style.Background);
    }
}
