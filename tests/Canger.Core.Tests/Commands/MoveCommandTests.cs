// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What a page of movement means, and whether the ends wrap.
/// </summary>
/// <remarks>
/// Both came from the same line. <c>Direction.FromArguments</c> never consulted the settings, so
/// <c>wrap_scroll</c> did nothing at all; and the page size was <c>scroll_offset * 2</c> — a
/// constant sixteen whatever the terminal was — where ranger uses the browser's own height
/// (<c>core/actions.py:485</c> and <c>:522</c>).
/// </remarks>
public class MoveCommandTests
{
    private static FakeFileManager Manager(int entries = 60)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home");

        for (int i = 1; i <= entries; i++)
        {
            fs.AddFile($"/home/f{i:D2}.txt");
        }

        return new FakeFileManager(fs, "/home");
    }

    private static int Cursor(FakeFileManager manager) => manager.CurrentTab.Current.Cursor.Index;

    [Fact]
    public void Move_APageIsTheBrowsersHeight()
    {
        FakeFileManager manager = Manager();
        manager.BrowserHeight = 40;

        manager.Execute("move down=1 pages=True");

        Assert.Equal(40, Cursor(manager));
    }

    [Fact]
    public void Move_APageFollowsTheTerminalRatherThanAConstant()
    {
        // The defect: a page was sixteen rows on every terminal, so page-down on a tall window
        // moved a third of the way down it.
        FakeFileManager tall = Manager();
        tall.BrowserHeight = 40;
        tall.Execute("move down=1 pages=True");

        FakeFileManager shortOne = Manager();
        shortOne.BrowserHeight = 10;
        shortOne.Execute("move down=1 pages=True");

        Assert.Equal(40, Cursor(tall));
        Assert.Equal(10, Cursor(shortOne));
    }

    [Fact]
    public void Move_HalfAPageIsHalfTheHeight()
    {
        FakeFileManager manager = Manager();
        manager.BrowserHeight = 40;

        manager.Execute("move down=0.5 pages=True");

        Assert.Equal(20, Cursor(manager));
    }

    [Fact]
    public void Move_StopsAtTheEndWithoutWrapScroll()
    {
        FakeFileManager manager = Manager();
        manager.Execute("move to=-1");
        manager.Execute("move down=1");

        Assert.Equal(59, Cursor(manager));
    }

    [Fact]
    public void Move_WrapsPastTheEndWithWrapScroll()
    {
        FakeFileManager manager = Manager();
        manager.SettingsStore.Set("wrap_scroll", true);
        manager.Execute("move to=-1");
        manager.Execute("move down=1");

        Assert.Equal(0, Cursor(manager));
    }

    [Fact]
    public void Move_WrapsBackwardsPastTheStartToo()
    {
        FakeFileManager manager = Manager();
        manager.SettingsStore.Set("wrap_scroll", true);
        manager.Execute("move up=1");

        Assert.Equal(59, Cursor(manager));
    }
}
