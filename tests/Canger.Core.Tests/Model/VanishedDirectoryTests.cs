// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Standing in a directory that another instance has deleted.
/// </summary>
/// <remarks>
/// Ranger enters anything that is not a directory as its parent
/// (<c>core/tab.py:151-153</c>), which covers a file — go there and select it — and equally a
/// path that has stopped being anything. Canger asked whether the path was a <em>file</em>, so a
/// path that had vanished failed the test and was entered anyway: <c>reset</c> re-entered a
/// directory that was not there and stayed in it.
/// </remarks>
public class VanishedDirectoryTests
{
    private static (Tab Tab, InMemoryFileSystem Fs, DirectoryCache Cache) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/manuj")
            .AddFile("/home/manuj/Test/notes.md", "hello")
            .AddFile("/home/manuj/keep.txt", "x");

        DirectoryCache cache = new(fs);

        return (new Tab(cache, "/home/manuj/Test"), fs, cache);
    }

    [Fact]
    public void EnteringAVanishedDirectoryLandsInItsParent()
    {
        (Tab tab, InMemoryFileSystem fs, DirectoryCache cache) = Build();
        fs.DeleteRecursive("/home/manuj/Test");

        // What `reset` does before re-entering, and the reason it is the command that recovers:
        // a cached listing still says the directory is there, so nothing is re-read until the
        // cache is dropped. Ranger stays put without a reset for the same reason.
        cache.Clear();

        tab.Enter("/home/manuj/Test", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/home/manuj", tab.Path);
    }

    [Fact]
    public void AWholeDeletedTreeIsWalkedUpUntilSomethingIsThere()
    {
        // Ranger tries the immediate parent and no further, so a deleted tree leaves it where it
        // was. Deleting a directory usually means deleting what contained it too.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/manuj")
            .AddFile("/home/manuj/a/b/c/notes.md", "x");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/manuj/a/b/c");
        fs.DeleteRecursive("/home/manuj/a");
        cache.Clear();

        tab.Enter("/home/manuj/a/b/c", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/home/manuj", tab.Path);
    }

    [Fact]
    public void AFileIsStillEnteredAsItsParentWithTheCursorOnIt()
    {
        // The behaviour this shares its one test with, which must not change: --selectfile and
        // jumping to a search result both rely on it.
        (Tab tab, _, _) = Build();

        tab.Enter("/home/manuj/keep.txt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/home/manuj", tab.Path);
        Assert.Equal("/home/manuj/keep.txt", tab.Selected?.Path);
    }

    [Fact]
    public void ADirectoryThatIsStillThereIsEnteredNormally()
    {
        (Tab tab, _, _) = Build();

        tab.Enter("/home/manuj/Test", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/home/manuj/Test", tab.Path);
    }
}
