// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// Refusing to copy a file onto itself.
/// </summary>
/// <remarks>
/// <para>
/// Ranger refuses outright — <c>copyfile</c> raises <c>"`x` and `y` are the same file"</c>
/// (<c>ext/shutil_generatorized.py:133-136</c>) — and Canger had no equivalent. `po` on a file in
/// its own directory resolves the destination to the source's own path, and the copy engine opens
/// the destination truncating.
/// </para>
/// <para>
/// Measured before the guard was added: nothing was actually destroyed. .NET takes an
/// inode-scoped advisory lock, so opening the same file for reading and for truncating writing
/// collides and the copy fails. But that is an implementation detail rather than a decision, it
/// goes away if <c>System.IO.DisableFileLocking</c> is set, and the error it produced — "used by
/// another process" — said nothing true. These tests pin the refusal and its wording, so the
/// safety is Canger's rather than the runtime's.
/// </para>
/// <para>
/// Against the real filesystem, because the whole point is device and inode numbers.
/// </para>
/// </remarks>
public sealed class SameFileTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-same-" + Path.GetRandomFileName());

    private readonly CopyEngine _engine = new(LocalFileSystem.Instance);

    public SameFileTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Join(_root, "real.txt"), "contents that must survive");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Real => Path.Join(_root, "real.txt");

    private void AssertRefusedAndIntact(FileCopyResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Contains("same file", result.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("contents that must survive", File.ReadAllText(Real));
    }

    [Fact]
    public void CopyingAFileOntoItsOwnPathIsRefused()
    {
        AssertRefusedAndIntact(
            _engine.CopyFile(Real, Real, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CopyingOntoAHardLinkToTheSourceIsRefused()
    {
        // Two names, one inode — so the paths differ and only the inode gives it away. Hard links
        // are ordinary: git object stores, package caches, rsync --link-dest snapshot trees.
        string link = Path.Join(_root, "hardlink.txt");
        LocalFileSystem.Instance.CreateHardLink(link, Real);

        AssertRefusedAndIntact(
            _engine.CopyFile(Real, link, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CopyingOntoASymlinkToTheSourceIsRefused()
    {
        // FileMode.Create follows the link, so without the guard the write lands on the source.
        string link = Path.Join(_root, "symlink.txt");
        File.CreateSymbolicLink(link, Real);

        AssertRefusedAndIntact(
            _engine.CopyFile(Real, link, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AnOrdinaryCopyIsStillAllowed()
    {
        // The guard must not refuse the thing it is guarding.
        string destination = Path.Join(_root, "copy.txt");

        FileCopyResult result =
            _engine.CopyFile(Real, destination, null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("contents that must survive", File.ReadAllText(destination));
    }

    [Fact]
    public void CopyingOverAnUnrelatedExistingFileIsStillAllowed()
    {
        // Overwriting a different file is what `po` is for; only identity is refused.
        string destination = Path.Join(_root, "other.txt");
        File.WriteAllText(destination, "will be replaced");

        FileCopyResult result =
            _engine.CopyFile(Real, destination, null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("contents that must survive", File.ReadAllText(destination));
    }

    [Fact]
    public void CopyingASymlinkOntoItselfLeavesItAlone()
    {
        // A symbolic link source is recreated rather than read, so it never reaches the guard.
        // Pinned because it is the one path that bypasses it.
        string link = Path.Join(_root, "link.txt");
        File.CreateSymbolicLink(link, Real);

        FileCopyResult result =
            _engine.CopyFile(link, link, null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(Real, new FileInfo(link).LinkTarget);
        Assert.Equal("contents that must survive", File.ReadAllText(Real));
    }
}
