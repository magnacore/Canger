// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

public class BarRenderTests
{
    private static (Tab Tab, InMemoryFileSystem Fs) BuildTab()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/user/alpha.txt", "content here")
            .AddFile("/home/user/beta.txt")
            .AddDirectory("/home/user/docs");

        return (new Tab(new DirectoryCache(fs), "/home/user"), fs);
    }

    // ---- Title bar -------------------------------------------------------------------

    [Fact]
    public void TitleBar_ShowsTheCurrentPath()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(60, 1);

        TitleBar bar = new(new DefaultColorScheme()) { Tab = tab, ShowSelection = false };
        bar.Layout(new Rect(0, 0, 60, 1));
        bar.Render(screen);

        Assert.Contains("/home/user", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBar_ShowsTheUserAndHost()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(60, 1);

        TitleBar bar = new(new DefaultColorScheme())
        {
            Tab = tab,
            UserName = "ada",
            HostName = "analytical",
        };
        bar.Layout(new Rect(0, 0, 60, 1));
        bar.Render(screen);

        Assert.StartsWith("ada@analytical:", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBar_WarnsInRedWhenRunningAsRoot()
    {
        // Running a file manager as root deserves a standing reminder.
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(60, 1);

        TitleBar bar = new(new DefaultColorScheme())
        {
            Tab = tab,
            UserName = "root",
            HostName = "host",
            IsRoot = true,
        };
        bar.Layout(new Rect(0, 0, 60, 1));
        bar.Render(screen);

        Assert.Equal(Color.Red, screen[0, 0].Style.Foreground);
    }

    [Fact]
    public void TitleBar_ShortensFromTheLeftWhenTheTerminalIsNarrow()
    {
        // The end of a path identifies where you are; the beginning rarely does.
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(8, 1);

        TitleBar bar = new(new DefaultColorScheme()) { Tab = tab, ShowSelection = false };
        bar.Layout(new Rect(0, 0, 8, 1));
        bar.Render(screen);

        string text = screen.TextAt(0);
        Assert.StartsWith("...", text, StringComparison.Ordinal);
        // The tail survives, so the directory you are actually in is still readable.
        Assert.Contains("user", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBar_ShowsThePartiallyTypedKeySequence()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(60, 1);

        TitleBar bar = new(new DefaultColorScheme()) { Tab = tab, KeyBuffer = "3g" };
        bar.Layout(new Rect(0, 0, 60, 1));
        bar.Render(screen);

        Assert.Contains("3g", screen.TextAt(0), StringComparison.Ordinal);
    }

    // ---- Status bar ------------------------------------------------------------------

    [Fact]
    public void StatusBar_ShowsPermissionsOwnerAndPosition()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(80, 1);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        string text = screen.TextAt(0);
        Assert.StartsWith("drwxr-xr-x", text, StringComparison.Ordinal);
        Assert.Contains("1/3", text, StringComparison.Ordinal);
        Assert.Contains("Top", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBar_ShowsHowManyNamesTheFileHas()
    {
        // Between the permissions and the owner, where `ls -l` and ranger both put it. Nearly
        // always 1, and worth showing for the moment it is not: deleting one name of a file with
        // several does not delete the file, and nothing else on screen says so.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/user/alpha.txt", "content here");

        Tab tab = new(new DirectoryCache(fs), "/home/user");
        ScreenBuffer screen = new(80, 1);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        Assert.StartsWith("-rw-r--r-- 1 ", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBar_ReportsThePositionInTheListing()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(80, 1);

        tab.MoveCursor(2);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        string text = screen.TextAt(0);
        Assert.Contains("3/3", text, StringComparison.Ordinal);
        Assert.Contains("Bot", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBar_ShowsFreeSpace()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(80, 1);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 2_000_000_000 };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        Assert.Contains("2 G free", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBar_SummarisesMarkedEntriesInsteadOfFreeSpace()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(80, 1);

        tab.Current.Entries[1].IsMarked = true;

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 2_000_000_000 };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        string text = screen.TextAt(0);

        // Ranger's shape: the total size over the count, then `Mrk` where the position
        // indicator would be (`gui/widgets/statusbar.py:284-307`).
        Assert.Contains("/1  Mrk", text, StringComparison.Ordinal);
        Assert.DoesNotContain("free", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusBar_ReplacesEverythingWithAMessage()
    {
        // Errors appear where the user is already looking, rather than in a dialog.
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(80, 1);

        StatusBar bar = new(new DefaultColorScheme())
        {
            Tab = tab,
            Message = "no such setting: banana",
            MessageIsError = true,
        };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        string text = screen.TextAt(0);

        // Every headline takes a one-column margin so that a message and the task line that
        // follows it start in the same place; placement itself is pinned in StatusBarTests.
        Assert.StartsWith("no such setting: banana", text.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("Top", text, StringComparison.Ordinal);
        Assert.Equal(Color.Red.Bright(), screen[0, 0].Style.Foreground);
    }

    [Fact]
    public void StatusBar_TintsInProportionToOutstandingWork()
    {
        (Tab tab, _) = BuildTab();
        ScreenBuffer screen = new(100, 1);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, Progress = 0.4 };
        bar.Layout(new Rect(0, 0, 100, 1));
        bar.Render(screen);

        Assert.Equal(new DefaultColorScheme().ProgressBarColor, screen[0, 0].Style.Background);
        Assert.Equal(new DefaultColorScheme().ProgressBarColor, screen[39, 0].Style.Background);
        Assert.NotEqual(new DefaultColorScheme().ProgressBarColor, screen[41, 0].Style.Background);
    }

    [Theory]
    [InlineData(0x81A4, "-rw-r--r--")]  // 0100644 regular
    [InlineData(0x81FF, "-rwxrwxrwx")]  // 0100777 regular
    [InlineData(0x41ED, "drwxr-xr-x")]  // 0040755 directory
    [InlineData(0xA1FF, "lrwxrwxrwx")]  // 0120777 symlink
    public void PermissionString_MatchesLongListingFormat(uint mode, string expected)
    {
        Core.FileSystem.FileStatus status = new(
            Core.FileSystem.FileStatus.DecodeKind(mode), mode, 0, 1, 0, 0, 1, 1,
            default, default, default);

        Assert.Equal(expected, StatusBar.PermissionString(status));
    }
}
