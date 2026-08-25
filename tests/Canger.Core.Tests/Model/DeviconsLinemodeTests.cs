// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// The glyph before each name saying what kind of file it is.
/// </summary>
/// <remarks>
/// In ranger this is a Python plugin, so a configuration carried over from ranger asks for a
/// linemode Canger cannot load. Built in here instead: the tables are data and the resolution is
/// three lookups.
/// </remarks>
public class DeviconsLinemodeTests
{
    private static readonly LinemodeContext Context =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Loads a directory and returns one of its entries.</summary>
    private static FsNode Entry(InMemoryFileSystem fs, string name, string path = "/home")
    {
        DirectoryNode directory = new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);
        return directory.Entries.Single(e => e.RelativePath == name);
    }

    [Fact]
    public void Registry_OffersDeviconsByName()
    {
        // The whole point: `default_linemode devicons` has to resolve.
        Assert.NotNull(new LinemodeRegistry().Find("devicons"));
    }

    [Fact]
    public void Title_PutsAGlyphBeforeTheName()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/song.mp3");
        ILinemode mode = new DeviconsLinemode();

        string title = mode.Title(Entry(fs, "song.mp3"), FileMetadata.Empty, Context);

        Assert.EndsWith(" song.mp3", title, StringComparison.Ordinal);
        Assert.True(title.Length > "song.mp3".Length + 1);
    }

    [Fact]
    public void Glyph_GivesTheSameIconToFilesOfOneKind()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.mp3")
            .AddFile("/home/b.flac");

        // Both are audio, so both carry the audio glyph.
        Assert.Equal(DeviconsLinemode.Glyph(Entry(fs, "a.mp3")),
                     DeviconsLinemode.Glyph(Entry(fs, "b.flac")));
    }

    [Fact]
    public void Glyph_DistinguishesKindsFromEachOther()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/song.mp3")
            .AddFile("/home/code.cs")
            .AddDirectory("/home/folder");

        string audio = DeviconsLinemode.Glyph(Entry(fs, "song.mp3"));
        string code = DeviconsLinemode.Glyph(Entry(fs, "code.cs"));
        string folder = DeviconsLinemode.Glyph(Entry(fs, "folder"));

        Assert.NotEqual(audio, code);
        Assert.NotEqual(audio, folder);
        Assert.NotEqual(code, folder);
    }

    [Fact]
    public void Glyph_PrefersAWholeFilenameOverAnExtension()
    {
        // Makefile and .gitignore have no useful extension, and Dockerfile would otherwise be
        // indistinguishable from any other extensionless file.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/Dockerfile")
            .AddFile("/home/plain");

        Assert.NotEqual(DeviconsLinemode.Glyph(Entry(fs, "plain")),
                        DeviconsLinemode.Glyph(Entry(fs, "Dockerfile")));
    }

    [Fact]
    public void Glyph_FallsBackRatherThanShowingNothing()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/thing.qqzz");

        Assert.NotEmpty(DeviconsLinemode.Glyph(Entry(fs, "thing.qqzz")));
    }

    [Fact]
    public void Glyph_IgnoresTheCaseOfAnExtension()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.MP3")
            .AddFile("/home/b.mp3");

        Assert.Equal(DeviconsLinemode.Glyph(Entry(fs, "b.mp3")),
                     DeviconsLinemode.Glyph(Entry(fs, "a.MP3")));
    }

    [Fact]
    public void Detail_LeavesTheRightHandSideToTheColumn()
    {
        // The mode changes the name, nothing else — so it defers, exactly as `filename` does,
        // and the column supplies the size.
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/big.bin", 2_500_000, Context.Now);

        ILinemode mode = new DeviconsLinemode();

        Assert.Null(mode.Detail(Entry(fs, "big.bin"), FileMetadata.Empty, Context));
    }

    [Fact]
    public void Detail_CanBeGivenAnotherModeToSupplyIt()
    {
        // Which is how devicons could be combined with, say, the modification time.
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/big.bin", 2_500_000, Context.Now);

        ILinemode mode = new DeviconsLinemode(new SizeLinemodeStub());

        Assert.Equal("stub", mode.Detail(Entry(fs, "big.bin"), FileMetadata.Empty, Context));
    }

    /// <summary>Stands in for a mode that does fill the right-hand side.</summary>
    private sealed class SizeLinemodeStub : ILinemode
    {
        public string Name => "stub";

        public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context) =>
            node.RelativePath;

        public string? Detail(FsNode node, FileMetadata metadata, in LinemodeContext context) =>
            "stub";
    }
}
