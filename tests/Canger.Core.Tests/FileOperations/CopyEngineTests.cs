// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// The copy engine against a real filesystem.
/// </summary>
/// <remarks>
/// These have to touch the disk: the whole point is which kernel facility the filesystem accepts,
/// and an in-memory double cannot answer that. The reflink tests are skipped where the filesystem
/// does not support one.
/// </remarks>
public sealed class CopyEngineTests : IDisposable
{
    private readonly string _root;
    private readonly CopyEngine _engine = new(LocalFileSystem.Instance);

    public CopyEngineTests()
    {
        _root = Path.Join(ScratchRoot(), "canger-copy-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Where the test tree is built.
    /// </summary>
    /// <remarks>
    /// Deliberately not the system temporary directory, which is very often a tmpfs and cannot
    /// reflink. Working beside the repository instead means the copy-on-write path is exercised
    /// on any machine whose checkout lives on a filesystem that supports it, rather than being
    /// skipped everywhere.
    /// </remarks>
    private static string ScratchRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")))
            {
                string scratch = Path.Join(directory.FullName, "artifacts", "test-scratch");
                Directory.CreateDirectory(scratch);
                return scratch;
            }

            directory = directory.Parent;
        }

        return Path.GetTempPath();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    private string At(string name) => Path.Join(_root, name);

    private string WriteFile(string name, int sizeBytes)
    {
        string path = At(name);
        byte[] content = new byte[sizeBytes];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void CopyFile_ReproducesTheContentExactly()
    {
        string source = WriteFile("source.bin", 200_000);
        string destination = At("copy.bin");

        FileCopyResult result = _engine.CopyFile(source, destination, null,
                                                 TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
    }

    [Fact]
    public void CopyFile_HandlesAnEmptyFile()
    {
        string source = WriteFile("empty.bin", 0);
        string destination = At("empty-copy.bin");

        Assert.True(_engine.CopyFile(source, destination, null,
                                         TestContext.Current.CancellationToken).Succeeded);
        Assert.Equal(0, new FileInfo(destination).Length);
    }

    [Fact]
    public void CopyFile_UsesAReflinkWhereTheFilesystemSupportsOne()
    {
        // On a copy-on-write filesystem the data is shared rather than duplicated, so the copy
        // is instant whatever the file's size. This is the fast path Canger is built around.
        string source = WriteFile("large.bin", 4_000_000);

        FileCopyResult result = _engine.CopyFile(source, At("clone.bin"), null, TestContext.Current.CancellationToken);

        Assert.SkipWhen(result.Strategy != CopyStrategy.Reflink,
                        "this filesystem does not support reflinks");

        Assert.Equal(0, result.TransferredBytes);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(At("clone.bin")));
    }

    [Fact]
    public void CopyFile_FallsBackWhenReflinkingIsNotAllowed()
    {
        string source = WriteFile("source.bin", 100_000);
        CopyEngine engine = new(LocalFileSystem.Instance) { AllowReflink = false };

        FileCopyResult result = engine.CopyFile(source, At("copy.bin"), null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotEqual(CopyStrategy.Reflink, result.Strategy);
        Assert.Equal(100_000, result.TransferredBytes);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(At("copy.bin")));
    }

    [Fact]
    public void CopyFile_FallsBackAllTheWayToCopyingByHand()
    {
        string source = WriteFile("source.bin", 100_000);
        CopyEngine engine = new(LocalFileSystem.Instance)
        {
            AllowReflink = false,
            AllowKernelCopy = false,
        };

        FileCopyResult result = engine.CopyFile(source, At("copy.bin"), null, TestContext.Current.CancellationToken);

        Assert.Equal(CopyStrategy.Buffered, result.Strategy);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(At("copy.bin")));
    }

    [Fact]
    public void CopyFile_ReportsProgressAsDataMoves()
    {
        string source = WriteFile("source.bin", 100_000);
        CopyEngine engine = new(LocalFileSystem.Instance) { AllowReflink = false };

        long reported = 0;
        engine.CopyFile(source, At("copy.bin"), bytes => reported += bytes,
                          TestContext.Current.CancellationToken);

        Assert.Equal(100_000, reported);
    }

    [Fact]
    public void CopyFile_ReportsNoProgressForAReflinkBecauseNothingMoves()
    {
        // This is exactly why the time estimate has to ignore reflinked bytes.
        string source = WriteFile("large.bin", 4_000_000);

        long reported = 0;
        FileCopyResult result = _engine.CopyFile(source, At("clone.bin"), bytes => reported += bytes,
                                                TestContext.Current.CancellationToken);

        Assert.SkipWhen(result.Strategy != CopyStrategy.Reflink,
                        "this filesystem does not support reflinks");

        Assert.Equal(0, reported);
    }

    [Fact]
    public void CopyFile_RecreatesASymbolicLinkRatherThanFollowingIt()
    {
        // Copying a tree of links must not quietly turn them into copies of their targets.
        File.WriteAllText(At("target.txt"), "content");
        File.CreateSymbolicLink(At("link.txt"), At("target.txt"));

        FileCopyResult result = _engine.CopyFile(At("link.txt"), At("copied-link.txt"), null, TestContext.Current.CancellationToken);

        Assert.Equal(CopyStrategy.Symlink, result.Strategy);
        Assert.NotNull(new FileInfo(At("copied-link.txt")).LinkTarget);
    }

    [Fact]
    public void CopyFile_OverwritesWhatIsAlreadyThere()
    {
        string source = WriteFile("source.bin", 5_000);
        File.WriteAllText(At("copy.bin"), "this should be replaced entirely");

        _engine.CopyFile(source, At("copy.bin"), null, TestContext.Current.CancellationToken);

        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(At("copy.bin")));
    }

    [Fact]
    public void CopyFile_AcceptsARelativeDestination()
    {
        // A relative path has no directory component, and the same-filesystem check asked for
        // the status of an empty string. Absolute paths hid this entirely.
        string source = WriteFile("source.bin", 1_000);
        string previous = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(_root);

            FileCopyResult result = _engine.CopyFile(source, "relative-copy.bin", null,
                                                     TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(At("relative-copy.bin")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void CopyFile_ReportsAMissingSourceRatherThanThrowing()
    {
        FileCopyResult result = _engine.CopyFile(At("nothing-here"), At("copy.bin"), null, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CopyFile_CanBeCancelled()
    {
        string source = WriteFile("large.bin", 4_000_000);
        CopyEngine engine = new(LocalFileSystem.Instance)
        {
            AllowReflink = false,
            AllowKernelCopy = false,
        };

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => engine.CopyFile(source, At("copy.bin"), null, cancellation.Token));
    }
}
