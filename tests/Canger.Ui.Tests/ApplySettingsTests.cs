// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Which directories the listing settings reach.
/// </summary>
/// <remarks>
/// Reported as backspace — <c>set show_hidden!</c> — updating every column except the preview one
/// on the right, which kept the listing it already had. The settings were pushed onto
/// <c>Pathway</c>, which is the ancestry and the current directory and nothing else; the preview
/// directory got only <c>autoupdate_cumulative_size</c>. Ranger has no such gap because every
/// directory binds itself to <c>show_hidden</c>, <c>hidden_filter</c> and the sort options when it
/// is constructed (<c>container/directory.py:140-148</c>), so all of them refilter at once.
///
/// The same omission was silently doing it to the sort order.
/// </remarks>
public class ApplySettingsTests
{
    private const string HiddenFilter = @"^\.";

    private static Tab Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/work")
            .AddDirectory("/home/work/notes")
            .AddFile("/home/work/notes/visible.txt")
            .AddFile("/home/work/notes/another.txt")
            .AddFile("/home/work/notes/third.txt")
            .AddFile("/home/work/notes/.hidden.txt")
            .AddFile("/home/work/a.txt")
            .AddFile("/home/work/.b.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/work", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        // Cursor onto `notes`, which is what puts it in the preview column.
        IReadOnlyList<FsNode> entries = tab.Current.Entries;
        int index = entries.ToList().FindIndex(e => e.Basename == "notes");
        Assert.True(index >= 0, "notes is not in the listing");
        tab.Current.Cursor.MoveTo(index, entries);

        tab.SelectedDirectory?.Load(TestContext.Current.CancellationToken);

        return tab;
    }

    private static void Apply(Tab tab, bool showHidden, bool reverse = false) =>
        Browser.ApplySettings(
            tab,
            new Dictionary<int, Tab> { [1] = tab },
            viewmode: null,
            new SortOrder(SortKey.Basename, reverse, true, true, false),
            showHidden,
            HiddenFilter,
            autoupdate: false);

    private static IReadOnlyList<string> Names(DirectoryNode directory) =>
        [.. directory.Entries.Select(e => e.Basename)];

    [Fact]
    public void ShowHiddenReachesThePreviewColumn()
    {
        // The reported defect. The preview directory is on screen and must list what every other
        // column lists.
        Tab tab = Build();
        Apply(tab, showHidden: false);

        DirectoryNode preview = Assert.IsType<DirectoryNode>(tab.SelectedDirectory);
        Assert.DoesNotContain(".hidden.txt", Names(preview), StringComparer.Ordinal);

        Apply(tab, showHidden: true);

        Assert.Contains(".hidden.txt", Names(preview), StringComparer.Ordinal);
    }

    [Fact]
    public void ShowHiddenStillReachesTheCurrentDirectory()
    {
        // The half that always worked; it must keep working.
        Tab tab = Build();
        Apply(tab, showHidden: false);
        Assert.DoesNotContain(".b.txt", Names(tab.Current), StringComparer.Ordinal);

        Apply(tab, showHidden: true);
        Assert.Contains(".b.txt", Names(tab.Current), StringComparer.Ordinal);
    }

    [Fact]
    public void TheSortOrderReachesThePreviewColumnToo()
    {
        // Not what was reported, but the same omission: the preview column kept whatever order it
        // was loaded with however the `sort` settings changed.
        Tab tab = Build();
        Apply(tab, showHidden: true);

        DirectoryNode preview = Assert.IsType<DirectoryNode>(tab.SelectedDirectory);
        IReadOnlyList<string> ascending = Names(preview);

        // More than one name, or reversing it would prove nothing — the first version of this
        // test had a single visible entry and passed happily with the defect in place.
        Assert.True(ascending.Count > 1, "the preview needs several entries to have an order");

        Apply(tab, showHidden: true, reverse: true);

        Assert.Equal(ascending.Reverse(), Names(preview));
    }

    [Fact]
    public void EveryVisibleDirectoryIsReached()
    {
        // Stated as the rule rather than as three separate cases: whatever `VisibleDirectories`
        // returns is what gets configured, so a column added later cannot be missed.
        Tab tab = Build();
        Apply(tab, showHidden: true);

        Assert.All(Browser.VisibleDirectories(tab, new Dictionary<int, Tab> { [1] = tab }, null),
                   directory => Assert.True(directory.ShowHidden));
    }
}

/// <summary>
/// Where the cursor lands when a directory is opened under a sort other than the default.
/// </summary>
/// <remarks>
/// Reported as "if a folder is sorted by mtime, the first alphabetical item is highlighted rather
/// than the first item, even if it is in the middle of the list". The cursor is placed by the
/// first load, and a directory only learned its sort order a frame later, when something walked
/// the visible columns. So the first load ordered by name, put the cursor on the first name, and
/// the real order then carried the cursor to wherever that name belonged.
/// </remarks>
public class DirectorySettingsAtOpenTests
{
    /// <summary>A tree whose name order and size order are opposites.</summary>
    private static (Tab Tab, DirectoryCache Cache) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/work")
            .AddDirectory("/home/work/later")
            .AddFileOfSize("/home/work/later/a.txt", 10)
            .AddFileOfSize("/home/work/later/b.txt", 20)
            .AddFileOfSize("/home/work/later/c.txt", 30)
            .AddFile("/home/work/here.txt")

            // Outside the tree the tab walks, so nothing lists it into existence.
            .AddDirectory("/elsewhere")
            .AddDirectory("/elsewhere/fresh")
            .AddFile("/elsewhere/fresh/x.txt")
            .AddFile("/elsewhere/fresh/y.txt")
            .AddFile("/elsewhere/fresh/z.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/work", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        // The cursor is parked on the file, deliberately, so that `later` is *not* the previewed
        // directory. With the cursor on it, it is on screen and is configured directly by the
        // walk over the visible columns — and a test written that way passes with the fix removed,
        // which is what the first draft of these did.
        IReadOnlyList<FsNode> entries = tab.Current.Entries;
        int file = entries.ToList().FindIndex(e => e.Basename == "here.txt");
        Assert.True(file >= 0, "here.txt is not in the listing");

        // Through the tab, not the directory: the tab keeps a cursor of its own and the preview
        // column follows that one. Moving only the directory's left `later` previewed, and a
        // previewed directory is configured directly — which is exactly the hole this test is
        // meant to avoid falling into.
        tab.MoveCursor(file);

        Assert.Null(tab.SelectedDirectory);

        return (tab, cache);
    }

    /// <summary>Pushes the settings the way the render path does.</summary>
    private static void Apply(Tab tab, SortKey key, bool reverse)
    {
        Browser.ApplySettings(
            tab,
            new Dictionary<int, Tab> { [1] = tab },
            viewmode: null,
            new SortOrder(key, reverse, DirectoriesFirst: true, CaseInsensitive: true,
                          UseUnicodeCollation: false),
            showHidden: false,
            hiddenPattern: string.Empty,
            autoupdate: false);
    }

    [Fact]
    public void OpeningADirectoryPutsTheCursorOnItsFirstRow()
    {
        // Reversed, so the first row is c.txt while the first name is still a.txt. The two have
        // to differ, or the test passes whichever row the cursor lands on.
        (Tab tab, DirectoryCache cache) = Build();

        Apply(tab, SortKey.Natural, reverse: true);

        DirectoryNode opened = cache.GetLoaded("/home/work/later",
                                               TestContext.Current.CancellationToken);

        Assert.Equal(["c.txt", "b.txt", "a.txt"],
                     opened.Entries.Select(e => e.Basename).ToArray());
        Assert.Equal("c.txt", opened.Selected?.Basename);
    }

    [Fact]
    public void OpeningADirectoryAlreadyKnownToTheCacheAlsoLandsOnItsFirstRow()
    {
        // The case that made the first attempt at this useless. A subdirectory's node is created
        // while its parent is listed, which is long before the user changes the sort — so
        // configuring a directory only when it is *created* left this one exactly as it was.
        (Tab tab, DirectoryCache cache) = Build();

        // Listing the parent has already made the node, unloaded.
        Assert.False(cache.Get("/home/work/later").IsLoaded);

        Apply(tab, SortKey.Natural, reverse: true);

        DirectoryNode opened = cache.GetLoaded("/home/work/later",
                                               TestContext.Current.CancellationToken);

        Assert.Equal("c.txt", opened.Selected?.Basename);
    }

    [Fact]
    public void ADirectoryNeverSeenBeforeAlsoOpensOnItsFirstRow()
    {
        // `:cd` to somewhere outside the tree that has been walked. No parent listing has made
        // this node, so it is built on the spot and must be born configured — the other half of
        // the fix, and one the other tests cannot reach because their directory always exists by
        // the time they ask for it.
        (Tab tab, DirectoryCache cache) = Build();

        Apply(tab, SortKey.Natural, reverse: true);

        DirectoryNode opened = cache.GetLoaded("/elsewhere/fresh",
                                               TestContext.Current.CancellationToken);

        Assert.Equal(["z.txt", "y.txt", "x.txt"],
                     opened.Entries.Select(e => e.Basename).ToArray());
        Assert.Equal("z.txt", opened.Selected?.Basename);
    }

    [Fact]
    public void ALinkOnTheFirstRowIsSelectedLikeAnythingElse()
    {
        // The other half of the report: "if the first item in a folder is a link ranger selects
        // it, but canger selects the first folder which is a non-link". It was the same defect
        // seen from another angle — the cursor was stuck on the alphabetically first directory —
        // so what has to be pinned is that nothing about a link makes the cursor step over it.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/work")
            .AddDirectory("/home/work/linked")
            .AddDirectory("/home/work/linked/real")
            .AddFile("/home/work/here.txt");

        fs.AddSymbolicLink("/home/work/linked/aaa-link", "/home/work/linked/real");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/work", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        IReadOnlyList<FsNode> entries = tab.Current.Entries;
        tab.MoveCursor(entries.ToList().FindIndex(e => e.Basename == "here.txt"));
        Assert.Null(tab.SelectedDirectory);

        Browser.ApplySettings(
            tab,
            new Dictionary<int, Tab> { [1] = tab },
            viewmode: null,
            new SortOrder(SortKey.Natural, Reverse: false, DirectoriesFirst: true,
                          CaseInsensitive: true, UseUnicodeCollation: false),
            showHidden: false,
            hiddenPattern: string.Empty,
            autoupdate: false);

        DirectoryNode opened = cache.GetLoaded("/home/work/linked",
                                               TestContext.Current.CancellationToken);

        Assert.Equal("aaa-link", opened.Entries[0].Basename);
        Assert.Equal("aaa-link", opened.Selected?.Basename);
    }

    [Fact]
    public void TheDefaultSortStillOpensOnItsFirstRow()
    {
        // The case that always worked, kept so a fix aimed at the other one cannot break it.
        (Tab tab, DirectoryCache cache) = Build();

        Apply(tab, SortKey.Natural, reverse: false);

        DirectoryNode opened = cache.GetLoaded("/home/work/later",
                                               TestContext.Current.CancellationToken);

        Assert.Equal("a.txt", opened.Selected?.Basename);
    }
}
