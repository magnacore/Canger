// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Noticing a change made to an entry from outside Canger.
/// </summary>
/// <remarks>
/// <para>
/// A directory's mtime changes when an entry is added, removed or renamed, and that is all
/// <see cref="DirectoryNode.LoadIfOutdated"/> watches. A <c>chmod</c> in another terminal, a
/// <c>chown</c>, or a file being written to leave it alone entirely, so the permissions, owner,
/// size and modification time on screen stayed as first read — sometimes for a whole session.
/// Ranger has the same gap; this is a deliberate divergence.
/// </para>
/// <para>
/// Against the real filesystem, because the whole question is whether a second <c>stat</c> sees
/// what the first did not.
/// </para>
/// </remarks>
public sealed class RefreshMetadataTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-restat-" + Path.GetRandomFileName());

    private readonly DirectoryNode _directory;

    public RefreshMetadataTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Join(_root, "alpha.txt"), "one\n");
        File.WriteAllText(Path.Join(_root, "beta.txt"), "two\n");
        File.SetUnixFileMode(Path.Join(_root, "alpha.txt"),
                             UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // No throttling in the tests: the interval exists to bound cost on a network share, and
        // waiting a quarter of a second per assertion would only make the suite slower.
        DirectoryNode.MetadataRefreshInterval = TimeSpan.Zero;

        _directory = new DirectoryCache(LocalFileSystem.Instance).Get(_root);
        _directory.Load(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        DirectoryNode.MetadataRefreshInterval = TimeSpan.FromMilliseconds(250);

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FsNode Entry(string name) =>
        _directory.Entries.First(e => e.Basename == name);

    [Fact]
    public void SeesAPermissionChangeMadeFromOutside()
    {
        // The reported case. `chmod` does not touch the directory's mtime, so nothing else here
        // would ever notice it.
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                     Entry("alpha.txt").Status!.Value.Permissions);

        File.SetUnixFileMode(Path.Join(_root, "alpha.txt"),
                             UnixFileMode.UserRead | UnixFileMode.UserWrite |
                             UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        Assert.True(_directory.RefreshMetadata(0, _directory.Count));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite |
                     UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                     Entry("alpha.txt").Status!.Value.Permissions);
    }

    [Fact]
    public void SeesAFileGrowFromOutside()
    {
        // The same gap, and the one a user meets more often: writing to a file leaves the
        // directory's mtime alone too, so the size in the listing never moved.
        long before = Entry("beta.txt").Status!.Value.Size;

        File.AppendAllText(Path.Join(_root, "beta.txt"), "a good deal more text\n");

        Assert.True(_directory.RefreshMetadata(0, _directory.Count));
        Assert.True(Entry("beta.txt").Status!.Value.Size > before);
    }

    [Fact]
    public void SaysNothingChangedWhenNothingDid()
    {
        // Otherwise it would ask for a repaint on every pass. Reading a file bumps its access
        // time, which is why the comparison is field by field rather than the record's own.
        _ = File.ReadAllText(Path.Join(_root, "alpha.txt"));

        Assert.False(_directory.RefreshMetadata(0, _directory.Count));
    }

    [Fact]
    public void OnlyReadsTheRangeItIsGiven()
    {
        // What makes it affordable: the cost is the rows on screen, not the size of the listing.
        int alpha = _directory.Entries.ToList().FindIndex(e => e.Basename == "alpha.txt");
        int beta = _directory.Entries.ToList().FindIndex(e => e.Basename == "beta.txt");

        File.AppendAllText(Path.Join(_root, "beta.txt"), "more\n");
        long stale = Entry("beta.txt").Status!.Value.Size;

        Assert.False(_directory.RefreshMetadata(alpha, 1) && alpha != beta);
        Assert.Equal(stale, Entry("beta.txt").Status!.Value.Size);

        Assert.True(_directory.RefreshMetadata(beta, 1));
    }

    [Fact]
    public void LeavesMarksAndTheListingOrderAlone()
    {
        // The nodes are updated in place rather than replaced, and nothing is re-sorted: a file
        // growing while you watch it must not move under the cursor.
        IReadOnlyList<string> order = [.. _directory.Entries.Select(e => e.Basename)];
        Entry("beta.txt").IsMarked = true;

        File.AppendAllText(Path.Join(_root, "beta.txt"), "much more text than alpha has\n");
        _directory.RefreshMetadata(0, _directory.Count);

        Assert.Equal(order, _directory.Entries.Select(e => e.Basename));
        Assert.True(Entry("beta.txt").IsMarked);
    }

    [Fact]
    public void DoesNothingWhileTheListingIsFrozen()
    {
        // `freeze_files` means the listing is held exactly as it is; re-reading metadata behind
        // it would be the same surprise in a smaller form.
        DirectoryCache cache = new(LocalFileSystem.Instance) { Frozen = true };
        DirectoryNode frozen = cache.Get(_root);
        frozen.Load(TestContext.Current.CancellationToken);

        File.AppendAllText(Path.Join(_root, "beta.txt"), "written while frozen\n");

        Assert.False(frozen.RefreshMetadata(0, frozen.Count));
    }

    [Fact]
    public void HonoursTheRefreshInterval()
    {
        // The interval is what bounds the cost on a network share.
        DirectoryNode.MetadataRefreshInterval = TimeSpan.FromMinutes(1);
        _directory.RefreshMetadata(0, _directory.Count);

        File.AppendAllText(Path.Join(_root, "beta.txt"), "changed within the interval\n");

        Assert.False(_directory.RefreshMetadata(0, _directory.Count));
    }
}
