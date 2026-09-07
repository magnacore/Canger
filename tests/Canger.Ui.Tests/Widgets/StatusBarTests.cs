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

        // Placement is pinned by AMessageTakesTheSameMarginAsARunningTask; this is about the
        // message displacing the usual contents.
        Assert.StartsWith("something happened", Line(screen).TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("free", Line(screen), StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_TintsTheBarWhileWorkIsOutstanding()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ShowProgressBar = true;
        bar.Progress = 0.5;

        bar.Render(screen);

        // Half the width recoloured, as ranger tints it (statusbar.py:330-336). Compared against
        // the scheme's own colour rather than a literal, so the test says "the bar is the bar
        // colour" and does not have to be edited whenever that colour is chosen differently.
        DefaultColorScheme scheme = new();

        Assert.Equal(scheme.ProgressBarColor, screen[0, 0].Style.Background);
        Assert.NotEqual(scheme.ProgressBarColor, screen[Width - 1, 0].Style.Background);
    }

    [Fact]
    public void Progress_TintsUnderTheTaskLineToo()
    {
        // The defect. While a transfer runs its description fills the bar, and that path wrote
        // the line and returned — so the one moment the bar had progress to show was the moment
        // it was skipped. The tint goes underneath the words, not instead of them.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ShowProgressBar = true;
        bar.Progress = 0.5;
        bar.TaskDescription = "copying src: 50%";

        bar.Render(screen);

        // The margin below is why this is not `StartsWith`; what this test is for is that the words
        // and the tint are both there.
        DefaultColorScheme scheme = new();

        Assert.Contains("copying", screen.TextAt(0), StringComparison.Ordinal);
        Assert.Equal(scheme.ProgressBarColor, screen[0, 0].Style.Background);
        Assert.NotEqual(scheme.ProgressBarColor, screen[Width - 1, 0].Style.Background);
    }

    [Fact]
    public void Progress_NamesTheTextColourOverTheFillAsWellAsTheFill()
    {
        // Reported: "the white text on cyan is hard to read". Ranger sets only the background and
        // leaves the text at whatever it was — measured, the bar emits ESC[0;44m, a reset then
        // background blue, so the words keep the terminal's default foreground. Whether that reads
        // depends entirely on how the terminal renders the fill. Naming both halves is what makes
        // the pairing hold in any palette.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ShowProgressBar = true;
        bar.Progress = 0.5;
        bar.TaskDescription = "copying src: 50%";

        bar.Render(screen);

        DefaultColorScheme scheme = new();

        Assert.Equal(scheme.ProgressBarTextColor, screen[0, 0].Style.Foreground);
        Assert.NotEqual(scheme.ProgressBarTextColor, screen[Width - 1, 0].Style.Foreground);
    }

    [Fact]
    public void Progress_ContrastsTheTextWithTheFill()
    {
        // The point of naming both: a scheme that paired a colour with itself, or left the text at
        // the terminal default over a fill that might be any shade, would be back where it started.
        DefaultColorScheme scheme = new();

        Assert.NotEqual(scheme.ProgressBarColor, scheme.ProgressBarTextColor);
        Assert.False(scheme.ProgressBarTextColor.IsDefault,
                     "the text over the fill must be stated, not inherited");
    }

    [Fact]
    public void Progress_LeavesAMarginBeforeTheTaskDescription()
    {
        // Reported: the description sat against the very edge of the tinted bar with nothing to
        // separate the two.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ShowProgressBar = true;
        bar.Progress = 0.5;
        bar.TaskDescription = "Compressing: demo.tar.lz";

        bar.Render(screen);

        Assert.StartsWith(" Compressing", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageTakesTheSameMarginAsARunningTask()
    {
        // Reported: "the Compressing text starts flush to the left, but as soon as the progress
        // bar appears it gets indented by 1 space, so there is a slight jump". The two are the
        // same headline a moment apart — the plugin's notice, then the task's own line — and
        // giving the margin to only one of them moved the text sideways as the bar arrived.
        //
        // A deliberate divergence: ranger draws messages flush left, through `_draw_message`,
        // which does no tinting. Canger tints beneath the running task, so the margin has to be
        // there; making it unconditional is what keeps the text still.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.Message = "rename: already exists";

        bar.Render(screen);

        Assert.StartsWith(" rename:", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotMoveTheHeadlineWhenAMessageGivesWayToATask()
    {
        // The jump itself, pinned: both phases must start in the same column.
        (StatusBar first, ScreenBuffer messageScreen) = Build();
        first.Message = "Compressing 1 into demo.tar.lz";
        first.Render(messageScreen);

        (StatusBar second, ScreenBuffer taskScreen) = Build();
        second.ShowProgressBar = true;
        second.Progress = 0.5;
        second.TaskDescription = "Compressing: demo.tar.lz:  50%";
        second.Render(taskScreen);

        Assert.Equal(Indent(messageScreen.TextAt(0)), Indent(taskScreen.TextAt(0)));
    }

    /// <summary>How far in the text starts.</summary>
    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    [Fact]
    public void Progress_DoesNotTintUnderAMessageTheUserAskedFor()
    {
        // Ranger draws those through `_draw_message`, which does no tinting, and a notice about
        // something that has already happened is not progress.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ShowProgressBar = true;
        bar.Progress = 0.5;
        bar.Message = "12 files copied";

        bar.Render(screen);

        Assert.NotEqual(Color.Blue, screen[0, 0].Style.Background);
    }

    [Fact]
    public void Progress_LeavesTheBarAloneWhenNothingIsRunning()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ShowProgressBar = true;
        bar.Progress = null;

        bar.Render(screen);

        Assert.NotEqual(Color.Blue, screen[0, 0].Style.Background);
    }
}

/// <summary>
/// The line something outside the task queue contributes — audio playing in the background.
/// </summary>
/// <remarks>
/// Three rules, and the whole point of the field is the third: a message outranks everything, a
/// queued task replaces the bar as it always did, and this shares the bar with what is already
/// there rather than taking it over.
/// </remarks>
public class StatusBarActivityTests
{
    private const string Playing = "00:04:21 / 00:06:44 (3%) 1.5x";

    /// <summary>A bar wide enough for all three fields, as a real terminal is.</summary>
    private const int Roomy = 160;

    private static (StatusBar Bar, ScreenBuffer Screen) Build(int width = Roomy)
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/a.txt", 1000, DateTimeOffset.UnixEpoch);
        fs.AddFileOfSize("/home/b.txt", 2000, DateTimeOffset.UnixEpoch);

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        // FreeBytes is what makes the right-hand block appear, as the tests above rely on.
        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 1_000_000_000 };
        bar.Layout(new Rect(0, 0, width, 1));

        return (bar, new ScreenBuffer(width, 1));
    }

    [Fact]
    public void ItSharesTheBarWithWhatIsAlreadyThere()
    {
        // Not a takeover: the file under the cursor and the free space both stay put. That is the
        // difference between this and a queued task, and it is what the report asked for — "so it
        // does not interfere with the rest of the status bar elements".
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ActivityDescription = Playing;

        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.Contains(Playing, line, StringComparison.Ordinal);
        Assert.StartsWith("-rw", line.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("free", line, StringComparison.Ordinal);
        Assert.Contains("1/2", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ItSitsBetweenTheTwoBlocksAndNotOnTopOfEither()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ActivityDescription = Playing;

        bar.Render(screen);
        string line = screen.TextAt(0);

        int activity = line.IndexOf(Playing, StringComparison.Ordinal);
        int permissions = line.IndexOf("-rw", StringComparison.Ordinal);
        int free = line.IndexOf("free", StringComparison.Ordinal);

        Assert.True(permissions < activity, "the activity overlaps the left-hand block");
        Assert.True(activity + Playing.Length < free, "the activity overlaps the right-hand block");
    }

    [Fact]
    public void ItKeepsItsColumnAsItsFiguresChangeWidth()
    {
        // Right-aligned, so a clock counting up does not shuffle the text sideways every second.
        (StatusBar first, ScreenBuffer one) = Build();
        first.ActivityDescription = "00:04:21 / 00:06:44 (3%) 1.5x";
        first.Render(one);

        (StatusBar second, ScreenBuffer two) = Build();
        second.ActivityDescription = "00:04:22 / 00:06:44 (100%) 1.5x";
        second.Render(two);

        int endOfFirst = one.TextAt(0).IndexOf("1.5x", StringComparison.Ordinal) + 4;
        int endOfSecond = two.TextAt(0).IndexOf("1.5x", StringComparison.Ordinal) + 4;

        Assert.Equal(endOfFirst, endOfSecond);
    }

    [Fact]
    public void AQueuedTaskTakesTheBarBack()
    {
        // Asked for: "if an mka is playing and some other archiving or copy status bar appears, it
        // will override it till its done". The browser stops asking the activity at all while the
        // queue has something to say, so this is what the bar does with both set.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.TaskDescription = "copying big.bin:  38%   570 M/1.5 G";
        bar.ActivityDescription = Playing;

        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.Contains("copying big.bin", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Playing, line, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageStillOutranksIt()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.Message = "rename: already exists";
        bar.ActivityDescription = Playing;

        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.Contains("rename:", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Playing, line, StringComparison.Ordinal);
    }

    [Fact]
    public void ItIsLeftOutRatherThanTruncatedOnANarrowTerminal()
    {
        // A clipped clock is worse than none, and what is under the cursor matters more.
        (StatusBar bar, ScreenBuffer screen) = Build(width: 48);
        bar.ActivityDescription = Playing;

        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.DoesNotContain("00:04:21", line, StringComparison.Ordinal);
        Assert.StartsWith("-rw", line.TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsDrawnWhenThereIsNothingToSay()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ActivityDescription = null;

        bar.Render(screen);

        Assert.Contains("free", screen.TextAt(0), StringComparison.Ordinal);
    }
}
