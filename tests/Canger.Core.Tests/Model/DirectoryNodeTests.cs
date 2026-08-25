// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Core.Model.Filters;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

public class DirectoryNodeTests
{
    /// <summary>Builds a loaded directory over an in-memory tree.</summary>
    private static DirectoryNode Load(InMemoryFileSystem fs, string path = "/home")
    {
        DirectoryNode directory = new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);
        return directory;
    }

    private static string[] Names(DirectoryNode directory) =>
        [.. directory.Entries.Select(e => e.RelativePath)];

    [Fact]
    public void Load_ListsTheDirectoryContents()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/b.txt")
            .AddFile("/home/a.txt")
            .AddDirectory("/home/sub");

        DirectoryNode directory = Load(fs);

        Assert.True(directory.IsLoaded);
        Assert.Null(directory.LoadError);
        Assert.Equal(["sub", "a.txt", "b.txt"], Names(directory));
    }

    [Fact]
    public void Load_BuildsDirectoriesAndFilesAsDifferentKinds()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddDirectory("/home/sub");

        DirectoryNode directory = Load(fs);

        Assert.IsType<DirectoryNode>(directory.Entries.Single(e => e.Basename == "sub"));
        Assert.IsType<FileNode>(directory.Entries.Single(e => e.Basename == "a.txt"));
    }

    [Fact]
    public void Load_RecordsAnErrorRatherThanThrowingOnAnUnreadableDirectory()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/secret")
            .MakeInaccessible("/home/secret");

        DirectoryNode directory = Load(fs, "/home/secret");

        Assert.NotNull(directory.LoadError);
        Assert.Empty(directory.Entries);
    }

    [Fact]
    public void Load_ResolvesSymbolicLinks()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/target")
            .AddSymbolicLink("/home/good", "/target")
            .AddSymbolicLink("/home/broken", "/nowhere");

        DirectoryNode directory = Load(fs);

        FsNode good = directory.Entries.Single(e => e.Basename == "good");
        FsNode broken = directory.Entries.Single(e => e.Basename == "broken");

        Assert.True(good.IsSymbolicLink);
        Assert.True(good.IsDirectory);
        Assert.False(good.IsBrokenSymbolicLink);

        Assert.True(broken.IsBrokenSymbolicLink);
        Assert.False(broken.IsDirectory);
    }

    // ---- Sorting ---------------------------------------------------------------------

    [Fact]
    public void Sort_PutsDirectoriesFirstByDefault()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddDirectory("/home/z-dir");

        Assert.Equal(["z-dir", "a.txt"], Names(Load(fs)));
    }

    [Fact]
    public void Sort_KeepsDirectoriesFirstWhenReversed()
    {
        // Reversal must apply to the ordering within each group, not to the grouping itself,
        // or reversing would hide the directories at the bottom of the listing.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/b.txt")
            .AddDirectory("/home/x-dir")
            .AddDirectory("/home/y-dir");

        DirectoryNode directory = Load(fs);
        directory.SortOrder = directory.SortOrder with { Reverse = true };

        Assert.Equal(["y-dir", "x-dir", "b.txt", "a.txt"], Names(directory));
    }

    [Fact]
    public void Sort_IsStableWithinTheDirectoryGrouping()
    {
        // This is the trap that a direct translation of ranger falls into: it relies on two
        // stable sorts in sequence, and List<T>.Sort is not stable. If the grouping disturbed
        // the name order, these names would come back scrambled.
        InMemoryFileSystem fs = new InMemoryFileSystem();
        foreach (string name in (string[])["c", "a", "e", "b", "d"])
        {
            fs.AddFile($"/home/{name}.txt");
            fs.AddDirectory($"/home/{name}-dir");
        }

        DirectoryNode directory = Load(fs);

        Assert.Equal(
            ["a-dir", "b-dir", "c-dir", "d-dir", "e-dir",
             "a.txt", "b.txt", "c.txt", "d.txt", "e.txt"],
            Names(directory));
    }

    [Fact]
    public void Sort_OrdersNumbersNaturallyByDefault()
    {
        // Lexicographic ordering would put track10 before track2, which is never what is wanted.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/track1.mp3")
            .AddFile("/home/track2.mp3")
            .AddFile("/home/track10.mp3")
            .AddFile("/home/track20.mp3");

        Assert.Equal(["track1.mp3", "track2.mp3", "track10.mp3", "track20.mp3"], Names(Load(fs)));
    }

    [Fact]
    public void Sort_OrdersByNameCharacterByCharacterWhenAskedTo()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/track1.mp3")
            .AddFile("/home/track10.mp3")
            .AddFile("/home/track2.mp3");

        DirectoryNode directory = Load(fs);
        directory.SortOrder = directory.SortOrder with { Key = SortKey.Basename };

        Assert.Equal(["track1.mp3", "track10.mp3", "track2.mp3"], Names(directory));
    }

    [Fact]
    public void Sort_IgnoresCaseByDefault()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/Beta")
            .AddFile("/home/alpha")
            .AddFile("/home/Gamma");

        Assert.Equal(["alpha", "Beta", "Gamma"], Names(Load(fs)));
    }

    [Fact]
    public void Sort_OrdersBySizeLargestFirst()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFileOfSize("/home/small", 10)
            .AddFileOfSize("/home/large", 1000)
            .AddFileOfSize("/home/medium", 500);

        DirectoryNode directory = Load(fs);
        directory.SortOrder = directory.SortOrder with { Key = SortKey.Size };

        Assert.Equal(["large", "medium", "small"], Names(directory));
    }

    [Fact]
    public void Sort_OrdersByModificationTimeNewestFirst()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/old", modified: now.AddDays(-10))
            .AddFile("/home/newest", modified: now)
            .AddFile("/home/middle", modified: now.AddDays(-5));

        DirectoryNode directory = Load(fs);
        directory.SortOrder = directory.SortOrder with { Key = SortKey.ModificationTime };

        Assert.Equal(["newest", "middle", "old"], Names(directory));
    }

    [Fact]
    public void Sort_OrdersByExtension()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/notes.txt")
            .AddFile("/home/photo.jpg")
            .AddFile("/home/archive.zip");

        DirectoryNode directory = Load(fs);
        directory.SortOrder = directory.SortOrder with { Key = SortKey.Extension };

        Assert.Equal(["photo.jpg", "notes.txt", "archive.zip"], Names(directory));
    }

    [Fact]
    public void Sort_FallsBackToTheNameWhenKeysAreEqual()
    {
        // Equal sizes must not leave the order at the mercy of whatever the scan returned.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFileOfSize("/home/c", 100)
            .AddFileOfSize("/home/a", 100)
            .AddFileOfSize("/home/b", 100);

        DirectoryNode directory = Load(fs);
        directory.SortOrder = directory.SortOrder with { Key = SortKey.Size };

        Assert.Equal(["a", "b", "c"], Names(directory));
    }

    // ---- Filtering -------------------------------------------------------------------

    [Fact]
    public void Filter_HidesDotfilesByDefault()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/visible.txt")
            .AddFile("/home/.hidden");

        DirectoryNode directory = Load(fs);

        Assert.Equal(["visible.txt"], Names(directory));
        // The entry is filtered out of the view, not lost.
        Assert.Equal(2, directory.AllEntries.Count);
    }

    [Fact]
    public void Filter_ShowsHiddenEntriesOnRequest()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/visible.txt")
            .AddFile("/home/.hidden");

        DirectoryNode directory = Load(fs);
        directory.ShowHidden = true;
        directory.Refilter();

        Assert.Equal([".hidden", "visible.txt"], Names(directory));
    }

    [Fact]
    public void Filter_HonoursACustomHiddenPattern()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/keep.txt")
            .AddFile("/home/build.pyc")
            .AddFile("/home/notes.bak");

        DirectoryNode directory = Load(fs);
        directory.HiddenPattern = @"\.(pyc|bak)$";
        directory.Refilter();

        Assert.Equal(["keep.txt"], Names(directory));
    }

    [Fact]
    public void Filter_AppliesTheStackAndTheHiddenSettingTogether()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/report.txt")
            .AddFile("/home/report.pdf")
            .AddFile("/home/.report.txt");

        DirectoryNode directory = Load(fs);
        directory.FilterStack.Push(new NameFilter(@"\.txt$"));
        directory.Refilter();

        Assert.Equal(["report.txt"], Names(directory));
    }

    [Fact]
    public void Filter_KeepsTheCursorOnTheSameEntryWhenItSurvives()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/b.log")
            .AddFile("/home/c.txt");

        DirectoryNode directory = Load(fs);
        directory.Cursor.MoveToNode(directory.Entries.Single(e => e.Basename == "c.txt"),
                                    directory.Entries);

        directory.FilterStack.Push(new NameFilter(@"\.txt$"));
        directory.Refilter();

        Assert.Equal("c.txt", directory.Selected?.Basename);
    }

    // ---- Marks -----------------------------------------------------------------------

    [Fact]
    public void Selection_IsTheCursorEntryWhenNothingIsMarked()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt").AddFile("/home/b.txt");

        DirectoryNode directory = Load(fs);
        directory.Cursor.MoveTo(1, directory.Entries);

        Assert.Equal(["b.txt"], directory.Selection.Select(e => e.Basename));
    }

    [Fact]
    public void Selection_IsTheMarkedEntriesOnceAnythingIsMarked()
    {
        // This one rule is why dd means "cut this" and "cut all of these" without two commands.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt").AddFile("/home/b.txt").AddFile("/home/c.txt");

        DirectoryNode directory = Load(fs);
        directory.Entries[0].IsMarked = true;
        directory.Entries[2].IsMarked = true;

        Assert.Equal(["a.txt", "c.txt"], directory.Selection.Select(e => e.Basename));
    }

    [Fact]
    public void Marks_SurviveAReload()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt").AddFile("/home/b.txt");

        DirectoryNode directory = Load(fs);
        directory.Entries[0].IsMarked = true;

        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt"], directory.MarkedEntries.Select(e => e.Basename));
    }

    [Fact]
    public void Marks_AreDroppedForEntriesThatDisappeared()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt").AddFile("/home/b.txt");

        DirectoryNode directory = Load(fs);
        directory.Entries[0].IsMarked = true;

        fs.Delete("/home/a.txt");
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Empty(directory.MarkedEntries);
    }

    [Fact]
    public void ToggleAllMarked_InvertsEveryVisibleEntry()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt").AddFile("/home/b.txt");

        DirectoryNode directory = Load(fs);
        directory.Entries[0].IsMarked = true;

        directory.ToggleAllMarked();

        Assert.Equal(["b.txt"], directory.MarkedEntries.Select(e => e.Basename));
    }

    // ---- Flat mode -------------------------------------------------------------------

    [Fact]
    public void Flat_FoldsSubdirectoriesIntoTheListing()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/top.txt")
            .AddFile("/home/sub/inner.txt")
            .AddFile("/home/sub/deeper/deep.txt");

        DirectoryNode directory = Load(fs);
        directory.SetFlatLevel(-1, TestContext.Current.CancellationToken);

        Assert.Contains("sub/inner.txt", Names(directory), StringComparer.Ordinal);
        Assert.Contains("sub/deeper/deep.txt", Names(directory), StringComparer.Ordinal);
        Assert.Contains("top.txt", Names(directory), StringComparer.Ordinal);
    }

    [Fact]
    public void Flat_StopsAtTheRequestedDepth()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/top.txt")
            .AddFile("/home/sub/inner.txt")
            .AddFile("/home/sub/deeper/deep.txt");

        DirectoryNode directory = Load(fs);
        directory.SetFlatLevel(1, TestContext.Current.CancellationToken);

        Assert.Contains("sub/inner.txt", Names(directory), StringComparer.Ordinal);
        Assert.DoesNotContain("sub/deeper/deep.txt", Names(directory), StringComparer.Ordinal);
    }

    [Fact]
    public void Flat_MatchesTheHiddenPatternAgainstEveryPathComponent()
    {
        // Only in flat mode does this matter, and without it a flattened listing fills up with
        // the contents of dot-directories.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/visible.txt")
            .AddFile("/home/.config/secret.txt");

        DirectoryNode directory = Load(fs);
        directory.SetFlatLevel(-1, TestContext.Current.CancellationToken);

        Assert.Equal(["visible.txt"], Names(directory));
    }

    [Fact]
    public void Flat_ReturnsToAPlainListingAtLevelZero()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/top.txt")
            .AddFile("/home/sub/inner.txt");

        DirectoryNode directory = Load(fs);
        directory.SetFlatLevel(-1, TestContext.Current.CancellationToken);
        directory.SetFlatLevel(0, TestContext.Current.CancellationToken);

        Assert.Equal(["sub", "top.txt"], Names(directory));
    }

    [Fact]
    public void Flat_SkipsUnreadableSubdirectoriesRatherThanFailing()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/top.txt")
            .AddFile("/home/open/fine.txt")
            .AddDirectory("/home/locked")
            .MakeInaccessible("/home/locked");

        DirectoryNode directory = Load(fs);
        directory.SetFlatLevel(-1, TestContext.Current.CancellationToken);

        Assert.Contains("open/fine.txt", Names(directory), StringComparer.Ordinal);
        Assert.Null(directory.LoadError);
    }

    [Fact]
    public void ANewlyLoadedDirectory_HidesDotfilesWithoutBeingToldTo()
    {
        // The default hidden_filter is applied by the field initialiser, not by the setter, so a
        // directory that nobody has configured must still hide dotfiles. Nothing covered this,
        // and caching the compiled pattern in the setter alone silently unhid every dotfile.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/visible.txt")
            .AddFile("/home/.hidden");

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(["visible.txt"], directory.Entries.Select(e => e.Basename));
    }

    [Fact]
    public void AHiddenFilterOfSeveralBranches_HidesEveryBranch()
    {
        // The real configuration's filter is a four-branch alternation; the compiled instance has
        // to behave exactly as the static call did.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/keep.txt")
            .AddFile("/home/.dotfile")
            .AddFile("/home/thing.pyc")
            .AddFile("/home/notes.bak");

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true))
        {
            HiddenPattern = @"^\.|\.(?:pyc|pyo|bak|swp)$",
        };

        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(["keep.txt"], directory.Entries.Select(e => e.Basename));
    }

    [Fact]
    public void AnEmptyHiddenFilter_HidesNothing()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/.hidden");

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true))
        {
            HiddenPattern = string.Empty,
        };

        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(2, directory.Count);
    }
}
