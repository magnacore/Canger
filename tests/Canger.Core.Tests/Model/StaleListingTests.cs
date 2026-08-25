// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Noticing that a directory changed underneath the listing.
/// </summary>
/// <remarks>
/// Two separate faults, both reported from real use and neither covered before.
///
/// A listing was only ever re-read when something Canger itself did finished, so a file created by
/// a forked <c>shell -f cp …</c> stayed invisible, and a paste from another directory took an
/// unbounded time to appear. Ranger re-stats each visible directory from the draw path
/// (<c>container/directory.py:686-711</c>, called from <c>gui/widgets/view_miller.py:98</c>).
///
/// And a tab keeps its own cursor, adopted from the directory's only when it enters or is
/// activated — so a reload left the tab pointing at a node no longer in the listing. After
/// <c>mkdirmv</c> that meant the cursor still held the file it had just moved away, so pressing
/// <c>l</c> asked rifle to open a vanished file instead of entering the directory that replaced
/// it. Ranger's <c>Tab.pointer</c> is a self-healing property for exactly this reason
/// (<c>core/tab.py:59-68</c>).
/// </remarks>
public class StaleListingTests
{
    private static DirectoryNode Loaded(IFileSystem fs, string path)
    {
        DirectoryNode directory = new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        return directory;
    }

    // ---- LoadIfOutdated -----------------------------------------------------------------

    [Fact]
    public void LoadIfOutdated_RereadsWhenAFileAppearedFromOutside()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/first.txt");
        DirectoryNode directory = Loaded(fs, "/home");

        Assert.Equal(1, directory.Count);

        // Something Canger did not do: a forked cp, another pane, another program.
        fs.AddFile("/home/second.txt", modified: DateTimeOffset.UtcNow.AddSeconds(5));
        fs.Touch("/home", DateTimeOffset.UtcNow.AddSeconds(5));

        Assert.True(directory.LoadIfOutdated(TestContext.Current.CancellationToken));
        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public void LoadIfOutdated_DoesNothingWhenTheDirectoryIsUnchanged()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        DirectoryNode directory = Loaded(fs, "/home");

        int listings = fs.ListCount;

        Assert.False(directory.LoadIfOutdated(TestContext.Current.CancellationToken));
        Assert.Equal(listings, fs.ListCount);
    }

    [Fact]
    public void LoadIfOutdated_LoadsADirectoryThatWasNeverRead()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        DirectoryNode directory = new(fs, "/home",
                                      fs.GetStatus("/home", followSymbolicLinks: true));

        Assert.True(directory.LoadIfOutdated(TestContext.Current.CancellationToken));
        Assert.True(directory.IsLoaded);
    }

    [Fact]
    public void LoadIfOutdated_LeavesAFlattenedListingAlone()
    {
        // Ranger compares the maximum mtime across every folded level, which means walking the
        // tree on every draw. On a flattened directory of twenty thousand entries that would cost
        // far more than the staleness it prevents, so a flat listing is refreshed explicitly.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/sub/a.txt");
        DirectoryNode directory = Loaded(fs, "/home");
        directory.SetFlatLevel(-1, TestContext.Current.CancellationToken);

        fs.Touch("/home", DateTimeOffset.UtcNow.AddSeconds(5));

        Assert.False(directory.LoadIfOutdated(TestContext.Current.CancellationToken));
    }

    // ---- the tab's cursor ---------------------------------------------------------------

    [Fact]
    public void TheTabCursor_DoesNotKeepAnEntryThatLeftTheListing()
    {
        // The mkdirmv report: the file under the cursor is moved away, the listing is re-read, and
        // the tab must not still be pointing at the file that has gone.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/moveme.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home");

        Assert.Equal("moveme.txt", tab.Selected?.Basename);

        // What the move leaves behind: the file gone, a directory in its place.
        fs.Delete("/home/moveme.txt");
        fs.AddDirectory("/home/TARGET");
        tab.Current.Load(TestContext.Current.CancellationToken);

        Assert.Equal("TARGET", tab.Selected?.Basename);
        Assert.True(tab.Selected?.IsDirectory);
    }

    [Fact]
    public void TheTabCursor_FollowsAnEntryThatMovedWithinTheListing()
    {
        // A reload must not lose the selection just because its index changed.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/b.txt")
            .AddFile("/home/c.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home");
        tab.MoveCursorTo(tab.Current.Entries.First(e => e.Basename == "c.txt"));

        fs.AddFile("/home/a.txt");
        tab.Current.Load(TestContext.Current.CancellationToken);

        Assert.Equal("c.txt", tab.Selected?.Basename);
    }

    [Fact]
    public void TheTabCursor_HandlesTheListingBecomingEmpty()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/only.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home");

        fs.Delete("/home/only.txt");
        tab.Current.Load(TestContext.Current.CancellationToken);

        Assert.Null(tab.Selected);
    }
}
