// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// What the user is told once a run of background work has stopped.
/// </summary>
/// <remarks>
/// There is only ever one message on the status bar. Each finished job used to be reported on its
/// own and each report replaced the last, so what survived was whatever finished last — and a
/// transfer that had lost a file could be reported and then unreported within the same frame by
/// a clean one beside it.
/// </remarks>
public class FinishedWorkTests
{
    /// <summary>Runs a transfer to completion and hands back the queued job.</summary>
    private static QueuedTask Run(IReadOnlyList<string> sources, InMemoryFileSystem fs)
    {
        TaskQueue queue = new();
        QueuedTask task = queue.Add(new CopyJob(fs, sources, "/dest"));

        for (int i = 0; i < 10_000 && queue.HasWork; i++)
        {
            queue.Work(TimeSpan.Zero);
        }

        return task;
    }

    /// <summary>A filesystem holding <paramref name="files"/> readable files.</summary>
    private static InMemoryFileSystem Filesystem(int files)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/dest");
        for (int i = 0; i < files; i++)
        {
            fs.AddFileOfSize($"/src/f{i}.bin", 100, DateTimeOffset.UnixEpoch);
        }

        return fs;
    }

    [Fact]
    public void AProblemIsNotDrownedOutByAJobThatWentWell()
    {
        // The reason this is worth fixing at all. Nothing is lost but the telling, and the whole
        // purpose of the notice is that a file which did not arrive should be noticed.
        InMemoryFileSystem fs = Filesystem(2);
        QueuedTask clean = Run(["/src/f0.bin"], fs);
        QueuedTask broken = Run(["/src/missing.bin"], fs);

        (string Message, bool IsError)? report = FinishedWork.Describe([clean, broken]);

        Assert.NotNull(report);
        Assert.True(report.Value.IsError);
        Assert.Contains("missing.bin", report.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOrderTheJobsFinishedInDoesNotDecideWhatIsSaid()
    {
        InMemoryFileSystem fs = Filesystem(2);
        QueuedTask clean = Run(["/src/f0.bin"], fs);
        QueuedTask broken = Run(["/src/missing.bin"], fs);

        Assert.True(FinishedWork.Describe([broken, clean])!.Value.IsError);
        Assert.True(FinishedWork.Describe([clean, broken])!.Value.IsError);
    }

    [Fact]
    public void SeveralProblemsAreCountedAndOneIsShown()
    {
        InMemoryFileSystem fs = Filesystem(1);
        QueuedTask first = Run(["/src/gone.bin"], fs);
        QueuedTask second = Run(["/src/also-gone.bin"], fs);

        string message = FinishedWork.Describe([first, second])!.Value.Message;

        Assert.StartsWith("2 problems, first: ", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFilesFromEveryTransferAreCountedTogether()
    {
        // "done: 1 files" was what two finished transfers of two files each reported, because
        // only the last of them was ever described.
        InMemoryFileSystem fs = Filesystem(4);
        QueuedTask first = Run(["/src/f0.bin", "/src/f1.bin"], fs);
        QueuedTask second = Run(["/src/f2.bin", "/src/f3.bin"], fs);

        Assert.Equal("done: 4 files", FinishedWork.Describe([first, second])!.Value.Message);
    }

    [Fact]
    public void OneFileIsSaidInTheSingular()
    {
        InMemoryFileSystem fs = Filesystem(1);

        Assert.Equal("done: 1 file",
                     FinishedWork.Describe([Run(["/src/f0.bin"], fs)])!.Value.Message);
    }

    [Fact]
    public void AStoppedTransferIsNotCalledDone()
    {
        // The files already moved are still there, so a user who pressed abort should be told
        // what got through rather than congratulated.
        InMemoryFileSystem fs = Filesystem(3);
        CopyJob job = new(fs, ["/src/f0.bin", "/src/f1.bin"], "/dest");
        TaskQueue queue = new();
        QueuedTask task = queue.Add(job);

        // Stopped partway, with one file through and one not — a fixed number of steps ran the
        // whole transfer instead, and cancelling a job that has already finished does nothing.
        for (int i = 0; i < 1000 && job.Progress.CompletedFiles == 0; i++)
        {
            queue.Work(TimeSpan.Zero);
        }

        Assert.False(task.IsComplete, "the transfer should still have a file to go");
        queue.Cancel(task);

        (string Message, bool IsError)? report = FinishedWork.Describe([task]);

        Assert.NotNull(report);
        Assert.StartsWith("stopped: ", report.Value.Message, StringComparison.Ordinal);
        Assert.False(report.Value.IsError);
    }

    [Fact]
    public void WorkThatMovedNoFilesSaysNothing()
    {
        // An archive unpacking or an external command finishing is not a transfer, and has
        // already said whatever it had to say.
        Assert.Null(FinishedWork.Describe([]));
    }
}
