// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;
using Canger.Vcs;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The bottom line: what the cursor is on, where in the listing it is, and what the repository
/// last committed.
/// </summary>
public class StatusBarTests
{
    private const int Width = 100;

    /// <summary>Builds a status bar over a small directory.</summary>
    private static (StatusBar Bar, ScreenBuffer Screen) Build(int width = Width)
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/a.txt", 8, new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero));
        fs.AddFile("/home/b.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 1_000_000_000 };
        bar.Layout(new Rect(0, 0, width, 1));

        return (bar, new ScreenBuffer(width, 1));
    }

    private static string Line(ScreenBuffer screen) => screen.TextAt(0);

    [Fact]
    public void Draw_ShowsPermissionsOwnershipAndTime()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.Render(screen);

        string line = Line(screen);
        Assert.StartsWith("-rw", line, StringComparison.Ordinal);
        Assert.Contains("2026-01-02", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsThePositionAndFreeSpaceOnTheRight()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.Render(screen);

        string line = Line(screen);
        Assert.Contains("free", line, StringComparison.Ordinal);
        Assert.Contains("1/2", line, StringComparison.Ordinal);
    }

    /// <summary>A listing of two files and two directories, with the directories marked.</summary>
    private static (StatusBar Bar, ScreenBuffer Screen, DirectoryNode Directory) BuildWithFolders()
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/a.txt", 1000, DateTimeOffset.UnixEpoch);
        fs.AddFileOfSize("/home/b.txt", 2000, DateTimeOffset.UnixEpoch);
        fs.AddDirectory("/home/one");
        fs.AddFile("/home/one/x");
        fs.AddFile("/home/one/y");
        fs.AddDirectory("/home/two");
        fs.AddFile("/home/two/z");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 1_000_000_000 };
        bar.Layout(new Rect(0, 0, Width, 1));

        return (bar, new ScreenBuffer(Width, 1), tab.Current);
    }

    [Fact]
    public void Draw_DoesNotCountAFolderWhoseSizeHasNotBeenMeasured()
    {
        // The reported bug: two folders holding sixteen things between them read "16 B marked",
        // because a directory's `Size` is its entry count and the sum added it as bytes. Ranger
        // skips a directory until `dc` has measured it (`statusbar.py:288-289`).
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();

        foreach (FsNode entry in directory.Entries.Where(e => e.IsDirectory))
        {
            entry.IsMarked = true;
        }

        bar.Render(screen);

        Assert.Contains("0/2  Mrk", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_CountsAFolderOnceItHasBeenMeasured()
    {
        // What `dc` sets. The total then appears here, on the right, rather than as a message on
        // the left that says it once and goes away.
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();

        foreach (FsNode entry in directory.Entries.Where(e => e.IsDirectory))
        {
            entry.IsMarked = true;
            entry.CumulativeSize = 5_000;
        }

        bar.Render(screen);

        Assert.Contains("10 k/2  Mrk", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_AddsUpMarkedFilesInBytes()
    {
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();

        foreach (FsNode entry in directory.Entries.Where(e => !e.IsDirectory))
        {
            entry.IsMarked = true;
        }

        Assert.Contains("3 k/2  Mrk", Line(Rendered(bar, screen)), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_HighlightsTheMarkIndicatorWithoutHighlightingTheSizeBesideIt()
    {
        // The whole right-hand side used to be written in one style, so the `marked` colour —
        // `bold | reverse`, i.e. a solid block — spread across the byte count too. Ranger colours
        // each fragment on its own (`gui/widgets/statusbar.py:253-327`) and adds the sizes with no
        // context at all, so only the indicator lights up.
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();

        foreach (FsNode entry in directory.Entries.Where(e => !e.IsDirectory))
        {
            entry.IsMarked = true;
        }

        bar.Render(screen);

        string line = Line(screen);
        int mrk = line.IndexOf("Mrk", StringComparison.Ordinal);
        int size = line.IndexOf("3 k/2", StringComparison.Ordinal);
        Assert.True(mrk > 0 && size > 0, line);

        Assert.True(screen[mrk, 0].Style.Attributes.HasFlag(CellAttributes.Reverse),
                    "the mark indicator should stand out");
        Assert.False(screen[size, 0].Style.Attributes.HasFlag(CellAttributes.Reverse),
                     "the size beside it should not");

        // Including the gap between them, which is where a stray block would show most.
        Assert.False(screen[mrk - 1, 0].Style.Attributes.HasFlag(CellAttributes.Reverse),
                     "the separator should not be highlighted");
    }

    [Fact]
    public void Draw_DoesNotHighlightThePositionWhenNothingIsMarked()
    {
        // The unmarked line carries `scroll` on the position and its indicator, and nothing on the
        // sizes. None of it reverses, so the bar stays quiet until there is something to say.
        (StatusBar bar, ScreenBuffer screen) = Build();

        bar.Render(screen);

        string line = Line(screen);
        int sum = line.IndexOf("sum", StringComparison.Ordinal);
        Assert.True(sum > 0, line);

        for (int x = sum; x < Width; x++)
        {
            Assert.False(screen[x, 0].Style.Attributes.HasFlag(CellAttributes.Reverse),
                         $"column {x} should not be highlighted: {line}");
        }
    }

    [Fact]
    public void Draw_UsesTheDirectorysOwnTotalWhenEverythingIsMarked()
    {
        // Ranger's shortcut (`statusbar.py:285-286`): the same figure the unmarked line shows,
        // so the number does not appear to change the moment you press `v`.
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();
        directory.SetAllMarked(true);

        Assert.Contains("3 k/4  Mrk", Line(Rendered(bar, screen)), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheDirectoryTotalAndFreeSpaceWhenNothingIsMarked()
    {
        (StatusBar bar, ScreenBuffer screen, _) = BuildWithFolders();

        string line = Line(Rendered(bar, screen));

        Assert.Contains("3 k sum, 1 G free", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Mrk", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ReplacesThePositionIndicatorWithMrk()
    {
        // Marks are easy to scroll away from and forget; ranger gives up the position indicator
        // to say so (`statusbar.py:304-307`).
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();
        directory.Entries[0].IsMarked = true;

        string line = Line(Rendered(bar, screen));

        Assert.Contains("Mrk", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Top", line, StringComparison.Ordinal);
    }

    private static ScreenBuffer Rendered(StatusBar bar, ScreenBuffer screen)
    {
        bar.Render(screen);
        return screen;
    }

    [Fact]
    public void Draw_SaysWhenAVisualSelectionIsStillOpen()
    {
        // Without this a range quietly absorbs a file created between its ends, which reads as a
        // bug rather than as a range doing its job. Ranger shows nothing here either.
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();
        directory.Entries[0].IsMarked = true;
        bar.IsVisualMode = true;

        Assert.Contains("Mrk  VIS", Line(Rendered(bar, screen)), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_DistinguishesTheRangeThatUnmarks()
    {
        // `uV`, following the u-means-undo convention the bindings already use.
        (StatusBar bar, ScreenBuffer screen, _) = BuildWithFolders();
        bar.IsVisualMode = true;
        bar.IsVisualReverse = true;

        Assert.Contains("UNVIS", Line(Rendered(bar, screen)), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_SaysNothingWhenTheRangeIsClosed()
    {
        // Marks made by a closed range stay marked, and are no longer live — which is the whole
        // distinction the indicator exists to draw.
        (StatusBar bar, ScreenBuffer screen, DirectoryNode directory) = BuildWithFolders();
        directory.Entries[0].IsMarked = true;

        string line = Line(Rendered(bar, screen));

        Assert.Contains("Mrk", line, StringComparison.Ordinal);
        Assert.DoesNotContain("VIS", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheRangeEvenWithNothingMarkedYet()
    {
        // `uV` on an unmarked listing marks nothing, so there is no `Mrk` to hang it off.
        (StatusBar bar, ScreenBuffer screen, _) = BuildWithFolders();
        bar.IsVisualMode = true;

        Assert.Contains("VIS", Line(Rendered(bar, screen)), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheLatestCommitWhenThereIsOne()
    {
        // Wider than the default, because the right-hand side is laid out first and takes what
        // it needs — the branch and commit are what give way when the line is short.
        (StatusBar bar, ScreenBuffer screen) = Build(width: 120);
        bar.Head = new VcsCommit("abc1234", "abc1234def", "Tester",
                                 new DateTimeOffset(2026, 5, 6, 7, 8, 0, TimeSpan.Zero),
                                 "the commit summary");

        bar.Render(screen);

        Assert.Contains("the commit summary", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_TruncatesTheCommitSummaryToTheConfiguredLength()
    {
        // A commit message can be a paragraph; this is one line shared with everything else.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.VcsMessageLength = 10;
        bar.Head = new VcsCommit("abc", "abc", "Tester", DateTimeOffset.UnixEpoch,
                                 "a very long commit summary that would not fit");

        bar.Render(screen);

        Assert.DoesNotContain("would not fit", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_DoesNotLetTheCommitOverwriteThePositionIndicator()
    {
        // Both sides grow with what they describe, and without a limit the longer simply
        // overwrote the shorter.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.VcsMessageLength = 200;
        bar.Head = new VcsCommit("abc", "abc", "Tester", DateTimeOffset.UnixEpoch,
                                 new string('x', 300));

        bar.Render(screen);

        Assert.Contains("free", Line(screen), StringComparison.Ordinal);
        Assert.Contains("1/2", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_DropsTheCommitEntirelyWhenThereIsNoRoomForIt()
    {
        // Half a date says nothing; better to show none of it and keep the file's own details.
        (StatusBar bar, ScreenBuffer screen) = Build(width: 55);
        bar.Head = new VcsCommit("abc", "abc", "Tester",
                                 new DateTimeOffset(2026, 5, 6, 7, 8, 0, TimeSpan.Zero),
                                 "summary");

        bar.Render(screen);

        Assert.DoesNotContain("summary", Line(screen), StringComparison.Ordinal);
        Assert.StartsWith("-rw", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsAMessageInsteadOfEverythingElse()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.Message = "something happened";

        bar.Render(screen);

        Assert.StartsWith("something happened", Line(screen), StringComparison.Ordinal);
        Assert.DoesNotContain("free", Line(screen), StringComparison.Ordinal);
    }
}
