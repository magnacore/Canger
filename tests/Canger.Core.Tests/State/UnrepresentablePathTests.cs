// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// Paths the line-per-entry formats cannot hold.
/// </summary>
/// <remarks>
/// Both files are ranger's formats, deliberately, so they can be shared between the two programs
/// — one entry per line. A path containing a newline cannot be represented: a tag written out
/// becomes two lines and comes back as two tags on two paths that do not exist, and the next save
/// makes that permanent; a bookmark comes back truncated at the break and points somewhere else.
///
/// Refused rather than escaped, which would fix the round trip and make the file unreadable to
/// ranger — a poor trade for a case this rare. What is lost is the tag, not the file.
/// </remarks>
public sealed class UnrepresentablePathTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-newline-" + Path.GetRandomFileName());

    public UnrepresentablePathTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Tags_RefuseAPathWithANewline()
    {
        string path = Path.Join(_root, "tagged");
        Tags tags = new(path);

        tags.Add(["/home/me/two\nlines.txt"]);

        Assert.False(tags.Contains("/home/me/two\nlines.txt"));
    }

    [Fact]
    public void Tags_RefusingOneDoesNotCorruptTheFile()
    {
        // The damage was not the refusal but the round trip: two bogus entries where one real
        // one should have been, made permanent by the next save.
        string path = Path.Join(_root, "tagged");
        Tags tags = new(path);

        tags.Add(["/home/me/ordinary.txt"]);
        tags.Add(["/home/me/two\nlines.txt"]);

        string[] lines = File.ReadAllLines(path);

        Assert.Equal(["/home/me/ordinary.txt"], lines);
    }

    [Fact]
    public void Tags_StillAcceptEverythingElseAwkward()
    {
        // Spaces, quotes, colons and per cents all survive the format; only the line break does
        // not, and the refusal must be that narrow.
        string path = Path.Join(_root, "tagged");
        Tags tags = new(path);

        tags.Add(["/home/me/it's a 50% file: really.txt"]);

        Assert.True(tags.Contains("/home/me/it's a 50% file: really.txt"));
    }

    [Fact]
    public void Bookmarks_RefuseAPathWithANewline()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home/me");
        Bookmarks bookmarks = new(fs, Path.Join(_root, "bookmarks"));

        bookmarks.Set('x', "/home/me/two\nlines");

        Assert.False(bookmarks.Entries.ContainsKey('x'));
    }

    [Fact]
    public void Bookmarks_StillAcceptAnOrdinaryPath()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home/me");
        Bookmarks bookmarks = new(fs, Path.Join(_root, "bookmarks"));

        bookmarks.Set('x', "/home/me/somewhere ordinary");

        Assert.Equal("/home/me/somewhere ordinary", bookmarks.Entries['x']);
    }
}
