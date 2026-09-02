// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// Where a queued command's progress comes from.
/// </summary>
/// <remarks>
/// A program Canger did not write does not report itself, so a queued command showed a spinner and
/// nothing else. Some can be persuaded: tar echoes a byte count if asked, and any archiver's
/// output file can be watched growing. The distinction between the two is what separates a
/// percentage from a byte count.
/// </remarks>
public class CommandProgressTests
{
    [Fact]
    public void ReadsTheLastCountTheCommandPrinted()
    {
        MarkerProgress progress = new("canger-bytes:", total: 1000);

        progress.Update("canger-bytes:100\ncanger-bytes:400\n");

        Assert.Equal(400, progress.Completed);
    }

    [Fact]
    public void SaysNothingUntilTheCommandHasReported()
    {
        // A spinner until the first checkpoint, rather than a bar sitting at zero.
        MarkerProgress progress = new("canger-bytes:", total: 1000);

        progress.Update("tar: some warning about a file\n");

        Assert.Null(progress.Completed);
    }

    [Fact]
    public void IgnoresACountThatWouldGoBackwards()
    {
        // A checkpoint line interleaved with an error message can arrive torn, and a figure that
        // retreats is worse than one briefly stale.
        MarkerProgress progress = new("canger-bytes:", total: 1000);

        progress.Update("canger-bytes:400\n");
        progress.Update("canger-bytes:400\ncanger-bytes:2\n");

        Assert.Equal(400, progress.Completed);
    }

    [Fact]
    public void SurvivesAMarkerWithNoNumberAfterIt()
    {
        MarkerProgress progress = new("canger-bytes:", total: 1000);

        progress.Update("canger-bytes:900\ncanger-bytes:");

        Assert.Equal(900, progress.Completed);
    }

    [Fact]
    public void ReadsTheGrowingArchiveInstead()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/w")
            .AddFile("/w/out.tar.lz", new string('x', 120));

        GrowingFileProgress progress = new(fs, "/w/out.tar.lz");
        progress.Update(string.Empty);

        Assert.Equal(120, progress.Completed);
    }

    [Fact]
    public void OffersNoTotalForAFileItIsOnlyWatching()
    {
        // Deliberate. How large an archive ends up depends on a compression ratio nobody knows in
        // advance, so dividing by the size of the input would draw a bar that lies.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/w").AddFile("/w/out.7z");

        Assert.Null(new GrowingFileProgress(fs, "/w/out.7z").Total);
    }

    [Fact]
    public void SaysNothingWhileTheArchiveDoesNotExistYet()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/w");

        GrowingFileProgress progress = new(fs, "/w/not-yet.7z");
        progress.Update(string.Empty);

        Assert.Null(progress.Completed);
    }
}

/// <summary>
/// A queued command carrying its progress source through to the task view.
/// </summary>
/// <remarks>
/// The sources are tested above in isolation, which says nothing about whether the task ever asks
/// them. It did not: removing the call from <c>CommandTask.Steps</c> broke no test at all, which
/// is the same seam that has caught this project three times — the thing under test being correct
/// while nothing connects it.
/// </remarks>
public class CommandTaskProgressTests
{
    private static (CommandTask Task, FakeFileManager.FakeBackgroundProcess Process)
        Queued(ICommandProgress source, int stepsBeforeExit = 5)
    {
        FakeFileManager.RecordingProcessRunner runner = new();
        FakeFileManager.FakeBackgroundProcess process = new()
        {
            StepsBeforeExit = stepsBeforeExit,
        };

        runner.BackgroundResults["tar"] = process;

        CommandTask task = new(
            runner,
            new ProcessRequest("tar -cf out.tar.lz big/", default, "/w"),
            "Compressing: out.tar.lz",
            notify: null,
            finished: null,
            progress: source);

        return (task, process);
    }

    [Fact]
    public void TheTaskAsksItsSourceAsTheProgramRuns()
    {
        MarkerProgress source = new("canger-bytes:", total: 1000);
        (CommandTask task, FakeFileManager.FakeBackgroundProcess process) = Queued(source);

        IEnumerator<Unit> steps = task.Steps();

        Assert.True(steps.MoveNext());
        Assert.Null(task.Progress);

        process.StandardError = "canger-bytes:250\n";
        Assert.True(steps.MoveNext());
        Assert.Equal(0.25, task.Progress);

        process.StandardError += "canger-bytes:750\n";
        Assert.True(steps.MoveNext());
        Assert.Equal(0.75, task.Progress);
    }

    [Fact]
    public void TheFinalReadingIsTakenAfterTheProgramEnds()
    {
        // A command that finishes between two polls would otherwise be left showing whatever the
        // last slice happened to catch rather than where it actually got to.
        MarkerProgress source = new("canger-bytes:", total: 1000);
        (CommandTask task, FakeFileManager.FakeBackgroundProcess process) = Queued(source, 1);

        IEnumerator<Unit> steps = task.Steps();
        steps.MoveNext();

        process.StandardError = "canger-bytes:1000\n";
        while (steps.MoveNext())
        {
            // run it out
        }

        Assert.Equal(1.0, task.Progress);
    }

    [Fact]
    public void ATaskWithNoSourceStillShowsASpinner()
    {
        // The behaviour every other queued command keeps.
        FakeFileManager.RecordingProcessRunner runner = new();
        runner.BackgroundResults["tar"] = new FakeFileManager.FakeBackgroundProcess();

        CommandTask task = new(runner, new ProcessRequest("tar -cf a b", default, "/w"), "Packing");

        Assert.Null(task.Progress);
        Assert.Null(task.Transferred);
    }

    [Fact]
    public void BytesAreReportedEvenWithNoTotal()
    {
        // What the task view shows where a bar cannot be justified.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/w")
            .AddFile("/w/out.7z", new string('x', 4096));

        GrowingFileProgress source = new(fs, "/w/out.7z");
        (CommandTask task, _) = Queued(source);

        IEnumerator<Unit> steps = task.Steps();
        steps.MoveNext();
        steps.MoveNext();

        Assert.Null(task.Progress);
        Assert.Equal(4096, task.Transferred);
    }
}

/// <summary>
/// What a finished command reports.
/// </summary>
/// <remarks>
/// tar reports at intervals and says nothing about the tail after the last one, so a real archive
/// ended at 85% and stopped there — which reads as a job that stalled rather than one that
/// finished.
/// </remarks>
public class FinishedCommandProgressTests
{
    private sealed class Reported(long? total, long? completed) : ICommandProgress
    {
        public long? Total => total;

        public long? Completed => completed;

        public void Update(string standardError)
        {
            // The figures are given.
        }
    }

    private static CommandTask Run(int exitCode, ICommandProgress source)
    {
        FakeFileManager.RecordingProcessRunner runner = new();
        runner.BackgroundResults["tar"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 1,
            Code = exitCode,
        };

        CommandTask task = new(
            runner,
            new ProcessRequest("tar -cf out.tar.lz big/", default, "/w"),
            "Compressing: out.tar.lz",
            notify: null,
            finished: null,
            progress: source);

        IEnumerator<Unit> steps = task.Steps();
        while (steps.MoveNext())
        {
            // run it out
        }

        return task;
    }

    [Fact]
    public void ASuccessfulCommandEndsAtTheEnd()
    {
        Assert.Equal(1.0, Run(0, new Reported(total: 1000, completed: 850)).Progress);
    }

    [Fact]
    public void AFailedCommandKeepsItsLastHonestFigure()
    {
        // Claiming a failure got to the end would be a lie about work that did not happen.
        Assert.Equal(0.85, Run(2, new Reported(total: 1000, completed: 850)).Progress);
    }
}

/// <summary>
/// A command run by the queue rather than driven by hand.
/// </summary>
/// <remarks>
/// The tests above drive <c>Steps</c> directly, and that is not what happens: the queue disposes
/// the work the instant it finishes, which releases the process handle. Reading anything through
/// that handle afterwards finds nothing, and a completed archive sat at 94% for ever while every
/// unit test passed. These run the real queue.
/// </remarks>
public class QueuedCommandProgressTests
{
    private sealed class Reported(long? total, long? completed, string marker = "") : ICommandProgress
    {
        public long? Total => total;

        public long? Completed => completed;

        public bool Reports(string line) =>
            marker.Length > 0 && line.Contains(marker, StringComparison.Ordinal);

        public void Update(string standardError)
        {
            // The figures are given.
        }
    }

    private static (TaskQueue Queue, QueuedTask Task, List<string> Messages) Run(
        int exitCode, string standardError, ICommandProgress source)
    {
        FakeFileManager.RecordingProcessRunner runner = new();
        runner.BackgroundResults["tar"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 1,
            Code = exitCode,
            StandardError = standardError,
        };

        List<string> messages = [];
        TaskQueue queue = new();

        QueuedTask task = queue.Add(new CommandTask(
            runner,
            new ProcessRequest("tar -cf out.tar.lz big/", default, "/w"),
            "Compressing: out.tar.lz",
            (message, _) => messages.Add(message),
            finished: null,
            progress: source));

        while (!task.IsComplete)
        {
            queue.Work(TimeSpan.FromSeconds(1));
        }

        return (queue, task, messages);
    }

    [Fact]
    public void AFinishedArchiveReadsAsFinished()
    {
        // Through the queue, which disposes the work as it completes.
        (_, QueuedTask task, _) = Run(0, string.Empty, new Reported(total: 1000, completed: 850));

        Assert.Equal(1.0, task.Progress);
    }

    [Fact]
    public void AFailedArchiveKeepsItsLastHonestFigure()
    {
        (_, QueuedTask task, _) = Run(2, string.Empty, new Reported(total: 1000, completed: 850));

        Assert.Equal(0.85, task.Progress);
    }

    [Fact]
    public void ProgressReportingIsNotMistakenForAComplaint()
    {
        // Asking tar to say how far it has got makes it write to standard error, which is where
        // failures are read from. Without filtering, every archive ended by showing its own
        // checkpoints as an error.
        (_, _, List<string> messages) = Run(
            0,
            "canger-bytes:1024\ncanger-bytes:2048\n",
            new Reported(total: 4096, completed: 2048, marker: "canger-bytes:"));

        Assert.Empty(messages);
    }

    [Fact]
    public void ARealComplaintStillGetsThrough()
    {
        // Filtering the source's own lines must not silence the archiver.
        (_, _, List<string> messages) = Run(
            2,
            "canger-bytes:1024\ntar: big/secret: Permission denied\n",
            new Reported(total: 4096, completed: 1024, marker: "canger-bytes:"));

        Assert.Contains(messages, m => m.Contains("Permission denied", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("canger-bytes", StringComparison.Ordinal));
    }
}
