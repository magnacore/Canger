// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

/// <summary>
/// The resolved path, which is what a tag is recorded against.
/// </summary>
/// <remarks>
/// Ranger tags the target of a link rather than the link (<c>core/actions.py:880</c>), so tagging
/// a file through a symbolic link and tagging it by its own name are the same act. Getting this
/// wrong is invisible until someone tags a link and finds the marker missing on the real file.
/// </remarks>
public sealed class FsNodeRealPathTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-realpath-" + Path.GetRandomFileName());

    private readonly LocalFileSystem _fs = new();

    public FsNodeRealPathTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string At(string name) => Path.Join(_root, name);

    private FsNode Node(string name) =>
        new FileNode(_fs, At(name),
                     _fs.GetStatus(At(name), followSymbolicLinks: true),
                     _fs.GetStatus(At(name), followSymbolicLinks: false));

    [Fact]
    public void RealPath_IsThePathItselfForAnOrdinaryFile()
    {
        File.WriteAllText(At("plain.txt"), "x");

        Assert.Equal(At("plain.txt"), Node("plain.txt").RealPath);
    }

    [Fact]
    public void RealPath_FollowsASymbolicLinkToItsTarget()
    {
        File.WriteAllText(At("target.txt"), "x");
        File.CreateSymbolicLink(At("link.txt"), At("target.txt"));

        Assert.Equal(At("target.txt"), Node("link.txt").RealPath);
    }

    [Fact]
    public void RealPath_FollowsAWholeChainOfLinks()
    {
        File.WriteAllText(At("end.txt"), "x");
        File.CreateSymbolicLink(At("middle.txt"), At("end.txt"));
        File.CreateSymbolicLink(At("start.txt"), At("middle.txt"));

        Assert.Equal(At("end.txt"), Node("start.txt").RealPath);
    }

    [Fact]
    public void RealPath_FollowsALinkToADirectory()
    {
        Directory.CreateDirectory(At("real-dir"));
        Directory.CreateSymbolicLink(At("dir-link"), At("real-dir"));

        Assert.Equal(At("real-dir"), Node("dir-link").RealPath);
    }

    [Fact]
    public void RealPath_ResolvesABrokenLinkToWhereItPoints()
    {
        // realpath(3) does not require the target to exist, and neither does the Python
        // os.path.realpath ranger calls: both name the missing target rather than the link. So
        // tagging a broken link records the path it would have had, and the tag reappears if
        // whatever it pointed at comes back.
        File.CreateSymbolicLink(At("broken"), At("nowhere"));

        Assert.Equal(At("nowhere"), Node("broken").RealPath);
    }
}
