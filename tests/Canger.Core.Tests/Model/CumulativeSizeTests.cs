// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// What <c>dc</c> puts in the size column, and what happens to it afterwards.
/// </summary>
/// <remarks>
/// Ranger's three states, from <c>container/directory.py:378-391</c> and <c>:582-585</c>: a
/// directory shows its entry count until it is measured, then the measured size, and then — once
/// the listing has been re-read and the figure can no longer be vouched for — the same size with a
/// <c>?</c> against it. <c>reset</c> discards the directory objects, so the count comes back.
/// </remarks>
public class CumulativeSizeTests
{
    private static DirectoryNode Measured(InMemoryFileSystem fs, string path, long size)
    {
        DirectoryNode directory = new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);
        directory.CumulativeSize = size;

        return directory;
    }

    [Fact]
    public void ADirectory_ShowsItsEntryCountUntilItIsMeasured()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/sub/a.bin")
            .AddFile("/home/sub/b.bin");

        DirectoryNode sub = new(fs, "/home/sub", fs.GetStatus("/home/sub", followSymbolicLinks: true));
        sub.Load(TestContext.Current.CancellationToken);

        Assert.Equal("2", LinemodeText.Size(sub));
    }

    [Fact]
    public void AMeasuredDirectory_ShowsTheSizeInsteadOfTheCount()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/sub/a.bin");

        Assert.Equal("4.5 k", LinemodeText.Size(Measured(fs, "/home/sub", 4500)));
    }

    [Fact]
    public void AMeasuredDirectory_GainsAQuestionMarkWhenItIsReadAgain()
    {
        // Not "when it changed": ranger cannot tell without measuring again, and says so.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/sub/a.bin");
        DirectoryNode sub = Measured(fs, "/home/sub", 4500);

        sub.Load(TestContext.Current.CancellationToken);

        Assert.True(sub.CumulativeSizeStale);
        Assert.Equal("4.5? k", LinemodeText.Size(sub));
    }

    [Fact]
    public void AnUnmeasuredDirectory_IsNotMarkedByAReload()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/sub/a.bin");
        DirectoryNode sub = new(fs, "/home/sub", fs.GetStatus("/home/sub", followSymbolicLinks: true));

        sub.Load(TestContext.Current.CancellationToken);
        sub.Load(TestContext.Current.CancellationToken);

        Assert.False(sub.CumulativeSizeStale);
        Assert.Equal("1", LinemodeText.Size(sub));
    }

    [Fact]
    public void WithAutoupdateOn_TheSizeIsTakenAgainRatherThanMarked()
    {
        // autoupdate_cumulative_size: pay for another walk of the tree instead of showing a `?`.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFileOfSize("/home/sub/a.bin", 1000);
        DirectoryNode sub = Measured(fs, "/home/sub", 4500);
        sub.AutoupdateCumulativeSize = true;

        sub.Load(TestContext.Current.CancellationToken);

        Assert.False(sub.CumulativeSizeStale);
        Assert.Equal(1000, sub.CumulativeSize);
    }

    [Fact]
    public void ASymbolicLinkToADirectory_KeepsRangersArrow()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/real/a.bin")
            .AddSymbolicLink("/home/link", "/home/real");

        DirectoryNode link = new(fs, "/home/link",
                                 fs.GetStatus("/home/link", followSymbolicLinks: true),
                                 fs.GetStatus("/home/link"));
        link.Load(TestContext.Current.CancellationToken);
        link.CumulativeSize = 4500;

        Assert.Equal("-> 4.5 k", LinemodeText.Size(link));
    }

    [Theory]
    [InlineData(4500, " ", "4.5 k")]
    [InlineData(4500, "? ", "4.5? k")]
    [InlineData(512, " ", "512 B")]
    public void Format_PutsTheSeparatorBetweenTheNumberAndTheUnit(
        long bytes, string separator, string expected) =>
        Assert.Equal(expected, HumanReadable.Format(bytes, binary: false, separator));

    [Fact]
    public void MeasuringAgain_ClearsTheDoubtAboutTheOldFigure()
    {
        // The reported sequence: `dc`, delete something, the `?` appears because the listing was
        // re-read, `dc` again — and the `?` stayed. `get_cumulative_size` set the new figure and
        // left the flag alone, so the marker was permanent once anything had set it.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFileOfSize("/home/sub/a.bin", 4096, DateTimeOffset.UnixEpoch);

        FakeFileManager manager = new(fs, "/home");
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == "sub"));

        manager.Execute("get_cumulative_size");
        FsNode sub = manager.CurrentTab.Current.Entries.First(e => e.Basename == "sub");

        Assert.Equal(4096L, sub.CumulativeSize);

        // What a re-read after a change does.
        sub.CumulativeSizeStale = true;

        manager.Execute("get_cumulative_size");

        Assert.False(sub.CumulativeSizeStale, "the figure was just taken; the doubt is not about it");
    }

    [Fact]
    public void MeasuringAgain_ReportsTheNewSize()
    {
        // And it is a new figure, not the old one with the marker rubbed off.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFileOfSize("/home/sub/a.bin", 4096, DateTimeOffset.UnixEpoch)
            .AddFileOfSize("/home/sub/b.bin", 1024, DateTimeOffset.UnixEpoch);

        FakeFileManager manager = new(fs, "/home");
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == "sub"));

        manager.Execute("get_cumulative_size");
        FsNode sub = manager.CurrentTab.Current.Entries.First(e => e.Basename == "sub");
        Assert.Equal(5120L, sub.CumulativeSize);

        fs.Delete("/home/sub/b.bin");
        sub.CumulativeSizeStale = true;

        manager.Execute("get_cumulative_size");

        Assert.Equal(4096L, sub.CumulativeSize);
        Assert.False(sub.CumulativeSizeStale);
    }
}
