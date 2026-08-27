// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// The eight ways a row can be rendered, and the rules that pick between them.
/// </summary>
public class LinemodeTests
{
    /// <summary>A fixed moment, so "today" and "this week" mean the same thing every run.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 14, 30, 0, TimeSpan.Zero);

    private static readonly LinemodeContext Context = new(Now);

    /// <summary>Builds a loaded directory over an in-memory tree.</summary>
    private static DirectoryNode Load(InMemoryFileSystem fs, string path = "/home")
    {
        DirectoryNode directory = new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);
        return directory;
    }

    private static FsNode Entry(InMemoryFileSystem fs, string name, string path = "/home") =>
        Load(fs, path).Entries.Single(e => e.RelativePath == name);

    /// <summary>A status carrying only what the permission rendering reads.</summary>
    private static FileStatus Status(FileKind kind, uint mode) =>
        new(kind, mode, 0, 1, 0, 0, 1, 1, Now, Now, Now);

    [Fact]
    public void Registry_HoldsRangersEightModesAndNoMore()
    {
        // Ranger's eight, and `devicons` deliberately not among them: it is a plugin there and a
        // plugin here, shipped as `config/plugins/devicons.cs`. Having it built in as well meant
        // two copies of the same four hundred glyphs, one shadowing the other whenever the plugin
        // was present. `ShippedDeviconsTests` covers the plugin.
        LinemodeRegistry registry = new();

        Assert.Equal(
            [
                "filename", "metatitle", "permissions", "fileinfo",
                "mtime", "sizemtime", "humanreadablemtime", "sizehumanreadablemtime",
            ],
            registry.Names);
    }

    [Fact]
    public void Registry_LetsAPluginReplaceABuiltInWithoutAddingAName()
    {
        // This is how a decoration plugin prefixes icons: it re-registers "filename".
        LinemodeRegistry registry = new();
        int before = registry.Names.Count;

        registry.Register(new DecoratedFilenameLinemode());

        Assert.Equal(before, registry.Names.Count);
        Assert.IsType<DecoratedFilenameLinemode>(registry.Find("filename"));
    }

    [Fact]
    public void FilenameMode_LeavesTheDetailToTheColumn()
    {
        // Returning null is how a mode says "only you know how much room is left".
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt", "hello");

        ILinemode mode = new FilenameLinemode();
        FsNode entry = Entry(fs, "a.txt");

        Assert.Equal("a.txt", mode.Title(entry, FileMetadata.Empty, Context));
        Assert.Null(mode.Detail(entry, FileMetadata.Empty, Context));
    }

    [Fact]
    public void PermissionsMode_PutsThePermissionsInTheTitleAndLeavesTheRightEmpty()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt", "hello");

        ILinemode mode = new PermissionsLinemode();
        FsNode entry = Entry(fs, "a.txt");

        // The row is already full; a size on the right would push the name out of view.
        Assert.Equal(string.Empty, mode.Detail(entry, FileMetadata.Empty, Context));
        Assert.EndsWith("a.txt", mode.Title(entry, FileMetadata.Empty, Context), StringComparison.Ordinal);
        Assert.StartsWith("-rw", mode.Title(entry, FileMetadata.Empty, Context), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FileKind.Directory, 'd')]
    [InlineData(FileKind.Fifo, 'p')]
    [InlineData(FileKind.Socket, 's')]
    [InlineData(FileKind.BlockDevice, 'b')]
    [InlineData(FileKind.CharacterDevice, 'c')]
    [InlineData(FileKind.Regular, '-')]
    public void Permissions_NamesTheKindTheWayLsDoes(FileKind kind, char expected)
    {
        // 0o600.
        Assert.Equal(expected, LinemodeText.Permissions(Status(kind, 0x180))[0]);
    }

    [Fact]
    public void Permissions_RendersEachBitInItsOwnPlace()
    {
        // 0o741.
        Assert.Equal("-rwxr----x", LinemodeText.Permissions(Status(FileKind.Regular, 0x1E1)));
    }

    [Fact]
    public void ModificationTimeMode_ShowsAQuestionMarkWhenTheStatusCouldNotBeRead()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddSymbolicLink("/home/broken", "/nowhere");

        ILinemode mode = new ModificationTimeLinemode();
        FsNode entry = Entry(fs, "broken");

        Assert.Null(entry.Status);
        Assert.Equal("?", mode.Detail(entry, FileMetadata.Empty, Context));
    }

    [Fact]
    public void SizeModes_ShowBothTheSizeAndTheTime()
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/big.bin", 2_500_000, Now.AddDays(-3));

        FsNode entry = Entry(fs, "big.bin");
        string detail = new SizeAndModificationTimeLinemode()
            .Detail(entry, FileMetadata.Empty, Context)!;

        // The time is rendered in local time, so only the size and shape are pinned here; the
        // formatting itself is covered by HumanReadable's own tests.
        Assert.StartsWith("2.5 M ", detail, StringComparison.Ordinal);
        Assert.Equal(LinemodeText.ModifyTime(entry.Status!.Value), detail["2.5 M ".Length..]);
    }

    [Fact]
    public void HumanReadableTimeMode_AbbreviatesTodayToAClockTime()
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/today.bin", 10, Now.AddHours(-1));

        FsNode entry = Entry(fs, "today.bin");
        string detail = new HumanReadableTimeLinemode()
            .Detail(entry, FileMetadata.Empty, new LinemodeContext(Now))!;

        Assert.Matches(@"^\d{2}:\d{2}$", detail);
    }

    [Fact]
    public void SizeMode_UsesBinaryPrefixesWhenTheSettingAsksForThem()
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/big.bin", 1024 * 1024, Now);

        FsNode entry = Entry(fs, "big.bin");

        // One mebibyte exactly, so the binary form is a round 1 — and the decimal form is not,
        // which is the whole point of the setting.
        Assert.Equal("1 Mi", LinemodeText.Size(entry, binaryPrefix: true));
        Assert.Equal("1.05 M", LinemodeText.Size(entry, binaryPrefix: false));
    }

    [Fact]
    public void Size_CountsADirectoryTheUserHasNotOpened()
    {
        // This is what automatically_count_files asks for, and it is what ranger does: one
        // shallow read of the directory, not a walk of everything beneath it.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/sub/a.txt")
            .AddFile("/home/sub/b.txt");

        DirectoryNode sub = (DirectoryNode)Entry(fs, "sub");

        Assert.False(sub.IsLoaded);
        Assert.Equal("2", LinemodeText.Size(sub));
    }

    [Fact]
    public void Size_ShowsNothingForAnUnopenedDirectoryWhenCountingIsOff()
    {
        // On a slow or networked filesystem one read per visible row is noticeable, which is
        // why this is a setting rather than always done.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/sub/a.txt")
            .AddFile("/home/sub/b.txt");

        DirectoryNode sub = (DirectoryNode)Entry(fs, "sub");

        Assert.Equal(string.Empty, LinemodeText.Size(sub, countFiles: false));
    }

    [Fact]
    public void Size_PrefersTheLoadedListingsOwnCount()
    {
        // Once opened, the count has to come from the listing rather than from a second read:
        // the listing is filtered, and the two would otherwise disagree.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/sub/a.txt")
            .AddFile("/home/sub/b.txt");

        DirectoryNode sub = (DirectoryNode)Entry(fs, "sub");
        sub.Load(TestContext.Current.CancellationToken);

        Assert.Equal("2", LinemodeText.Size(sub, countFiles: false));
    }

    [Fact]
    public void Size_CachesTheCountRatherThanReadingPerFrame()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/sub/a.txt");
        DirectoryNode sub = (DirectoryNode)Entry(fs, "sub");

        int before = fs.CountEntriesCount;
        LinemodeText.Size(sub);
        int afterFirst = fs.CountEntriesCount;
        LinemodeText.Size(sub);

        Assert.Equal(afterFirst, fs.CountEntriesCount);
        Assert.True(afterFirst > before);
    }

    [Fact]
    public void Size_CountsWithoutStattingEveryEntryInside()
    {
        // `automatically_count_files` wants one integer per visible directory. Getting it from a
        // full listing cost one statx per entry of every one of those directories and threw all of
        // it away — 70ms of a 420ms settle on a 20,546-entry listing, measured.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/sub/a.txt")
            .AddFile("/home/sub/b.txt")
            .AddFile("/home/sub/c.txt");

        DirectoryNode sub = (DirectoryNode)Entry(fs, "sub");
        int listings = fs.ListCount;

        Assert.Equal("3", LinemodeText.Size(sub));
        Assert.Equal(listings, fs.ListCount);
    }

    [Fact]
    public void MetatitleMode_PrefersTheRecordedTitleAndYear()
    {
        ILinemode mode = new MetatitleLinemode();
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        FsNode entry = Entry(fs, "a.txt");

        FileMetadata metadata = new(new Dictionary<string, string>
        {
            ["title"] = "Dune",
            ["year"] = "1965",
            ["authors"] = "Herbert, Frank",
        });

        Assert.Equal("1965 - Dune", mode.Title(entry, metadata, Context));

        // Only the first author fits, and it is the one that identifies the work.
        Assert.Equal("Herbert", mode.Detail(entry, metadata, Context));
    }

    [Fact]
    public void MetatitleMode_OmitsTheYearWhenThereIsNone()
    {
        ILinemode mode = new MetatitleLinemode();
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        FsNode entry = Entry(fs, "a.txt");

        FileMetadata metadata = new(new Dictionary<string, string> { ["title"] = "Dune" });

        Assert.Equal("Dune", mode.Title(entry, metadata, Context));
        Assert.Equal(string.Empty, mode.Detail(entry, metadata, Context));
    }

    /// <summary>Stands in for a plugin that decorates names.</summary>
    private sealed class DecoratedFilenameLinemode : ILinemode
    {
        public string Name => "filename";

        public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context) =>
            "* " + node.RelativePath;
    }
}
