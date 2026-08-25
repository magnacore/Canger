// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// The built-in commands, driven the way a key binding or the prompt would drive them.
/// </summary>
public class BuiltinCommandTests
{
    private static FakeFileManager Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.log")
            .AddDirectory("/home/docs")
            .AddFile("/home/docs/report.txt")
            .AddFile("/home/.hidden");

        return new FakeFileManager(fs);
    }

    [Fact]
    public void Quit_AsksToExit()
    {
        FakeFileManager manager = Build();

        manager.Execute("quit");

        Assert.True(manager.HasQuit);
    }

    [Fact]
    public void Echo_ShowsItsArgument()
    {
        FakeFileManager manager = Build();

        manager.Execute("echo hello there");

        Assert.Equal("hello there", manager.LastMessage);
    }

    [Fact]
    public void Move_StepsTheCursor()
    {
        FakeFileManager manager = Build();

        manager.Execute("move down=1");

        Assert.Equal(1, manager.CurrentTab.Current.Cursor.Index);
    }

    [Fact]
    public void Move_HonoursAQuantifier()
    {
        FakeFileManager manager = Build();

        manager.Execute("move down=1", quantifier: 2);

        Assert.Equal(2, manager.CurrentTab.Current.Cursor.Index);
    }

    [Fact]
    public void Move_RightEntersTheSelectedDirectory()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "docs"));

        manager.Execute("move right=1");

        Assert.Equal("/home/docs", manager.CurrentTab.Path);
    }

    [Fact]
    public void Move_LeftGoesUp()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.Enter("/home/docs",
                                 cancellationToken: TestContext.Current.CancellationToken);

        manager.Execute("move left=1");

        Assert.Equal("/home", manager.CurrentTab.Path);
    }

    [Fact]
    public void Cd_ChangesDirectory()
    {
        FakeFileManager manager = Build();

        manager.Execute("cd /home/docs");

        Assert.Equal("/home/docs", manager.CurrentTab.Path);
    }

    [Fact]
    public void Cd_ReportsAPathThatDoesNotExist()
    {
        FakeFileManager manager = Build();

        manager.Execute("cd /nowhere");

        Assert.Equal("/home", manager.CurrentTab.Path);
        Assert.True(manager.Messages[^1].IsError);
    }

    [Fact]
    public void Set_ChangesASetting()
    {
        FakeFileManager manager = Build();

        manager.Execute("set scroll_offset 3");

        Assert.Equal(3, manager.Settings.ScrollOffset);
    }

    [Fact]
    public void Set_TogglesWithATrailingBang()
    {
        FakeFileManager manager = Build();

        manager.Execute("set show_hidden!");

        Assert.True(manager.Settings.ShowHidden);
    }

    [Fact]
    public void Set_ReportsABadValueRatherThanThrowing()
    {
        FakeFileManager manager = Build();

        manager.Execute("set scroll_offset banana");

        Assert.True(manager.Messages[^1].IsError);
    }

    [Fact]
    public void MarkFiles_MarksAndAdvances()
    {
        // Marking advances so a run of files can be marked by holding one key.
        FakeFileManager manager = Build();

        manager.Execute("mark_files toggle=True");

        Assert.True(manager.CurrentTab.Current.Entries[0].IsMarked);
        Assert.Equal(1, manager.CurrentTab.Current.Cursor.Index);
    }

    [Fact]
    public void MarkFiles_CanMarkEverything()
    {
        FakeFileManager manager = Build();

        manager.Execute("mark_files all=True toggle=True");

        Assert.Equal(manager.CurrentTab.Current.Count,
                     manager.CurrentTab.Current.MarkedEntries.Count);
    }

    [Fact]
    public void Chain_RunsSeveralCommands()
    {
        FakeFileManager manager = Build();

        manager.Execute("chain set sort=size; set sort_reverse=True");

        Assert.Equal("size", manager.Settings.Sort);
        Assert.True(manager.Settings.SortReverse);
    }

    [Fact]
    public void Copy_RecordsTheSelection()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursor(1);

        manager.Execute("copy");

        Assert.Single(manager.CopyBuffer);
        Assert.False(manager.IsCutPending);
    }

    [Fact]
    public void Cut_RecordsTheSelectionForMoving()
    {
        FakeFileManager manager = Build();

        manager.Execute("cut");

        Assert.Single(manager.CopyBuffer);
        Assert.True(manager.IsCutPending);
    }

    [Fact]
    public void Mkdir_CreatesADirectory()
    {
        FakeFileManager manager = Build();

        manager.Execute("mkdir new folder");

        // The whole rest of the line is the name, so a directory name may contain spaces.
        Assert.True(manager.FileSystem.DirectoryExists("/home/new folder"));
    }

    [Fact]
    public void Mkdir_RefusesToOverwriteSomethingThatExists()
    {
        FakeFileManager manager = Build();

        manager.Execute("mkdir docs");

        Assert.True(manager.Messages[^1].IsError);
    }

    [Fact]
    public void Rename_MovesTheFileAndFollowsIt()
    {
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "alpha.txt"));

        manager.Execute("rename renamed.txt");

        Assert.True(manager.FileSystem.Exists("/home/renamed.txt"));
        Assert.False(manager.FileSystem.Exists("/home/alpha.txt"));
        Assert.Equal("renamed.txt", manager.CurrentTab.Selected?.Basename);
    }

    [Fact]
    public void Rename_RefusesToOverwriteAnExistingFile()
    {
        // Renaming onto an existing file would destroy it silently.
        FakeFileManager manager = Build();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.Single(e => e.Basename == "alpha.txt"));

        manager.Execute("rename beta.txt");

        Assert.True(manager.Messages[^1].IsError);
        Assert.True(manager.FileSystem.Exists("/home/alpha.txt"));
    }

    [Fact]
    public void Console_AsksForThePromptToOpen()
    {
        FakeFileManager manager = Build();

        manager.Execute("console shell ");

        Assert.Equal(("shell ", -1), manager.ConsoleOpenings[0]);
    }

    [Fact]
    public void Console_PlacesTheCursorWhenAsked()
    {
        FakeFileManager manager = Build();

        manager.Execute("console -p6 shell  x");

        Assert.Equal(6, manager.ConsoleOpenings[0].CursorPosition);
    }

    // ---- Scout, the search and filter command ----------------------------------------

    [Fact]
    public void Scout_JumpsToTheFirstMatch()
    {
        FakeFileManager manager = Build();

        manager.Execute("scout beta");

        Assert.Equal("beta.txt", manager.CurrentTab.Selected?.Basename);
    }

    [Fact]
    public void Scout_FiltersPermanentlyWithP()
    {
        FakeFileManager manager = Build();

        manager.Execute("scout -p .txt");

        Assert.Equal(["alpha.txt", "beta.txt"],
                     manager.CurrentTab.Current.Entries.Select(e => e.Basename));
    }

    [Fact]
    public void Scout_InvertsWithV()
    {
        FakeFileManager manager = Build();

        manager.Execute("scout -pv .txt");

        Assert.DoesNotContain("alpha.txt",
                              manager.CurrentTab.Current.Entries.Select(e => e.Basename),
                              StringComparer.Ordinal);
    }

    [Fact]
    public void Scout_MarksMatchesWithM()
    {
        FakeFileManager manager = Build();

        manager.Execute("scout -m .txt");

        Assert.Equal(["alpha.txt", "beta.txt"],
                     manager.CurrentTab.Current.MarkedEntries.Select(e => e.Basename));
    }

    [Fact]
    public void FilterStack_AddsAndClears()
    {
        FakeFileManager manager = Build();

        manager.Execute("filter_stack add type d");
        Assert.Equal(["docs"], manager.CurrentTab.Current.Entries.Select(e => e.Basename));

        manager.Execute("filter_stack clear");
        Assert.True(manager.CurrentTab.Current.Count > 1);
    }

    [Fact]
    public void Flat_FoldsSubdirectoriesIn()
    {
        FakeFileManager manager = Build();

        manager.Execute("flat -1");

        Assert.Contains("docs/report.txt",
                        manager.CurrentTab.Current.Entries.Select(e => e.RelativePath),
                        StringComparer.Ordinal);
    }

    // ---- Error handling --------------------------------------------------------------

    [Fact]
    public void AnUnknownCommand_IsReportedRatherThanIgnored()
    {
        FakeFileManager manager = Build();

        Assert.False(manager.Execute("no_such_command"));
        Assert.Contains("unknown command", manager.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnresolvableMacro_StopsTheCommandRunning()
    {
        // "%c" with an empty clipboard must not expand to nothing and run anyway.
        FakeFileManager manager = Build();

        Assert.False(manager.Execute("echo %c"));
        Assert.True(manager.Messages[^1].IsError);
    }

    /// <summary>A command that throws, to exercise the dispatcher's safety net.</summary>
    [Canger.Core.Commands.Command("explode")]
    private sealed class ExplodingCommand : Canger.Core.Commands.CangerCommand
    {
        public override void Execute() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void AThrowingCommand_IsReportedRatherThanCrashingTheSession()
    {
        // A bug in a built-in or user-written command must become a status message, not the end
        // of the session with an unsaved state and a wrecked terminal.
        FakeFileManager manager = Build();
        manager.Commands.Register<ExplodingCommand>();

        Assert.False(manager.Execute("explode"));
        Assert.True(manager.Messages[^1].IsError);
        Assert.Contains("boom", manager.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommandThatReportsAProblem_StillCountsAsHavingRun()
    {
        // Execute answers "did a command run", not "did it succeed"; the message carries the
        // outcome.
        FakeFileManager manager = Build();

        Assert.True(manager.Execute("cd /nonexistent/deeply/nested"));
        Assert.True(manager.Messages[^1].IsError);
    }
}
