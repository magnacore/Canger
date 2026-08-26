// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

public class TabTests
{
    private static (DirectoryCache Cache, InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/user/a.txt")
            .AddFile("/home/user/b.txt")
            .AddFile("/home/user/docs/report.txt")
            .AddFile("/home/user/docs/notes.txt")
            .AddFile("/home/other/x.txt");

        return (new DirectoryCache(fs), fs);
    }

    // ---- Directory interning ---------------------------------------------------------

    [Fact]
    public void Cache_ReturnsTheSameInstanceForAPath()
    {
        // Shared instances are what make tab switching and breadcrumb columns cheap, and what
        // lets a cursor move be visible everywhere at once.
        (DirectoryCache cache, _) = Build();

        Assert.Same(cache.Get("/home/user"), cache.Get("/home/user"));
    }

    [Fact]
    public void Cache_NormalisesPathsBeforeLookingThemUp()
    {
        (DirectoryCache cache, _) = Build();

        Assert.Same(cache.Get("/home/user"), cache.Get("/home/user/"));
        Assert.Same(cache.Get("/home/user"), cache.Get("/home/user/docs/.."));
    }

    [Fact]
    public void Cache_BuildsThePathwayFromTheRootDown()
    {
        (DirectoryCache cache, _) = Build();

        IReadOnlyList<DirectoryNode> pathway = cache.PathwayTo("/home/user/docs");

        Assert.Equal(["/", "/home", "/home/user", "/home/user/docs"],
                     pathway.Select(d => d.Path));
    }

    [Fact]
    public void Cache_UnloadIdle_KeepsTheListingsStillInUse()
    {
        (DirectoryCache cache, _) = Build();
        cache.GetLoaded("/home/user", TestContext.Current.CancellationToken);
        cache.GetLoaded("/home/other", TestContext.Current.CancellationToken);

        // Scanning /home/user also interned the /home/user/docs it found inside, which is the
        // point of interning: a directory is one object however it is reached.
        Assert.Equal(3, cache.Count);

        // The listings go; the nodes stay, because the cache promises one object per path and a
        // second one would carry a different cursor, different marks and no measured size.
        int unloaded = cache.UnloadIdle(
            keep: new HashSet<DirectoryNode> { cache.Get("/home/user") },
            olderThan: DateTimeOffset.UtcNow.AddMinutes(1));

        // One, not two: /home/user/docs was interned by the scan of its parent but never itself
        // scanned, so it has no listing to give up and costs nothing to be holding.
        Assert.Equal(1, unloaded);
        Assert.Equal(3, cache.Count);
        Assert.True(cache.Get("/home/user").IsLoaded, "the kept directory still has its listing");
        Assert.False(cache.Get("/home/other").IsLoaded, "the idle one gave its listing up");
    }

    [Fact]
    public void Cache_InternsTheDirectoriesAScanFinds()
    {
        // The same object whether it is reached through its parent's listing or by entering it.
        // Without that, a size measured on one is invisible to the other.
        (DirectoryCache cache, _) = Build();
        DirectoryNode user = cache.GetLoaded("/home/user", TestContext.Current.CancellationToken);

        FsNode docs = user.Entries.First(e => e.Basename == "docs");

        Assert.Same(cache.Get("/home/user/docs"), docs);
    }

    [Fact]
    public void Cache_InterningSurvivesAReload()
    {
        // A reload rebuilds the listing, and the row must still be the shared node — otherwise
        // anything recorded on it, such as a measured cumulative size, is silently orphaned.
        (DirectoryCache cache, _) = Build();
        DirectoryNode user = cache.GetLoaded("/home/user", TestContext.Current.CancellationToken);

        FsNode before = user.Entries.First(e => e.Basename == "docs");
        user.Load(TestContext.Current.CancellationToken);
        FsNode after = user.Entries.First(e => e.Basename == "docs");

        Assert.Same(before, after);
    }

    // ---- Navigation ------------------------------------------------------------------

    [Fact]
    public void Enter_MovesTheTabAndLoadsTheDirectory()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user");

        Assert.Equal("/home/user", tab.Path);
        Assert.True(tab.Current.IsLoaded);
        Assert.Contains("a.txt", tab.Current.Entries.Select(e => e.Basename), StringComparer.Ordinal);
    }

    [Fact]
    public void Enter_WithAFilePathMovesToItsDirectoryAndSelectsIt()
    {
        // This is what makes --selectfile and jumping to a search result work.
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user/docs/notes.txt");

        Assert.Equal("/home/user/docs", tab.Path);
        Assert.Equal("notes.txt", tab.Selected?.Basename);
    }

    [Fact]
    public void Enter_BuildsThePathway()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user/docs");

        Assert.Equal(["/", "/home", "/home/user", "/home/user/docs"],
                     tab.Pathway.Select(d => d.Path));
    }

    [Fact]
    public void Enter_PointsEachBreadcrumbColumnAtTheChildItCameThrough()
    {
        // Without this the columns to the left would all sit on their first entry, and the path
        // on screen would not line up with where the tab actually is.
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user/docs");

        DirectoryNode user = tab.Pathway.Single(d => d.Path == "/home/user");
        Assert.Equal("docs", user.Cursor.Current?.Basename);

        DirectoryNode home = tab.Pathway.Single(d => d.Path == "/home");
        Assert.Equal("user", home.Cursor.Current?.Basename);
    }

    [Fact]
    public void GoUp_MovesToTheParentAndSelectsTheDirectoryJustLeft()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user/docs");

        Assert.True(tab.GoUp(TestContext.Current.CancellationToken));

        Assert.Equal("/home/user", tab.Path);
        Assert.Equal("docs", tab.Selected?.Basename);
    }

    [Fact]
    public void GoUp_DoesNothingAtTheRoot()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/");

        Assert.False(tab.GoUp(TestContext.Current.CancellationToken));
        Assert.Equal("/", tab.Path);
    }

    [Fact]
    public void EnterSelected_DescendsIntoADirectory()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user");
        tab.MoveCursorTo(tab.Current.Entries.Single(e => e.Basename == "docs"));

        Assert.True(tab.EnterSelected(TestContext.Current.CancellationToken));
        Assert.Equal("/home/user/docs", tab.Path);
    }

    [Fact]
    public void EnterSelected_DoesNothingOnAFile()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user");
        tab.MoveCursorTo(tab.Current.Entries.Single(e => e.Basename == "a.txt"));

        Assert.False(tab.EnterSelected(TestContext.Current.CancellationToken));
        Assert.Equal("/home/user", tab.Path);
    }

    // ---- History ---------------------------------------------------------------------

    [Fact]
    public void History_RemembersWhereTheTabHasBeen()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user");
        tab.Enter("/home/user/docs", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["/home/user", "/home/user/docs"], tab.History.Entries);
    }

    [Fact]
    public void History_StepsBackAndForward()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user");
        tab.Enter("/home/user/docs", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(tab.GoInHistory(-1, TestContext.Current.CancellationToken));
        Assert.Equal("/home/user", tab.Path);

        Assert.True(tab.GoInHistory(1, TestContext.Current.CancellationToken));
        Assert.Equal("/home/user/docs", tab.Path);
    }

    [Fact]
    public void History_SteppingBackThenSomewhereNewDiscardsTheForwardPath()
    {
        (DirectoryCache cache, _) = Build();
        Tab tab = new(cache, "/home/user");
        tab.Enter("/home/user/docs", cancellationToken: TestContext.Current.CancellationToken);
        tab.GoInHistory(-1, TestContext.Current.CancellationToken);

        tab.Enter("/home/other", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["/home/user", "/home/other"], tab.History.Entries);
        Assert.False(tab.History.CanGoForward);
    }

    // ---- Shared directories between tabs ---------------------------------------------

    [Fact]
    public void TwoTabsShareADirectoryButKeepTheirOwnCursor()
    {
        // Directories are shared instances, so without a per-tab cursor the two tabs would
        // fight over one position and switching tabs would move the selection.
        (DirectoryCache cache, _) = Build();
        Tab first = new(cache, "/home/user");
        Tab second = new(cache, "/home/user");

        Assert.Same(first.Current, second.Current);

        first.MoveCursorTo(first.Current.Entries.Single(e => e.Basename == "b.txt"));
        second.MoveCursorTo(second.Current.Entries.Single(e => e.Basename == "a.txt"));

        first.Activate();
        Assert.Equal("b.txt", first.Selected?.Basename);

        second.Activate();
        Assert.Equal("a.txt", second.Selected?.Basename);
    }

    [Fact]
    public void Activate_RestoresTheCursorAfterAnotherTabMovedIt()
    {
        (DirectoryCache cache, _) = Build();
        Tab first = new(cache, "/home/user");
        first.MoveCursorTo(first.Current.Entries.Single(e => e.Basename == "b.txt"));

        Tab second = new(cache, "/home/user");
        second.MoveCursorTo(second.Current.Entries.Single(e => e.Basename == "docs"));

        first.Activate();

        Assert.Equal("b.txt", first.Selected?.Basename);
    }
}
