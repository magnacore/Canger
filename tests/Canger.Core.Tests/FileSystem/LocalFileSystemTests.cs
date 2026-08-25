// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileSystem;

/// <summary>
/// Exercises <see cref="LocalFileSystem"/> against a real temporary tree.
/// </summary>
/// <remarks>
/// These are integration tests by necessity: the whole point of the type is that it reports what
/// the kernel reports, so a mocked filesystem would test nothing. Everything above this layer is
/// tested against <see cref="IFileSystem"/> instead and needs no disk.
/// </remarks>
public sealed class LocalFileSystemTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileSystem _fs = LocalFileSystem.Instance;

    public LocalFileSystemTests()
    {
        _root = Path.Join(Path.GetTempPath(), "canger-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone; nothing to clean up.
        }
    }

    private string Path_(string relative) => Path.Join(_root, relative);

    [Fact]
    public void GetStatus_ReportsSizeAndKindOfARegularFile()
    {
        File.WriteAllText(Path_("hello.txt"), "0123456789");

        FileStatus status = Assert.NotNull(_fs.GetStatus(Path_("hello.txt")));

        Assert.Equal(FileKind.Regular, status.Kind);
        Assert.Equal(10, status.Size);
        Assert.Equal(1u, status.HardLinkCount);
        Assert.NotEqual(0ul, status.Inode);
    }

    [Fact]
    public void GetStatus_ReportsMissingPathsAsNull() =>
        Assert.Null(_fs.GetStatus(Path_("does-not-exist")));

    [Fact]
    public void GetStatus_DistinguishesLinkFromTarget()
    {
        File.WriteAllText(Path_("target.txt"), "content");
        File.CreateSymbolicLink(Path_("link.txt"), Path_("target.txt"));

        FileStatus link = Assert.NotNull(_fs.GetStatus(Path_("link.txt"), followSymbolicLinks: false));
        FileStatus target = Assert.NotNull(_fs.GetStatus(Path_("link.txt"), followSymbolicLinks: true));

        Assert.Equal(FileKind.SymbolicLink, link.Kind);
        Assert.Equal(FileKind.Regular, target.Kind);
        Assert.Equal(7, target.Size);
    }

    [Fact]
    public void ListDirectory_PreloadsLinkAndTargetStatus()
    {
        File.WriteAllText(Path_("plain"), "x");
        Directory.CreateDirectory(Path_("subdir"));
        File.CreateSymbolicLink(Path_("good-link"), Path_("subdir"));
        File.CreateSymbolicLink(Path_("broken-link"), Path_("nowhere"));

        Dictionary<string, DirectoryEntry> entries =
            _fs.ListDirectory(_root, TestContext.Current.CancellationToken).ToDictionary(e => e.Name, StringComparer.Ordinal);

        Assert.Equal(4, entries.Count);

        Assert.Equal(FileKind.Regular, entries["plain"].LinkStatus.Kind);
        Assert.Null(entries["plain"].TargetStatus);

        Assert.True(entries["subdir"].IsDirectory);

        // A link to a directory reports as a link itself but behaves as a directory.
        Assert.True(entries["good-link"].IsSymbolicLink);
        Assert.False(entries["good-link"].IsBrokenSymbolicLink);
        Assert.True(entries["good-link"].IsDirectory);

        // A dangling link has no target status at all.
        Assert.True(entries["broken-link"].IsBrokenSymbolicLink);
        Assert.Null(entries["broken-link"].TargetStatus);
        Assert.False(entries["broken-link"].IsDirectory);
    }

    [Fact]
    public void ListDirectory_ReturnsEmptyForAnEmptyDirectory() =>
        Assert.Empty(_fs.ListDirectory(_root, TestContext.Current.CancellationToken));

    [Fact]
    public void ReadFilePrefix_StopsAtTheRequestedLength()
    {
        File.WriteAllText(Path_("data"), "abcdefghij");

        Assert.Equal("abcd"u8.ToArray(), _fs.ReadFilePrefix(Path_("data"), 4));
        // A short file yields only what is there, not a zero-padded buffer.
        Assert.Equal("abcdefghij"u8.ToArray(), _fs.ReadFilePrefix(Path_("data"), 100));
        Assert.Empty(_fs.ReadFilePrefix(Path_("missing"), 4));
    }

    [Fact]
    public void CreateHardLink_SharesTheInodeAndRaisesTheLinkCount()
    {
        File.WriteAllText(Path_("original"), "shared");
        _fs.CreateHardLink(Path_("hardlink"), Path_("original"));

        FileStatus original = Assert.NotNull(_fs.GetStatus(Path_("original")));
        FileStatus link = Assert.NotNull(_fs.GetStatus(Path_("hardlink")));

        Assert.Equal(original.Inode, link.Inode);
        Assert.Equal(2u, original.HardLinkCount);
    }

    [Fact]
    public void Delete_RemovesASymlinkToADirectoryWithoutTouchingTheTarget()
    {
        Directory.CreateDirectory(Path_("real"));
        File.WriteAllText(Path_("real/keep.txt"), "keep me");
        File.CreateSymbolicLink(Path_("alias"), Path_("real"));

        _fs.Delete(Path_("alias"));

        Assert.False(_fs.Exists(Path_("alias")));
        Assert.True(File.Exists(Path_("real/keep.txt")));
    }

    [Fact]
    public void Touch_CreatesThenUpdatesWithoutTruncating()
    {
        _fs.Touch(Path_("fresh"));
        Assert.True(File.Exists(Path_("fresh")));

        File.WriteAllText(Path_("fresh"), "content");
        _fs.Touch(Path_("fresh"));

        Assert.Equal("content", File.ReadAllText(Path_("fresh")));
    }

    [Fact]
    public void GetDiskUsage_ReportsPlausibleTotals()
    {
        (long free, long total) = Assert.NotNull(_fs.GetDiskUsage(_root));

        Assert.True(total > 0, "total space should be positive");
        Assert.InRange(free, 0, total);
    }

    [Fact]
    public void ResolvePath_FollowsSymbolicLinks()
    {
        Directory.CreateDirectory(Path_("real"));
        File.CreateSymbolicLink(Path_("alias"), Path_("real"));

        // The temp directory itself may sit behind a symlink, so compare resolved against
        // resolved rather than against the literal path we built.
        Assert.Equal(_fs.ResolvePath(Path_("real")), _fs.ResolvePath(Path_("alias")));
    }
}
