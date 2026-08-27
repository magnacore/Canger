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
}
