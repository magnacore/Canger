// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Commands that run programs or open files, checked by what they ask the runner and opener to
/// do rather than by launching anything.
/// </summary>
public class ProcessCommandTests
{
    private static FakeFileManager Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/notes.txt")
            .AddFile("/home/report.pdf")
            .AddDirectory("/home/docs");

        return new FakeFileManager(fs);
    }

    [Fact]
    public void Shell_RunsWhatItWasGiven()
    {
        FakeFileManager manager = Build();

        manager.Execute("shell echo hello");

        Assert.Equal("echo hello", manager.RecordedRuns.Requests[0].Command);
    }

    [Fact]
    public void Shell_PassesItsFlagsThrough()
    {
        FakeFileManager manager = Build();

        manager.Execute("shell -f -- mpv video.mkv");

        Assert.True(manager.RecordedRuns.Requests[0].Flags.Fork);
        Assert.Equal("mpv video.mkv", manager.RecordedRuns.Requests[0].Command);
    }

    [Fact]
    public void Shell_QuotesMacroValuesSoAFilenameCannotBecomeACommand()
    {
        // The whole point of quoting: a file named like an injection stays one argument.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/; rm -rf ~");
        FakeFileManager manager = new(fs);
        manager.CurrentTab.MoveCursor(0);

        manager.Execute("shell cat %f");

        Assert.Equal("cat '; rm -rf ~'", manager.RecordedRuns.Requests[0].Command);
    }

    [Fact]
    public void Shell_ReportsWhenThereIsNothingToRun()
    {
        FakeFileManager manager = Build();

        manager.Execute("shell");

        Assert.Empty(manager.RecordedRuns.Requests);
        Assert.True(manager.Messages[^1].IsError);
    }

    [Fact]
    public void Open_OpensTheSelection()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "notes.txt"));

        manager.Execute("open");

        Assert.Equal(["/home/notes.txt"], manager.RecordedOpens.Opened[0].Paths);
    }

    [Fact]
    public void Open_EntersADirectoryRatherThanLaunchingSomething()
    {
        // Entering and opening are the same gesture, so one command has to do both.
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "docs"));

        manager.Execute("open");

        Assert.Equal("/home/docs", manager.CurrentTab.Path);
        Assert.Empty(manager.RecordedOpens.Opened);
    }

    [Fact]
    public void Open_OpensEveryMarkedFileTogether()
    {
        FakeFileManager manager = Build();
        foreach (Core.Model.FsNode entry in manager.CurrentTab.Current.Entries.Where(e => !e.IsDirectory))
        {
            entry.IsMarked = true;
        }

        manager.Execute("open");

        Assert.Equal(2, manager.RecordedOpens.Opened[0].Paths.Count);
    }

    [Fact]
    public void Open_AsksTheUserWhenTheRulesDecline()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "notes.txt"));
        manager.RecordedOpens.Result = new OpenResult(false, NeedsUserChoice: true);

        manager.Execute("open");

        Assert.Equal("open_with ", manager.ConsoleOpenings[0].Text);
    }

    [Fact]
    public void Open_ReportsAFailure()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "notes.txt"));
        manager.RecordedOpens.Result = new OpenResult(false, Message: "no rule matches");

        manager.Execute("open");

        Assert.True(manager.Messages[^1].IsError);
    }

    [Fact]
    public void OpenWith_ReadsANumber()
    {
        FakeFileManager manager = Build();

        manager.Execute("open_with 2");

        Assert.Equal(2, manager.RecordedOpens.Opened[0].Number);
    }

    [Fact]
    public void OpenWith_ReadsALabel()
    {
        FakeFileManager manager = Build();

        manager.Execute("open_with editor");

        Assert.Equal("editor", manager.RecordedOpens.Opened[0].Label);
    }

    [Fact]
    public void MoveRight_OpensAFileButEntersADirectory()
    {
        FakeFileManager manager = Build();

        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "docs"));
        manager.Execute("move right=1");
        Assert.Equal("/home/docs", manager.CurrentTab.Path);

        manager.CurrentTab.Enter("/home", cancellationToken: TestContext.Current.CancellationToken);
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "notes.txt"));
        manager.Execute("move right=1");

        Assert.Equal(["/home/notes.txt"], manager.RecordedOpens.Opened[0].Paths);
    }

    [Fact]
    public void Edit_LaunchesAnEditor()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "notes.txt"));

        manager.Execute("edit");

        Assert.Contains("notes.txt", manager.RecordedRuns.Requests[0].Command,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void DrawPossiblePrograms_ListsTheAlternatives()
    {
        FakeFileManager manager = Build();
        manager.RecordedOpens.AvailableAlternatives.Add(new OpenAlternative(0, "editor", "vim"));
        manager.RecordedOpens.AvailableAlternatives.Add(new OpenAlternative(1, "pager", "less"));

        manager.Execute("draw_possible_programs");

        Assert.Contains("editor", manager.LastMessage!, StringComparison.Ordinal);
        Assert.Contains("pager", manager.LastMessage!, StringComparison.Ordinal);
    }
}
