// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Opening, closing and reordering tabs.
/// </summary>
public class TabCommandTests
{
    private static FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddDirectory("/home/sub")
            .AddFile("/home/sub/b.txt")
            .AddDirectory("/other");

        return new FakeFileManager(fs, "/home");
    }

    private static int[] Numbers(FakeFileManager manager) => [.. manager.Tabs.Keys.Order()];

    [Fact]
    public void TabNew_OpensTheLowestFreeNumberAndSwitchesToIt()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_new");

        Assert.Equal([1, 2], Numbers(manager));
        Assert.Equal(2, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabNew_NamesTheTabWhenTheLineCarriesALabel()
    {
        // What `map bk eval fm.tab_new(narg='Books', path="~/Books")` translates to.
        FakeFileManager manager = Manager();

        manager.Execute("tab_new /other label=Books");

        Assert.Equal("Books", manager.CurrentTab.Label);
        Assert.Equal("/other", manager.CurrentTab.Path);
    }

    [Fact]
    public void TabNew_KeepsALabelThatContainsSpaces()
    {
        // `narg='Task Capture Bin'` is one string in ranger. Splitting the line into named
        // arguments on whitespace kept only `Task`, so the label runs to the end of the line.
        FakeFileManager manager = Manager();

        manager.Execute("tab_new /other label=Task Capture Bin");

        Assert.Equal("Task Capture Bin", manager.CurrentTab.Label);
        Assert.Equal("/other", manager.CurrentTab.Path);
    }

    [Fact]
    public void TabNew_LeavesAnUnlabelledTabUnnamed()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_new /other");

        Assert.Null(manager.CurrentTab.Label);
    }

    [Fact]
    public void TabNew_LabelsATabOpenedByNumber()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_new /other label=Books", quantifier: 4);

        Assert.Equal(4, manager.CurrentTabNumber);
        Assert.Equal("Books", manager.CurrentTab.Label);
    }

    [Fact]
    public void TabNew_StartsWhereTheCurrentTabIs()
    {
        // Opening a tab is a way of keeping a place, so the new one had better be at that place.
        FakeFileManager manager = Manager();

        manager.Execute("tab_new");

        Assert.Equal("/home", manager.CurrentTab.Path);
    }

    [Fact]
    public void TabNew_CanBeGivenSomewhereToGo()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_new /other");

        Assert.Equal("/other", manager.CurrentTab.Path);
    }

    [Fact]
    public void TabNew_FillsAGapRatherThanAlwaysAppending()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new");
        manager.Execute("tab_new");
        manager.Execute("tab_open 2");
        manager.Execute("tab_close");

        manager.Execute("tab_new");

        Assert.Equal([1, 2, 3], Numbers(manager));
    }

    [Fact]
    public void TabNew_HonoursAQuantifierAsTheTabNumber()
    {
        // 3gn opens tab three specifically, so a tab can get a number worth remembering.
        FakeFileManager manager = Manager();

        manager.Execute("tab_new", quantifier: 3);

        Assert.Equal([1, 3], Numbers(manager));
        Assert.Equal(3, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabOpen_CreatesTheTabWhenThereIsNone()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_open 4");

        Assert.Equal([1, 4], Numbers(manager));
        Assert.Equal(4, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabOpen_SwitchesToAnExistingTabWithoutDisturbingIt()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new /other");
        manager.Execute("tab_open 1");
        manager.Execute("tab_open 2");

        Assert.Equal("/other", manager.CurrentTab.Path);
        Assert.Equal(2, manager.Tabs.Count);
    }

    [Fact]
    public void TabOpen_RejectsSomethingThatIsNotATabNumber()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_open banana");

        Assert.Single(manager.Tabs);
        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void TabClose_RemovesTheCurrentTabAndFallsBackToAnother()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new");

        manager.Execute("tab_close");

        Assert.Equal([1], Numbers(manager));
        Assert.Equal(1, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabClose_RefusesToCloseTheLastTab()
    {
        // There would be nothing left to show.
        FakeFileManager manager = Manager();

        manager.Execute("tab_close");

        Assert.Single(manager.Tabs);
        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void TabMove_CyclesForwardAndWrapsAtTheEnd()
    {
        // Wrapping is what makes gt alone step through every tab.
        FakeFileManager manager = Manager();
        manager.Execute("tab_new");
        manager.Execute("tab_new");
        manager.Execute("tab_open 1");

        manager.Execute("tab_move 1");
        Assert.Equal(2, manager.CurrentTabNumber);

        manager.Execute("tab_move 1");
        Assert.Equal(3, manager.CurrentTabNumber);

        manager.Execute("tab_move 1");
        Assert.Equal(1, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabMove_CyclesBackwardsToo()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new");
        manager.Execute("tab_new");
        manager.Execute("tab_open 1");

        manager.Execute("tab_move -1");

        Assert.Equal(3, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabMove_StepsOverGapsRatherThanCountingNumbers()
    {
        // The tabs are 1 and 5; one step from 1 is 5, not "tab 2".
        FakeFileManager manager = Manager();
        manager.Execute("tab_open 5");
        manager.Execute("tab_open 1");

        manager.Execute("tab_move 1");

        Assert.Equal(5, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabMove_HonoursAQuantifierAsATabNumber()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new");
        manager.Execute("tab_new");

        manager.Execute("tab_move 1", quantifier: 1);

        Assert.Equal(1, manager.CurrentTabNumber);
    }

    [Fact]
    public void TabShift_MovesTheCurrentTabRatherThanTheFocus()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new /other");

        manager.Execute("tab_shift 1");

        Assert.Equal(3, manager.CurrentTabNumber);
        Assert.Equal("/other", manager.CurrentTab.Path);
        Assert.Equal([1, 3], Numbers(manager));
    }

    [Fact]
    public void TabShift_PushesWhateverIsInTheWayAsideRatherThanOverwritingIt()
    {
        // No tab is ever lost to a shift, however crowded the numbers are.
        FakeFileManager manager = Manager();
        manager.Execute("tab_open 2");
        manager.Execute("tab_open 3");
        manager.Execute("tab_open 1");

        // Tab 2 is displaced into the 1 this tab is vacating, so all three survive.
        manager.Execute("tab_shift 1");

        Assert.Equal(3, manager.Tabs.Count);
        Assert.Equal(2, manager.CurrentTabNumber);
        Assert.Equal([1, 2, 3], Numbers(manager));
    }

    [Fact]
    public void TabRestore_ReopensTheLastClosedTabWhereItsPathWas()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new /other");
        manager.Execute("tab_close");

        manager.Execute("tab_restore");

        Assert.Equal(2, manager.Tabs.Count);
        Assert.Equal("/other", manager.CurrentTab.Path);
    }

    [Fact]
    public void TabRestore_SaysSoWhenThereIsNothingToRestore()
    {
        FakeFileManager manager = Manager();

        manager.Execute("tab_restore");

        Assert.Single(manager.Tabs);
        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void TabRestore_ReopensTabsMostRecentFirst()
    {
        FakeFileManager manager = Manager();
        manager.Execute("tab_new /other");
        manager.Execute("tab_new /home/sub");
        manager.Execute("tab_close");
        manager.Execute("tab_open 2");
        manager.Execute("tab_close");

        manager.Execute("tab_restore");

        Assert.Equal("/other", manager.CurrentTab.Path);
    }
}
