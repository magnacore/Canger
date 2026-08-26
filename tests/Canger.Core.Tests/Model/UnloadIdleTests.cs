// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Letting go of listings the user has finished with, without letting go of anything they would
/// notice.
/// </summary>
/// <remarks>
/// The cache never forgets a directory — one object per path is what makes a size measured in one
/// column visible in another. What it can drop is the listing, which is where the memory is:
/// around a kilobyte an entry against a node of a dozen small fields. So the question these
/// answer is not "was it freed" but "did anything go missing".
/// </remarks>
public class UnloadIdleTests
{
    private static (DirectoryCache Cache, InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.txt");

        return (new DirectoryCache(fs), fs);
    }

    private static DirectoryNode Loaded(DirectoryCache cache, string path)
    {
        DirectoryNode directory = cache.Get(path);
        directory.Load(TestContext.Current.CancellationToken);
        return directory;
    }

    [Fact]
    public void Unload_DropsTheListingAndKeepsTheNode()
    {
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Assert.True(home.Unload());

        Assert.False(home.IsLoaded);
        Assert.Empty(home.Entries);
        Assert.Same(home, cache.Get("/home"));
    }

    [Fact]
    public void Unload_SaysNothingHappenedWhenThereWasNoListing()
    {
        (DirectoryCache cache, _) = Build();

        Assert.False(cache.Get("/home").Unload());
    }

    [Fact]
    public void Unload_KeepsMarksSoTheyComeBack()
    {
        // RememberMarks reads the entries, so it has to run before they are cleared. Get that
        // order wrong and a selection quietly disappears while the user is in another directory.
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");
        home.Entries.First(e => e.Basename == "beta.txt").IsMarked = true;

        home.Unload();
        home.Load(TestContext.Current.CancellationToken);

        Assert.Equal(["beta.txt"], home.MarkedEntries.Select(e => e.Basename));
    }

    [Fact]
    public void Unload_KeepsTheCursorRowButLetsGoOfTheEntry()
    {
        // The row is what puts the user back where they were. The entry has to go: `Selected` is
        // the cursor's entry, and handing back a file that is no longer in the listing is how a
        // command ends up acting on something that is not there.
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");
        home.Cursor.MoveTo(2, home.Entries);

        home.Unload();

        Assert.Equal(2, home.Cursor.Index);
        Assert.Null(home.Selected);

        home.Load(TestContext.Current.CancellationToken);
        Assert.Equal("gamma.txt", home.Selected?.Basename);
    }

    [Fact]
    public void Unload_KeepsTheSortOrderAndFilters()
    {
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");
        home.SortOrder = new SortOrder(SortKey.Basename, Reverse: true);

        home.Unload();
        home.Load(TestContext.Current.CancellationToken);

        Assert.Equal("gamma.txt", home.Entries[0].Basename);
    }

    [Fact]
    public void Unload_IsUndoneByAskingForTheDirectoryAgain()
    {
        // Nothing has to be told the listing went. This is the path `Enter` takes.
        (DirectoryCache cache, _) = Build();
        Loaded(cache, "/home").Unload();

        Assert.Equal(3, cache.GetLoaded("/home", TestContext.Current.CancellationToken)
                            .Entries.Count);
    }

    [Fact]
    public void Unload_IsUndoneByDrawingTheDirectoryAgain()
    {
        // And this is the path the miller view takes for a column it is about to draw.
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");
        home.Unload();

        Assert.True(home.LoadIfOutdated(TestContext.Current.CancellationToken));
        Assert.Equal(3, home.Entries.Count);
    }

    [Fact]
    public void UnloadIdle_LeavesAloneWhatIsStillBeingUsed()
    {
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Assert.Equal(0, cache.UnloadIdle(new HashSet<DirectoryNode> { home },
                                         DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.True(home.IsLoaded);
    }

    [Fact]
    public void UnloadIdle_LeavesAloneWhatWasScannedRecently()
    {
        // The age test, on its own: nothing is kept, but the listing is new.
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Assert.Equal(0, cache.UnloadIdle(new HashSet<DirectoryNode>(), DateTimeOffset.UtcNow.AddMinutes(-20)));
        Assert.True(home.IsLoaded);
    }

    [Fact]
    public void UnloadIdle_DropsAListingNobodyIsUsingOrHasTouched()
    {
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Assert.Equal(1, cache.UnloadIdle(new HashSet<DirectoryNode>(), DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.False(home.IsLoaded);
        Assert.Equal(1, cache.Count);
    }
}
