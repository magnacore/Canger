// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Holding a listing still while what it shows is changing underneath.
/// </summary>
/// <remarks>
/// The <c>freeze_files</c> setting, which did nothing. It is for reading a directory that is
/// being written to without the rows moving under the cursor. Ranger tests it at the top of
/// <c>load_content</c> (<c>container/directory.py:500</c>).
/// </remarks>
public class FreezeFilesTests
{
    private static (DirectoryCache Cache, InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/a.txt")
            .AddFile("/home/b.txt");

        return (new DirectoryCache(fs), fs);
    }

    [Fact]
    public void Frozen_HoldsTheListingAcrossAReload()
    {
        (DirectoryCache cache, InMemoryFileSystem fs) = Build();
        DirectoryNode directory = cache.GetLoaded("/home", TestContext.Current.CancellationToken);
        Assert.Equal(2, directory.Count);

        cache.Frozen = true;
        fs.AddFile("/home/c.txt");
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public void Thawed_PicksUpWhatChanged()
    {
        (DirectoryCache cache, InMemoryFileSystem fs) = Build();
        DirectoryNode directory = cache.GetLoaded("/home", TestContext.Current.CancellationToken);

        fs.AddFile("/home/c.txt");
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(3, directory.Count);
    }

    [Fact]
    public void Frozen_StillLoadsADirectoryForTheFirstTime()
    {
        // Freezing a screen that has never been drawn would show nothing at all rather than
        // holding what is there, which is not what the setting is for.
        (DirectoryCache cache, _) = Build();
        cache.Frozen = true;

        DirectoryNode directory = cache.GetLoaded("/home", TestContext.Current.CancellationToken);

        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public void Thawing_LetsTheNextReloadThrough()
    {
        (DirectoryCache cache, InMemoryFileSystem fs) = Build();
        DirectoryNode directory = cache.GetLoaded("/home", TestContext.Current.CancellationToken);

        cache.Frozen = true;
        fs.AddFile("/home/c.txt");
        directory.Load(TestContext.Current.CancellationToken);

        cache.Frozen = false;
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(3, directory.Count);
    }
}
