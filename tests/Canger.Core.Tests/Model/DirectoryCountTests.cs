// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Model.Filters;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// The number shown beside a directory: how many things are in it.
/// </summary>
/// <remarks>
/// It has to be the same number however the listing is being viewed. It was not: unvisited, a
/// directory reported what the shallow count found, which is every name on disk; once loaded it
/// reported the filtered listing instead. A directory of two hidden files read 2 until you looked
/// inside it and 0 afterwards. Ranger takes `len(filelist)` straight from `os.listdir`
/// (`container/directory.py:391`) and never filters it.
/// </remarks>
public class DirectoryCountTests
{
    private static DirectoryNode Load(InMemoryFileSystem fs, string path = "/home")
    {
        DirectoryNode directory =
            new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);
        return directory;
    }

    [Fact]
    public void HiddenEntriesAreCounted()
    {
        // The reported case: a directory whose contents are all hidden is not an empty directory.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/.hidden1")
            .AddFile("/home/.hidden2");

        DirectoryNode home = Load(fs);
        home.ShowHidden = false;

        Assert.Empty(home.Entries);
        Assert.Equal(2L, home.Size);
    }

    [Fact]
    public void TheCountIsTheSameWhetherHiddenEntriesAreShown()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/visible.txt")
            .AddFile("/home/.hidden");

        DirectoryNode home = Load(fs);

        home.ShowHidden = true;
        long shown = home.Size ?? -1;

        home.ShowHidden = false;
        long concealed = home.Size ?? -1;

        Assert.Equal(2L, shown);
        Assert.Equal(shown, concealed);
    }

    [Fact]
    public void AFilterDoesNotChangeTheCount()
    {
        // `zf` narrows what is displayed; it does not remove anything from the directory, and the
        // count answers "how much is in here", not "how much am I looking at".
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.log");

        DirectoryNode home = Load(fs);
        home.PreviewFilter = new NameFilter("alpha");
        home.Refilter();

        Assert.Single(home.Entries);
        Assert.Equal(3L, home.Size);
    }

    [Fact]
    public void LoadingDoesNotChangeTheCount()
    {
        // The heart of it: the number a directory reports must not depend on whether anyone has
        // looked inside it.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFile("/home/sub/.hidden1")
            .AddFile("/home/sub/.hidden2");

        DirectoryCache cache = new(fs);
        DirectoryNode sub = cache.Get("/home/sub");

        // What an unvisited directory reports: the shallow count, which counts every name.
        sub.EnsureCounted();
        long beforeLoading = sub.Size ?? -1;

        sub.Load(TestContext.Current.CancellationToken);
        sub.ShowHidden = false;

        Assert.Equal(2L, beforeLoading);
        Assert.Equal(beforeLoading, sub.Size);
    }

    [Fact]
    public void TheColumnShowsTheSameNumberOnceTheDirectoryIsLoaded()
    {
        // The column reads its own way to the count, so fixing `Size` alone fixed nothing the
        // user could see: `LinemodeText.Size` asked for `Count`, which is how many rows the
        // listing shows. Two places, one rule.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFile("/home/sub/.hidden1")
            .AddFile("/home/sub/.hidden2");

        DirectoryCache cache = new(fs);
        DirectoryNode sub = cache.Get("/home/sub");

        sub.EnsureCounted();
        string unvisited = LinemodeText.Size(sub);

        sub.Load(TestContext.Current.CancellationToken);
        sub.ShowHidden = false;

        Assert.Equal("2", unvisited);
        Assert.Equal(unvisited, LinemodeText.Size(sub));
    }

    [Fact]
    public void TheColumnStillCountsOnlyWhenAskedForAnUnvisitedDirectory()
    {
        // `automatically_count_files` off means no stat per visible row, which is the point of
        // the setting on a slow filesystem. That must survive the change above.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFile("/home/sub/a.txt");

        DirectoryNode sub = new DirectoryCache(fs).Get("/home/sub");

        Assert.Equal(string.Empty, LinemodeText.Size(sub, countFiles: false));
    }
}
