// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// What a node remembers belongs to the thing the name pointed at, and must not outlive it.
/// </summary>
/// <remarks>
/// <para>
/// Reported as folders arriving marked: two directories were pasted into a folder, and the first
/// of them came up selected, every time. Nothing marks anything during a paste — the instrumented
/// run logged no mark at all after it — because the mark was never set a second time. It had
/// never been cleared.
/// </para>
/// <para>
/// Directory entries are interned by path (<c>DirectoryCache.Intern</c>), so the row for
/// <c>x/notes</c> is the same object every time the listing is scanned, which is what lets a size
/// measured in one column show in another. A directory that is deleted and later recreated gets
/// that same object back, with whatever was set on it — including <c>IsMarked</c>. Files are
/// built afresh on each scan, so this could only ever happen to a folder, which is exactly what
/// the report describes.
/// </para>
/// </remarks>
public class ResurrectedEntryTests
{
    private static (DirectoryCache Cache, InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/notes")
            .AddFile("/home/notes/one.txt")
            .AddDirectory("/home/other");

        return (new DirectoryCache(fs), fs);
    }

    private static DirectoryNode Loaded(DirectoryCache cache, string path)
    {
        DirectoryNode directory = cache.Get(path);
        directory.Load(TestContext.Current.CancellationToken);
        return directory;
    }

    private static FsNode Entry(DirectoryNode directory, string name) =>
        directory.Entries.Single(e => e.Basename == name);

    [Fact]
    public void ADirectoryDeletedWhileMarkedDoesNotComeBackMarked()
    {
        (DirectoryCache cache, InMemoryFileSystem fs) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Entry(home, "notes").IsMarked = true;

        // Deleted, and the listing re-read — which is what the delete does.
        fs.DeleteRecursive("/home/notes");
        home.Load(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(home.Entries, e => e.Basename == "notes");

        // Pasted back, under the same name.
        fs.AddDirectory("/home/notes").AddFile("/home/notes/one.txt");
        home.Load(TestContext.Current.CancellationToken);

        Assert.False(Entry(home, "notes").IsMarked);
        Assert.Empty(home.MarkedEntries);
    }

    [Fact]
    public void AMarkStillSurvivesAnOrdinaryReload()
    {
        // The other half: a mark is meant to outlast a reload of a directory that is still there,
        // which is what `RememberMarks` and `RestoreMarks` are for.
        (DirectoryCache cache, InMemoryFileSystem fs) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Entry(home, "notes").IsMarked = true;

        fs.AddFile("/home/later.txt");
        home.Load(TestContext.Current.CancellationToken);

        Assert.True(Entry(home, "notes").IsMarked);
        Assert.Equal(["notes"], home.MarkedEntries.Select(e => e.Basename));
    }

    [Fact]
    public void AMeasuredSizeDoesNotSurviveTheFolderItMeasured()
    {
        // The same cause with a different face, and found by looking for one: a folder that had
        // been measured, deleted and replaced went on reporting the old figure — 4 MB for ten
        // bytes — without even the mark that says the number is doubtful.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/notes")
            .AddFileOfSize("/home/notes/big.bin", 4_000_000);

        DirectoryCache cache = new(fs);
        DirectoryNode home = Loaded(cache, "/home");

        FsNode notes = Entry(home, "notes");
        notes.CumulativeSize = 4_000_000;
        notes.VcsStatus = "modified";

        fs.DeleteRecursive("/home/notes");
        home.Load(TestContext.Current.CancellationToken);

        fs.AddDirectory("/home/notes").AddFileOfSize("/home/notes/small.bin", 10);
        home.Load(TestContext.Current.CancellationToken);

        FsNode back = Entry(home, "notes");

        Assert.Null(back.CumulativeSize);
        Assert.False(back.CumulativeSizeStale);
        Assert.Null(back.VcsStatus);
    }

    [Fact]
    public void AMeasuredSizeSurvivesAnOrdinaryReload()
    {
        // The control for the one above: the same folder being re-read keeps its figure, which is
        // the whole point of measuring it.
        (DirectoryCache cache, InMemoryFileSystem fs) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Entry(home, "notes").CumulativeSize = 1234;

        fs.AddFile("/home/later.txt");
        home.Load(TestContext.Current.CancellationToken);

        Assert.Equal(1234, Entry(home, "notes").CumulativeSize);
    }

    [Fact]
    public void AMarkSurvivesTheListingBeingDroppedAndReadAgain()
    {
        // `Unload` saves the marks on the way out precisely because the listing it would read
        // them from is gone, and that path must not be broken by clearing them on the way in.
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = Loaded(cache, "/home");

        Entry(home, "notes").IsMarked = true;

        Assert.True(home.Unload());
        home.Load(TestContext.Current.CancellationToken);

        Assert.True(Entry(home, "notes").IsMarked);
    }
}
