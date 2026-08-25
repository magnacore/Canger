// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;

namespace Canger.Core.Tests.Tasks;

public class TaskQueueTests
{
    /// <summary>A job that counts to a target, one step at a time.</summary>
    private sealed class CountingJob(string description, int target) : ILoadable
    {
        public string Description { get; } = description;

        public int Completed { get; private set; }

        public bool WasDisposed { get; private set; }

        public double? Progress => (double)Completed / target;

        public IEnumerator<Unit> Steps()
        {
            while (Completed < target)
            {
                Completed++;
                yield return Unit.Value;
            }
        }

        public void Dispose() => WasDisposed = true;
    }

    /// <summary>A job that fails partway through.</summary>
    private sealed class FailingJob : ILoadable
    {
        public string Description => "failing";

        public double? Progress => null;

        public IEnumerator<Unit> Steps()
        {
            yield return Unit.Value;
            throw new InvalidOperationException("something went wrong");
        }
    }

    /// <summary>Runs a queue to completion, one step at a time.</summary>
    private static void RunToCompletion(TaskQueue queue)
    {
        // A zero slice advances exactly one step, which keeps the test deterministic rather
        // than dependent on how fast the machine is.
        for (int i = 0; i < 10_000 && queue.HasWork; i++)
        {
            queue.Work(TimeSpan.Zero);
        }
    }

    [Fact]
    public void Add_QueuesAJobWithoutStartingIt()
    {
        TaskQueue queue = new();
        CountingJob job = new("count", 3);

        QueuedTask task = queue.Add(job);

        Assert.Equal(TaskState.Waiting, task.State);
        Assert.Equal(0, job.Completed);
        Assert.True(queue.HasWork);
    }

    [Fact]
    public void Work_AdvancesTheJobAtTheFront()
    {
        TaskQueue queue = new();
        CountingJob job = new("count", 3);
        queue.Add(job);

        queue.Work(TimeSpan.Zero);

        Assert.Equal(1, job.Completed);
    }

    [Fact]
    public void Work_RunsAJobToCompletionAndDisposesIt()
    {
        TaskQueue queue = new();
        CountingJob job = new("count", 3);
        QueuedTask task = queue.Add(job);

        RunToCompletion(queue);

        Assert.Equal(3, job.Completed);
        Assert.Equal(TaskState.Finished, task.State);
        Assert.True(job.WasDisposed);
        Assert.False(queue.HasWork);
    }

    [Fact]
    public void Work_RunsOnlyTheJobAtTheFront()
    {
        // Copying two large trees at once is slower than doing them in turn, and a visible
        // queue is easier to reason about than an unbounded pool.
        TaskQueue queue = new();
        CountingJob first = new("first", 2);
        CountingJob second = new("second", 2);
        queue.Add(first);
        queue.Add(second);

        queue.Work(TimeSpan.Zero);

        Assert.Equal(1, first.Completed);
        Assert.Equal(0, second.Completed);
    }

    [Fact]
    public void Work_MovesOnToTheNextJobWhenTheFrontOneFinishes()
    {
        TaskQueue queue = new();
        CountingJob first = new("first", 1);
        CountingJob second = new("second", 1);
        queue.Add(first);
        queue.Add(second);

        RunToCompletion(queue);

        Assert.Equal(1, first.Completed);
        Assert.Equal(1, second.Completed);
    }

    [Fact]
    public void Add_AtTheFrontJumpsTheQueue()
    {
        TaskQueue queue = new();
        CountingJob queued = new("queued", 2);
        CountingJob urgent = new("urgent", 2);
        queue.Add(queued);
        queue.Add(urgent, atFront: true);

        queue.Work(TimeSpan.Zero);

        Assert.Equal(1, urgent.Completed);
        Assert.Equal(0, queued.Completed);
    }

    [Fact]
    public void Work_ReturnsFalseWhenThereIsNothingToDo() =>
        Assert.False(new TaskQueue().Work(TimeSpan.Zero));

    [Fact]
    public void Pause_HoldsAJobAndLetsTheQueueMoveOn()
    {
        TaskQueue queue = new();
        CountingJob held = new("held", 5);
        CountingJob other = new("other", 5);
        QueuedTask task = queue.Add(held);
        queue.Add(other);

        queue.Pause(task);
        queue.Work(TimeSpan.Zero);

        Assert.Equal(0, held.Completed);
        Assert.Equal(1, other.Completed);
    }

    [Fact]
    public void Resume_LetsAHeldJobContinue()
    {
        TaskQueue queue = new();
        CountingJob job = new("job", 5);
        QueuedTask task = queue.Add(job);

        queue.Pause(task);
        queue.Resume(task);
        queue.Work(TimeSpan.Zero);

        Assert.Equal(1, job.Completed);
    }

    [Fact]
    public void Cancel_StopsAJobPartwayAndRemovesIt()
    {
        TaskQueue queue = new();
        CountingJob job = new("job", 100);
        QueuedTask task = queue.Add(job);
        queue.Work(TimeSpan.Zero);

        queue.Cancel(task);

        Assert.Equal(1, job.Completed);
        Assert.True(job.WasDisposed);
        Assert.Empty(queue.Tasks);
    }

    [Fact]
    public void Work_RecordsAFailureWithoutTakingTheQueueDown()
    {
        // One broken job must not stop everything else, or a single unreadable file would end
        // the whole session.
        TaskQueue queue = new();
        QueuedTask failing = queue.Add(new FailingJob());
        CountingJob healthy = new("healthy", 1);
        queue.Add(healthy);

        RunToCompletion(queue);

        Assert.Equal(TaskState.Failed, failing.State);
        Assert.IsType<InvalidOperationException>(failing.Error);
        Assert.Equal(1, healthy.Completed);
    }

    [Fact]
    public void Move_ReordersTheQueue()
    {
        TaskQueue queue = new();
        queue.Add(new CountingJob("first", 1));
        QueuedTask second = queue.Add(new CountingJob("second", 1));

        queue.Move(second, -1);

        Assert.Equal(["second", "first"], queue.Tasks.Select(t => t.Description));
    }

    [Fact]
    public void Move_ClampsAtTheEnds()
    {
        TaskQueue queue = new();
        QueuedTask first = queue.Add(new CountingJob("first", 1));
        queue.Add(new CountingJob("second", 1));

        queue.Move(first, -99);

        Assert.Equal(["first", "second"], queue.Tasks.Select(t => t.Description));
    }

    [Fact]
    public void RemoveCompleted_ClearsFinishedJobs()
    {
        TaskQueue queue = new();
        queue.Add(new CountingJob("done", 1));
        RunToCompletion(queue);

        Assert.Equal(1, queue.RemoveCompleted());
        Assert.Empty(queue.Tasks);
    }

    [Fact]
    public void CancelAll_StopsEverythingAndReleasesIt()
    {
        TaskQueue queue = new();
        CountingJob first = new("first", 100);
        CountingJob second = new("second", 100);
        queue.Add(first);
        queue.Add(second);

        queue.CancelAll();

        Assert.Empty(queue.Tasks);
        Assert.True(first.WasDisposed);
        Assert.True(second.WasDisposed);
    }

    [Fact]
    public void OverallProgress_AveragesTheOutstandingJobs()
    {
        TaskQueue queue = new();
        CountingJob first = new("first", 4);
        CountingJob second = new("second", 4);
        queue.Add(first);
        queue.Add(second);

        queue.Work(TimeSpan.Zero);
        queue.Work(TimeSpan.Zero);

        // The first job is halfway, the second has not started.
        Assert.Equal(0.25, queue.OverallProgress());
    }

    [Fact]
    public void OverallProgress_IsUnknownWhenNothingReportsIt() =>
        Assert.Null(new TaskQueue().OverallProgress());

    [Fact]
    public void Changed_IsRaisedWhenTheQueueIsModified()
    {
        TaskQueue queue = new();
        int changes = 0;
        queue.Changed += (_, _) => changes++;

        QueuedTask task = queue.Add(new CountingJob("job", 1));
        queue.Pause(task);
        queue.Resume(task);
        queue.Cancel(task);

        Assert.Equal(4, changes);
    }

    [Fact]
    public void Work_HonoursATimeSlice()
    {
        // The slice is what keeps input responsive while a long job runs.
        TaskQueue queue = new();
        CountingJob job = new("long", 100_000_000);
        queue.Add(job);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        queue.Work(TimeSpan.FromMilliseconds(30));
        stopwatch.Stop();

        Assert.True(job.Completed > 0, "the job should have advanced");
        Assert.False(job.WasDisposed);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 0, 500);
    }
}
