// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// Running an external command on the task queue instead of in front of the interface.
/// </summary>
/// <remarks>
/// This exists because unpacking an archive took the terminal away and held it: the screen went
/// blank, nothing else could be done, and there was no way to tell whether anything was
/// happening. Ranger runs the same command through its loader (<c>core/loader.py:162-278</c>),
/// which is what puts it in the task view with a spinner and leaves the browser alone.
/// </remarks>
public class CommandTaskTests
{
    private static (CommandTask Task, FakeFileManager.RecordingProcessRunner Runner,
                    List<(string Message, bool IsError)> Notices)
        Build(FakeFileManager.FakeBackgroundProcess? process = null,
              Action<CommandTask>? finished = null)
    {
        FakeFileManager.RecordingProcessRunner runner = new();

        if (process is not null)
        {
            runner.BackgroundResults["unzip"] = process;
        }

        List<(string, bool)> notices = [];

        CommandTask task = new(
            runner,
            new ProcessRequest("unzip -- 'notes.zip'", default, "/home"),
            "Extracting: notes.zip",
            (message, isError) => notices.Add((message, isError)),
            finished);

        return (task, runner, notices);
    }

    private static void RunToCompletion(CommandTask task)
    {
        IEnumerator<Unit> steps = task.Steps();

        while (steps.MoveNext())
        {
        }
    }

    [Fact]
    public void Steps_StartsTheProgramWithoutTakingTheTerminal()
    {
        (CommandTask task, FakeFileManager.RecordingProcessRunner runner, _) = Build();

        RunToCompletion(task);

        // `Run` is the path that suspends the interface. Nothing here may use it.
        Assert.Single(runner.Started);
        Assert.Equal("unzip -- 'notes.zip'", runner.Requests[0].Command);
        Assert.Equal("/home", runner.Requests[0].WorkingDirectory);
    }

    [Fact]
    public void Steps_YieldsBetweenPollsSoTheInterfaceKeepsItsTurn()
    {
        // The whole point: the queue gets control back between waits, which is what lets the
        // spinner turn and keys answer while the program runs.
        FakeFileManager.FakeBackgroundProcess process = new() { StepsBeforeExit = 3 };
        (CommandTask task, _, _) = Build(process);

        IEnumerator<Unit> steps = task.Steps();
        int yields = 0;

        while (steps.MoveNext())
        {
            yields++;
        }

        Assert.Equal(3, yields);
        Assert.Equal(4, process.Waits);
    }

    [Fact]
    public void Steps_ReportsWhateverTheProgramSaidOnStandardError()
    {
        // A wrong password or a corrupt archive says so there and nowhere else, and the old path
        // threw that away along with the terminal.
        FakeFileManager.FakeBackgroundProcess process =
            new() { Code = 1, StandardError = "  incorrect password\n" };
        (CommandTask task, _, List<(string Message, bool IsError)> notices) = Build(process);

        RunToCompletion(task);

        Assert.Equal(("incorrect password", true), notices[0]);
    }

    [Fact]
    public void Steps_ReportsAFailureThatSaidNothing()
    {
        FakeFileManager.FakeBackgroundProcess process = new() { Code = 9 };
        (CommandTask task, _, List<(string Message, bool IsError)> notices) = Build(process);

        RunToCompletion(task);

        Assert.Equal(("Extracting: notes.zip: exited with 9", true), notices[0]);
    }

    [Fact]
    public void Steps_SaysNothingWhenTheProgramSucceeded()
    {
        (CommandTask task, _, List<(string Message, bool IsError)> notices) = Build();

        RunToCompletion(task);

        Assert.Empty(notices);
    }

    [Fact]
    public void Steps_ReportsAProgramThatCouldNotBeStarted()
    {
        (CommandTask task, FakeFileManager.RecordingProcessRunner runner,
         List<(string Message, bool IsError)> notices) = Build();
        runner.BackgroundStartFails = true;

        RunToCompletion(task);

        Assert.Equal(("Extracting: notes.zip: could not start", true), notices[0]);
    }

    [Fact]
    public void Steps_RunsTheCompletionCallbackEvenWhenTheProgramFailed()
    {
        // The archive plugin reloads the directory here. A failure part-way through still leaves
        // files behind, so skipping the reload would show a stale listing.
        int calls = 0;
        FakeFileManager.FakeBackgroundProcess process = new() { Code = 2 };
        (CommandTask task, _, _) = Build(process, _ => calls++);

        RunToCompletion(task);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Steps_RunsTheCompletionCallbackWhenTheProgramNeverStarted()
    {
        int calls = 0;
        (CommandTask task, FakeFileManager.RecordingProcessRunner runner, _) =
            Build(finished: _ => calls++);
        runner.BackgroundStartFails = true;

        RunToCompletion(task);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Steps_MakesTheOutputAvailable()
    {
        FakeFileManager.FakeBackgroundProcess process = new() { StandardOutput = "one\ntwo\n" };
        (CommandTask task, _, _) = Build(process);

        RunToCompletion(task);

        Assert.Equal("one\ntwo\n", task.Output);
        Assert.Equal(0, task.ExitCode);
    }

    [Fact]
    public void Dispose_EndsAProgramThatIsStillRunning()
    {
        // Cancelling in the task view has to stop the work too, or it carries on invisibly with
        // nothing left to stop it.
        FakeFileManager.FakeBackgroundProcess process = new() { StepsBeforeExit = 100 };
        (CommandTask task, _, _) = Build(process);

        IEnumerator<Unit> steps = task.Steps();
        steps.MoveNext();
        task.Dispose();

        Assert.True(process.Killed);
        Assert.True(process.Disposed);
    }

    [Fact]
    public void CancellingTheQueuedTaskEndsTheProgram()
    {
        FakeFileManager.FakeBackgroundProcess process = new() { StepsBeforeExit = 100 };
        (CommandTask task, _, _) = Build(process);

        TaskQueue queue = new();
        QueuedTask queued = queue.Add(task);
        queue.Work(TimeSpan.Zero);
        queue.Cancel(queued);

        Assert.True(process.Killed);
    }

    [Fact]
    public void Description_IsWhatTheTaskViewShows()
    {
        (CommandTask task, _, _) = Build();

        Assert.Equal("Extracting: notes.zip", task.Description);

        // No percentage: an outside program does not say how far along it is, and inventing one
        // would be a lie the user would rely on.
        Assert.Null(task.Progress);
    }
}
