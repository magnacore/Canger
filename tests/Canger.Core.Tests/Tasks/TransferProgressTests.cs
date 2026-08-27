// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// What the status bar and the task view are told while transfers are running.
/// </summary>
public class TransferProgressTests
{
    /// <summary>A copy of <paramref name="files"/> files into <paramref name="destination"/>.</summary>
    private static CopyJob Job(InMemoryFileSystem fs, string source, string destination, int files)
    {
        fs.AddDirectory(destination);
        for (int i = 0; i < files; i++)
        {
            fs.AddFileOfSize($"{source}/f{i}.bin", 100_000, DateTimeOffset.UnixEpoch);
        }

        return new CopyJob(fs, [.. Enumerable.Range(0, files).Select(i => $"{source}/f{i}.bin")],
                           destination);
    }

    [Fact]
    public void ACopyReportsProgressBetweenNothingAndDone()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/dest");
        for (int i = 0; i < 8; i++)
        {
            fs.AddFileOfSize($"/src/f{i}.bin", 100_000, DateTimeOffset.UnixEpoch);
        }

        CopyJob job = new(fs, [.. Enumerable.Range(0, 8).Select(i => $"/src/f{i}.bin")], "/dest");

        TaskQueue queue = new();
        queue.Add(job);

        List<double?> seen = [];
        for (int i = 0; i < 200 && queue.HasWork; i++)
        {
            queue.Work(TimeSpan.Zero);
            seen.Add(queue.OverallProgress());
        }

        Assert.Contains(seen, p => p is > 0 and < 1);
    }

    [Fact]
    public void TwoTransfersBothStayInTheQueue()
    {
        // Started one after the other, both are listed until each finishes — which is what the
        // task view shows. The queue runs them one at a time, as ranger's loader does.
        InMemoryFileSystem fs = new();
        TaskQueue queue = new();
        queue.Add(Job(fs, "/a", "/dest-a", 6));
        queue.Add(Job(fs, "/b", "/dest-b", 6));

        queue.Work(TimeSpan.Zero);

        Assert.Equal(2, queue.Tasks.Count(t => !t.IsComplete));
    }

    [Fact]
    public void TheStatusBarIsToldTheAverageOfWhatIsRunning()
    {
        // Ranger averages across the queue — `sum(states) / len(states)`
        // (gui/widgets/statusbar.py:334-338) — so a second transfer starting halves the bar
        // rather than restarting it.
        InMemoryFileSystem fs = new();
        TaskQueue queue = new();
        queue.Add(Job(fs, "/a", "/dest-a", 6));

        // Step until the first is genuinely part-way, not finished: a completed task drops out
        // of the average altogether.
        while (queue.HasWork && queue.OverallProgress() is not ( > 0.2 and < 0.9))
        {
            queue.Work(TimeSpan.Zero);
        }

        double? alone = queue.OverallProgress();
        queue.Add(Job(fs, "/b", "/dest-b", 6));
        double? together = queue.OverallProgress();

        Assert.NotNull(alone);
        Assert.NotNull(together);
        Assert.True(together < alone,
                    $"a second transfer should pull the average down: {alone} -> {together}");
    }

    [Fact]
    public void TheRunningTransferIsStillNamedAfterAnotherFinishesAheadOfIt()
    {
        // The reported sequence. A second paste goes to the front and runs; when it finishes the
        // first is picked up again — but the list still begins with the completed one, and
        // reading the list's head said "nothing is running" while a copy plainly was. The
        // description and its time remaining vanished from the status bar at that moment.
        InMemoryFileSystem fs = new();
        TaskQueue queue = new();

        QueuedTask first = queue.Add(Job(fs, "/a", "/dest-a", 8));
        QueuedTask second = queue.Add(Job(fs, "/b", "/dest-b", 2), atFront: true);

        // Run until the one that jumped the queue is done, leaving the first part-finished.
        for (int i = 0; i < 400 && !second.IsComplete; i++)
        {
            queue.Work(TimeSpan.Zero);
        }

        Assert.True(second.IsComplete, "the second transfer never finished");
        Assert.False(first.IsComplete, "the first should still have work left");

        Assert.Same(first, queue.Current);
        Assert.NotNull(queue.Current?.Description);
    }

    [Fact]
    public void NothingIsNamedWhenNothingIsRunning()
    {
        InMemoryFileSystem fs = new();
        TaskQueue queue = new();
        queue.Add(Job(fs, "/a", "/dest-a", 2));

        while (queue.HasWork)
        {
            queue.Work(TimeSpan.Zero);
        }

        Assert.Null(queue.Current);
    }
}
