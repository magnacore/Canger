// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// Working out a total that is not known in advance.
/// </summary>
/// <remarks>
/// Compressing a folder showed <c>109 M</c> and no bar: it knew how far it had got and not how far
/// there was to go. A folder's size means walking it, which nothing should do on the spot — but
/// the task queue already measures a copy while the copy waits, in three-millisecond slices, and
/// an archive can be the same kind of job.
/// </remarks>
public class MeasuredProgressTests
{
    private sealed class Counted(long? completed) : ICommandProgress
    {
        public long? Total => null;

        public long? Completed => completed;

        public void Update(string standardError)
        {
            // The figure is given.
        }
    }

    private static InMemoryFileSystem Tree() =>
        new InMemoryFileSystem()
            .AddDirectory("/w")
            .AddDirectory("/w/folder")
            .AddDirectory("/w/folder/inner")
            .AddFile("/w/folder/a.bin", new string('x', 100))
            .AddFile("/w/folder/inner/b.bin", new string('x', 250))
            .AddFile("/w/loose.bin", new string('x', 40));

    [Fact]
    public void MeasuresEverythingItWasGiven()
    {
        MeasuredProgress progress =
            new(new Counted(0), Tree(), ["/w/folder", "/w/loose.bin"]);

        IEnumerator<Unit> walk = progress.Measure();
        while (walk.MoveNext())
        {
            // a slice per file
        }

        Assert.Equal(390, progress.Total);
        Assert.True(progress.IsMeasured);
    }

    [Fact]
    public void WithholdsTheTotalUntilTheWalkIsDone()
    {
        // A total still growing would make the percentage fall as the measuring caught up, and a
        // bar that goes backwards reads as a fault — the mistake OverallProgress was reverted for.
        MeasuredProgress progress = new(new Counted(0), Tree(), ["/w/folder"]);

        IEnumerator<Unit> walk = progress.Measure();
        walk.MoveNext();

        Assert.Null(progress.Total);
        Assert.False(progress.IsMeasured);
    }

    [Fact]
    public void HandsBackControlBetweenFiles()
    {
        // What lets the queue spend three milliseconds at a time on it rather than stopping the
        // interface for as long as the walk takes.
        MeasuredProgress progress = new(new Counted(0), Tree(), ["/w/folder"]);

        int steps = 0;
        IEnumerator<Unit> walk = progress.Measure();
        while (walk.MoveNext())
        {
            steps++;
        }

        Assert.Equal(2, steps);
    }

    [Fact]
    public void PassesTheCountThroughToWhateverIsCounting()
    {
        MeasuredProgress progress = new(new Counted(64), Tree(), ["/w/loose.bin"]);

        Assert.Equal(64, progress.Completed);
    }

    [Fact]
    public void TheQueueMeasuresTheJobAndThenTheBarAppears()
    {
        // End to end through the real queue, which is where the sizing actually happens.
        FakeFileManager.RecordingProcessRunner runner = new();
        runner.BackgroundResults["tar"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 40,
            StandardError = "canger-bytes:195\n",
        };

        MeasuredProgress progress = new(
            new MarkerProgress("canger-bytes:", total: null), Tree(), ["/w/folder"]);

        TaskQueue queue = new();
        QueuedTask task = queue.Add(new CommandTask(
            runner,
            new ProcessRequest("tar -cf out.tar.lz folder", default, "/w"),
            "Compressing: out.tar.lz",
            notify: null,
            finished: null,
            progress: progress));

        Assert.Null(task.Progress);

        // A few slices: the queue sizes the job as well as running it.
        for (int i = 0; i < 20 && task.Progress is null; i++)
        {
            queue.Work(TimeSpan.FromMilliseconds(30));
        }

        Assert.Equal(350, progress.Total);
        Assert.Equal(195d / 350, task.Progress!.Value, 3);
    }
}
