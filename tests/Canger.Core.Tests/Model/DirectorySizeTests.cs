// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Measuring a tree, which is what <c>dc</c> asks for.
/// </summary>
public sealed class DirectorySizeTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-dirsize-" + Path.GetRandomFileName());

    private readonly LocalFileSystem _fs = new();

    public DirectorySizeTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relative, int bytes)
    {
        string path = Path.Join(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    [Fact]
    public void Measure_AddsUpEverythingBeneath()
    {
        Write("a.bin", 1000);
        Write("sub/b.bin", 2000);
        Write("sub/deeper/c.bin", 4000);

        Assert.Equal(7000, DirectorySize.Measure(_fs, _root, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Measure_IsZeroForAnEmptyTree()
    {
        Directory.CreateDirectory(Path.Join(_root, "empty"));

        Assert.Equal(0, DirectorySize.Measure(_fs, Path.Join(_root, "empty"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Measure_DoesNotDescendIntoALinkedDirectory()
    {
        // A link pointing at an ancestor would otherwise make the walk run forever. Ranger's
        // os.walk does not follow them either.
        Write("real/big.bin", 5000);
        Directory.CreateSymbolicLink(Path.Join(_root, "loop"), _root);

        Assert.Equal(5000, DirectorySize.Measure(_fs, _root, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Measure_CountsALinkedFileAsItsTarget()
    {
        // Ranger stats through the link, so the bytes counted are the target's.
        Write("target.bin", 300);
        File.CreateSymbolicLink(Path.Join(_root, "alias.bin"), Path.Join(_root, "target.bin"));

        Assert.Equal(600, DirectorySize.Measure(_fs, _root, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Measure_IgnoresABrokenLink()
    {
        Write("real.bin", 700);
        File.CreateSymbolicLink(Path.Join(_root, "broken"), Path.Join(_root, "gone"));

        Assert.Equal(700, DirectorySize.Measure(_fs, _root, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Measure_TreatsAnUnreadableSubdirectoryAsEmpty()
    {
        // Better than abandoning the whole measurement over one directory.
        Write("readable/a.bin", 800);
        string closed = Path.Join(_root, "closed");
        Directory.CreateDirectory(closed);
        File.WriteAllBytes(Path.Join(closed, "hidden.bin"), new byte[100]);
        File.SetUnixFileMode(closed, UnixFileMode.None);

        try
        {
            Assert.Equal(800, DirectorySize.Measure(_fs, _root, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.SetUnixFileMode(closed, UnixFileMode.UserRead | UnixFileMode.UserWrite
                                         | UnixFileMode.UserExecute);
        }
    }
}
