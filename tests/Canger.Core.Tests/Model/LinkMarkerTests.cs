// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// The <c>-&gt;</c> that marks a listing row as a link.
/// </summary>
/// <remarks>
/// Ranger prefixes the info column — a file's size and a directory's entry count alike — with
/// <c>-&gt;</c> for any link (<c>container/fsobject.py:342</c>,
/// <c>container/directory.py:394</c>). Canger did it only for a directory whose size had been
/// measured with <c>dc</c>, which is the one case nobody starts from.
/// </remarks>
public class LinkMarkerTests
{
    private static (DirectoryCache Cache, InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/target/a.txt")
            .AddFile("/home/target/b.txt")
            .AddFile("/home/target/c.txt")
            .AddFile("/home/plain.txt", "hello world!")
            .AddSymbolicLink("/home/linked-dir", "/home/target")
            .AddSymbolicLink("/home/linked-file.txt", "/home/plain.txt");

        return (new DirectoryCache(fs), fs);
    }

    private static string SizeOf(string name)
    {
        (DirectoryCache cache, _) = Build();
        DirectoryNode home = cache.GetLoaded("/home");

        FsNode node = home.Entries.First(
            e => string.Equals(e.RelativePath, name, StringComparison.Ordinal));

        return LinemodeText.Size(node);
    }

    [Fact]
    public void ALinkedDirectoryIsMarked()
    {
        Assert.StartsWith("-> ", SizeOf("linked-dir"), StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkedFileIsMarkedToo()
    {
        // Ranger marks both; only the directory case was noticed first because that is what was
        // reported.
        Assert.StartsWith("-> ", SizeOf("linked-file.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void APlainFileIsNotMarked()
    {
        Assert.DoesNotContain("->", SizeOf("plain.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFigureItselfIsStillThere()
    {
        // The marker is a prefix, not a replacement: a linked directory still says how many
        // entries it holds.
        Assert.Equal("-> 3", SizeOf("linked-dir"));
    }
}
