// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// The figures the queue reports for all of its work rather than for the job in hand.
/// </summary>
/// <remarks>
/// Two films pasted together used to show the second one's percentage restarting from nothing,
/// because the bar had only ever described whichever film was moving. It could not describe more
/// than that: a job awaiting its turn had no size, and could not be asked for one without also
/// starting it. Separating the sizing from the transfer is what makes these figures possible, so
/// most of what follows is about that separation holding.
/// </remarks>
public class QueueEstimateTests
{
    /// <summary>Builds a transfer of one file of a given size.</summary>
    private static CopyJob Transfer(InMemoryFileSystem fs, string name, long size)
    {
        fs.AddFileOfSize($"/src/{name}", size, DateTimeOffset.UnixEpoch);
        return new CopyJob(fs, [$"/src/{name}"], "/dest");
    }

    /// <summary>A filesystem with somewhere to put things.</summary>
    private static InMemoryFileSystem Filesystem() => new InMemoryFileSystem().AddDirectory("/dest");

    /// <summary>Sizes everything the queue can size, however many steps that takes.</summary>
    private static void SizeEverything(TaskQueue queue)
    {
        for (int i = 0; i < 10_000 && queue.Size(TimeSpan.FromMilliseconds(1)); i++)
        {
            // `Size` returns false once there is nothing left with a size to establish.
        }
    }

    /// <summary>A job that reports progress but cannot say how big it is.</summary>
    private sealed class OpaqueJob(int target) : ILoadable
    {
        public string Description => "unpacking";

        public int Completed { get; private set; }

        public double? Progress => (double)Completed / target;

        public IEnumerator<Unit> Steps()
        {
            while (Completed < target)
            {
                Completed++;
                yield return Unit.Value;
            }
        }
    }

    [Fact]
    public void SizingATransferMovesNoData()
    {
        // The whole arrangement rests on this. Sizing runs while another job is copying, so if
        // it wrote, renamed or deleted anything it would be doing so out of turn.
        InMemoryFileSystem fs = Filesystem();
        CopyJob job = Transfer(fs, "film.mp4", 4000);

        IEnumerator<Unit> sizing = job.SizingSteps();
        while (sizing.MoveNext())
        {
            Assert.False(fs.Exists("/dest/film.mp4"));
        }

        Assert.True(job.IsSized);
        Assert.Equal(4000, job.TotalBytes);
        Assert.Equal(4000, job.RemainingBytes);
        Assert.False(fs.Exists("/dest/film.mp4"));
    }

    [Fact]
    public void ATransferSizedInAdvanceDoesNotWalkItsSourcesTwice()
    {
        // Sizing and transferring share one walk, so a job the queue has already sized starts
        // moving data on its first step instead of measuring all over again. Without this the
        // saving would be nil and the tree would be read twice.
        InMemoryFileSystem fs = Filesystem();
        CopyJob job = Transfer(fs, "film.mp4", 4000);

        IEnumerator<Unit> sizing = job.SizingSteps();
        while (sizing.MoveNext())
        {
            // Sized ahead of time, as the queue does while something else runs.
        }

        IEnumerator<Unit> steps = job.Steps();
        steps.MoveNext();

        Assert.True(job.Progress.CompletedBytes > 0,
                    "the first step of an already-sized transfer should move data");
    }

    [Fact]
    public void ATransferNobodySizedStillSizesItself()
    {
        // Driven directly — by a test, or a plugin with its own loop — nothing has sized it in
        // advance, and it must still refuse to report a percentage against an unknown total.
        InMemoryFileSystem fs = Filesystem();
        CopyJob job = Transfer(fs, "film.mp4", 4000);

        IEnumerator<Unit> steps = job.Steps();
        steps.MoveNext();

        Assert.Equal(4000, job.Progress.TotalBytes);
    }

    [Fact]
    public void AQueuedTransferIsSizedBeforeItsTurnComes()
    {
        InMemoryFileSystem fs = Filesystem();
        CopyJob first = Transfer(fs, "one.mp4", 4000);
        CopyJob second = Transfer(fs, "two.mp4", 1000);

        TaskQueue queue = new();
        QueuedTask running = queue.Add(first);
        queue.Add(second);

        SizeEverything(queue);

        Assert.True(second.IsSized);
        Assert.False(running.IsComplete);
    }

    [Fact]
    public void ServingTheQueueSizesWhatIsWaiting()
    {
        // The share of the slice is not something a caller has to remember to ask for.
        InMemoryFileSystem fs = Filesystem();
        CopyJob first = Transfer(fs, "one.mp4", 1000);
        CopyJob second = Transfer(fs, "two.mp4", 1000);

        TaskQueue queue = new();
        queue.Add(first);
        queue.Add(second);

        for (int i = 0; i < 100 && !second.IsSized; i++)
        {
            queue.Work();
        }

        Assert.True(second.IsSized);
    }

    [Fact]
    public void AZeroSliceSizesNothing()
    {
        // A zero slice means "advance exactly one step", which callers rely on to step a job
        // deliberately. Spending part of it elsewhere would make that untrue.
        InMemoryFileSystem fs = Filesystem();
        CopyJob first = Transfer(fs, "one.mp4", 1000);
        CopyJob second = Transfer(fs, "two.mp4", 1000);

        TaskQueue queue = new();
        queue.Add(first);
        queue.Add(second);

        queue.Work(TimeSpan.Zero);

        Assert.False(second.IsSized);
    }

    [Fact]
    public void TheOverallFigureIsWeightedBySizeOnceEverythingHasOne()
    {
        // A four gigabyte film and a hundred megabyte one are not half the work each. The
        // averaged figure said they were.
        InMemoryFileSystem fs = Filesystem();
        CopyJob large = Transfer(fs, "large.mp4", 9000);
        CopyJob small = Transfer(fs, "small.mp4", 1000);

        TaskQueue queue = new();
        queue.Add(large);
        queue.Add(small);
        SizeEverything(queue);

        // The small one finished; the large one has not started.
        while (small.Progress.CompletedBytes < 1000)
        {
            small.Steps().MoveNext();
        }

        double? progress = queue.OverallProgress();

        // A tenth of the bytes, not half of the jobs.
        Assert.NotNull(progress);
        Assert.Equal(0.1, progress.Value, 3);
    }

    [Fact]
    public void TheOverallFigureFallsBackToAnAverageWhileSomethingHasNoSize()
    {
        // An archive being unpacked cannot say how big it is. Leaving it out of a byte figure
        // would report the queue as further along than it is, so the older, rougher figure is
        // used until everything outstanding can be counted.
        InMemoryFileSystem fs = Filesystem();
        CopyJob transfer = Transfer(fs, "film.mp4", 1000);

        TaskQueue queue = new();
        queue.Add(transfer);
        queue.Add(new OpaqueJob(4));
        SizeEverything(queue);

        while (transfer.Progress.CompletedBytes < 1000)
        {
            transfer.Steps().MoveNext();
        }

        // Averaged: one job wholly done, one not started.
        Assert.Equal(0.5, queue.OverallProgress()!.Value, 3);
    }

    [Fact]
    public void TheFigureNeverGoesBackwardsAsJobsFinish()
    {
        // The complaint this was all for: the bar restarted from nothing when the first film
        // finished. Nothing here is allowed to fall.
        InMemoryFileSystem fs = Filesystem();
        TaskQueue queue = new();
        queue.Add(Transfer(fs, "one.mp4", 4000));
        queue.Add(Transfer(fs, "two.mp4", 4000));

        double highest = 0;

        for (int i = 0; i < 10_000 && queue.HasWork; i++)
        {
            SizeEverything(queue);
            queue.Work(TimeSpan.Zero);

            if (queue.OverallProgress() is { } progress)
            {
                Assert.True(progress >= highest,
                            $"the figure fell from {highest:F3} to {progress:F3}");
                highest = progress;
            }
        }

        Assert.Equal(1, highest, 3);
    }

    [Fact]
    public void TheLineNamesWhichJobOfHowMany()
    {
        InMemoryFileSystem fs = Filesystem();
        TaskQueue queue = new();
        queue.Add(Transfer(fs, "one.mp4", 4000));
        queue.Add(Transfer(fs, "two.mp4", 4000));
        SizeEverything(queue);

        string line = queue.Summary()!.Describe();

        Assert.Contains("copying one.mp4 (1 of 2)", line, StringComparison.Ordinal);
        Assert.Contains("8 k", line, StringComparison.Ordinal);
    }

    [Fact]
    public void OneJobKeepsItsOwnLineUnchanged()
    {
        // Nothing to measure it against, so "(1 of 1)" would be noise and the job's own figures
        // are already the queue's figures.
        InMemoryFileSystem fs = Filesystem();
        CopyJob only = Transfer(fs, "film.mp4", 4000);

        TaskQueue queue = new();
        queue.Add(only);
        SizeEverything(queue);

        Assert.Equal(only.Description, queue.Summary()!.Describe());
    }

    [Fact]
    public void AQueueWithSomethingUnsizedSaysSo()
    {
        InMemoryFileSystem fs = Filesystem();
        TaskQueue queue = new();
        queue.Add(Transfer(fs, "film.mp4", 4000));
        queue.Add(new OpaqueJob(4));
        SizeEverything(queue);

        QueueSummary summary = queue.Summary()!;

        Assert.True(summary.Partial);
        Assert.EndsWith("+", summary.Describe(), StringComparison.Ordinal);
    }

    /// <summary>A sized job whose reported throughput the test controls.</summary>
    private sealed class PacedJob(long total, double? rate) : ILoadable, ISizedWork
    {
        public string Description => $"{Subject}: paced";

        public string Subject => "copying paced";

        public double? Progress => 0;

        public bool IsSized => true;

        public long? TotalBytes => total;

        public long? RemainingBytes => total;

        public double? BytesPerSecond => rate;

        public IEnumerator<Unit> SizingSteps() => Enumerable.Empty<Unit>().GetEnumerator();

        public IEnumerator<Unit> Steps()
        {
            yield return Unit.Value;
        }
    }

    [Fact]
    public void TheEstimateSurvivesOneJobHandingOverToTheNext()
    {
        // A job that has just started has moved nothing and so has no rate of its own. Without
        // the last measurement to fall back on, the estimate vanished for a frame or two every
        // time a file finished — which reads as the figure having broken rather than paused.
        TaskQueue queue = new();
        QueuedTask measured = queue.Add(new PacedJob(1000, 2_000_000));
        queue.Add(new PacedJob(1000, null));

        queue.Work(TimeSpan.Zero);
        queue.Cancel(measured);

        QueueSummary summary = queue.Summary()!;

        Assert.Equal(2_000_000, summary.BytesPerSecond);
        Assert.NotNull(summary.Estimate);
    }

    [Fact]
    public void ANewRunOfWorkDoesNotInheritTheOldOnesSpeed()
    {
        // The next thing pasted may well be going somewhere else entirely, and a remembered
        // solid-state speed would badly flatter a transfer to a memory stick.
        TaskQueue queue = new();
        QueuedTask task = queue.Add(new PacedJob(1000, 2_000_000));

        queue.Work(TimeSpan.Zero);
        queue.Cancel(task);
        queue.RemoveCompleted();

        queue.Add(new PacedJob(1000, null));

        Assert.Null(queue.Summary()!.BytesPerSecond);
    }

    [Fact]
    public void TheEstimateCoversWhatIsQueuedAndNotOnlyWhatIsMoving()
    {
        // One transfer of two files' worth of bytes, and a second of the same, at a known rate:
        // the estimate has to speak for both.
        InMemoryFileSystem fs = Filesystem();
        TaskQueue queue = new();
        CopyJob first = Transfer(fs, "one.mp4", 4000);
        queue.Add(first);
        queue.Add(Transfer(fs, "two.mp4", 4000));
        SizeEverything(queue);

        Assert.Equal(8000, queue.Summary()!.TotalBytes);
        Assert.Equal(0, queue.Summary()!.CompletedBytes);
    }
}
