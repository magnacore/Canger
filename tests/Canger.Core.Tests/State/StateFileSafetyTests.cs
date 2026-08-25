// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// Smaller ways state and destinations were being written over.
/// </summary>
public sealed class StateFileSafetyTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-statesafe-" + Path.GetRandomFileName());

    public StateFileSafetyTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Tags_SavingThroughASymlinkKeepsTheLink()
    {
        // Keeping a state file as a link into a dotfiles repository is common; rename(2) replaces
        // the link rather than its target, so saving used to break it and later changes then
        // accumulated somewhere the repository never saw.
        string real = Path.Join(_root, "real-tagged");
        string link = Path.Join(_root, "tagged");
        File.WriteAllText(real, string.Empty);
        File.CreateSymbolicLink(link, real);

        Tags tags = new(link);
        tags.Toggle(["/home/me/one.txt"]);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Contains("/home/me/one.txt", File.ReadAllText(real), StringComparison.Ordinal);
    }

    [Fact]
    public void Tags_AreNotWrittenAtAllWhenNotPersistent()
    {
        // `--clean`. The old shape pointed the path at /dev/null and relied on the rename
        // failing, which it does not do as root.
        string path = Path.Join(_root, "tagged");

        Tags tags = new(path) { Persistent = false };
        tags.Toggle(["/home/me/one.txt"]);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SafePath_TreatsABrokenLinkAsOccupying()
    {
        // `Exists` follows links, so a dangling one reads as free — and the write that followed
        // landed wherever the link pointed, outside the directory being looked at.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/dest");
        fs.AddSymbolicLink("/dest/report.pdf", "/somewhere/that/does/not/exist");

        string chosen = SafePath.MakeUnique(fs, "/dest/report.pdf");

        Assert.NotEqual("/dest/report.pdf", chosen);
    }

    [Fact]
    public void SafePath_StillReturnsAFreeNameUnchanged()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/dest");

        Assert.Equal("/dest/report.pdf", SafePath.MakeUnique(fs, "/dest/report.pdf"));
    }
}
