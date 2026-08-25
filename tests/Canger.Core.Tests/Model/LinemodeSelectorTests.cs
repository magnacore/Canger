// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.Model;
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Which linemode an entry ends up drawn with.
/// </summary>
public class LinemodeSelectorTests
{
    private static readonly LinemodeRegistry Registry = new();

    /// <summary>Builds a loaded directory over an in-memory tree.</summary>
    private static FsNode Entry(InMemoryFileSystem fs, string name)
    {
        DirectoryNode directory = new(fs, "/home",
                                      fs.GetStatus("/home", followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);
        return directory.Entries.Single(e => e.RelativePath == name);
    }

    private static LinemodeRule Always(string mode) =>
        new(LinemodeScope.Always, null, null, mode);

    private static LinemodeRule ForPath(string pattern, string mode) =>
        new(LinemodeScope.Path, new Regex(pattern), null, mode);

    [Fact]
    public void Resolve_UsesTheDefaultWhenNothingSaysOtherwise()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        LinemodeSelector selector = new(new LinemodeRegistry());

        Assert.Equal("filename", selector.Resolve(Entry(fs, "a.txt")).Mode.Name);
    }

    [Fact]
    public void Resolve_PrefersTheRuleAddedLast()
    {
        // A general rule followed by a narrow one has to read the way it looks, so later wins.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        LinemodeSelector selector = new(new LinemodeRegistry());

        selector.Add(Always("mtime"));
        selector.Add(Always("permissions"));

        Assert.Equal("permissions", selector.Resolve(Entry(fs, "a.txt")).Mode.Name);
    }

    [Fact]
    public void Resolve_SkipsARuleWhosePathPatternDoesNotMatch()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/notes.txt")
            .AddFile("/home/song.mp3");

        LinemodeSelector selector = new(new LinemodeRegistry());
        selector.Add(Always("mtime"));
        selector.Add(ForPath(@"\.mp3$", "permissions"));

        Assert.Equal("permissions", selector.Resolve(Entry(fs, "song.mp3")).Mode.Name);
        Assert.Equal("mtime", selector.Resolve(Entry(fs, "notes.txt")).Mode.Name);
    }

    [Fact]
    public void Resolve_IgnoresARuleNamingAModeThatDoesNotExist()
    {
        // A typo in the configuration should cost that one line, not the whole listing.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        LinemodeSelector selector = new(new LinemodeRegistry());

        selector.Add(Always("mtime"));
        selector.Add(Always("no-such-mode"));

        Assert.Equal("mtime", selector.Resolve(Entry(fs, "a.txt")).Mode.Name);
    }

    [Fact]
    public void Resolve_LetsATagRuleApplyOnlyToTaggedEntries()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/tagged.txt")
            .AddFile("/home/plain.txt");

        Tags tags = new("/dev/null");
        tags.Add(["/home/tagged.txt"], 'p');

        LinemodeSelector selector = new(new LinemodeRegistry(), tags);
        selector.Add(new LinemodeRule(LinemodeScope.Tag, null, "pq", "permissions"));

        Assert.Equal("permissions", selector.Resolve(Entry(fs, "tagged.txt")).Mode.Name);
        Assert.Equal("filename", selector.Resolve(Entry(fs, "plain.txt")).Mode.Name);
    }

    [Fact]
    public void Resolve_LetsAnExplicitChoiceBeatEveryRule()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        LinemodeSelector selector = new(new LinemodeRegistry());
        selector.Add(Always("mtime"));

        FsNode entry = Entry(fs, "a.txt");
        entry.LinemodeOverride = "permissions";

        Assert.Equal("permissions", selector.Resolve(entry).Mode.Name);
    }

    [Fact]
    public void Resolve_FallsBackToTheDefaultForAnEntryMissingTheMetadataAModeNeeds()
    {
        // A half-annotated library should still list cleanly: the fallback is per entry, not
        // per directory.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/described.epub")
            .AddFile("/home/bare.epub")
            .AddFile("/home/.metadata.json",
                     """{"described.epub": {"title": "Dune", "year": "1965"}}""");

        LinemodeSelector selector = new(new LinemodeRegistry(), null, new MetadataManager(fs));
        selector.Add(Always("metatitle"));

        (ILinemode described, FileMetadata metadata) = selector.Resolve(Entry(fs, "described.epub"));
        Assert.Equal("metatitle", described.Name);
        Assert.Equal("Dune", metadata.Title);

        Assert.Equal("filename", selector.Resolve(Entry(fs, "bare.epub")).Mode.Name);
    }

    [Fact]
    public void Resolve_DoesNotReadMetadataForAModeThatDoesNotWantIt()
    {
        // Reading the database for every row of every listing would be wasted work.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/.metadata.json", """{"a.txt": {"title": "Dune"}}""");

        LinemodeSelector selector = new(new LinemodeRegistry(), null, new MetadataManager(fs));

        Assert.Same(FileMetadata.Empty, selector.Resolve(Entry(fs, "a.txt")).Metadata);
    }
}
