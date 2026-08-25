// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// A job that is only waiting on something outside itself, and what the queue does about it.
/// </summary>
/// <remarks>
/// <para>
/// The queue is pumped from the same loop that reads the keyboard, so where the waiting happens
/// decides how quickly a keystroke is answered. Measured while unpacking an archive: waiting
/// inside the job cost 40 ms at the ninetieth percentile against 1 ms idle, which is the
/// sluggishness that prompted this. Waiting in the loop instead — where a keypress ends the wait
/// — brought it to 1.4 ms at the worst.
/// </para>
/// <para>
/// Both halves are needed, and getting only the first is worse than neither: a step that no
/// longer blocks will otherwise be asked the same question for the whole 30 ms slice, which
/// measured a flat 32 ms latency and burned a core doing it.
/// </para>
/// </remarks>
public class IdleTaskTests
{
    /// <summary>A job that never finishes and never has anything of its own to do.</summary>
    private sealed class Waiting(TimeSpan idle) : ILoadable
    {
        public int StepCount { get; private set; }

        public string Description => "waiting";

        public double? Progress => null;

        public TimeSpan Idle => idle;

        public IEnumerator<Unit> Steps()
        {
            while (true)
            {
                StepCount++;
                yield return Unit.Value;
            }
        }
    }

    [Fact]
    public void Work_TakesOneStepFromAnIdleJobRatherThanSpinningTheSlice()
    {
        Waiting work = new(TimeSpan.FromMilliseconds(30));
        TaskQueue queue = new();
        queue.Add(work);

        queue.Work();

        Assert.Equal(1, work.StepCount);
    }

    [Fact]
    public void Work_KeepsSteppingAJobThatHasWorkOfItsOwn()
    {
        // The default. A copy is not waiting on anything, so it should use its whole slice.
        Waiting work = new(TimeSpan.Zero);
        TaskQueue queue = new();
        queue.Add(work);

        queue.Work(TimeSpan.FromMilliseconds(20));

        Assert.True(work.StepCount > 1,
                    $"expected more than one step in a 20ms slice, got {work.StepCount}");
    }

    [Fact]
    public void IdleDelay_IsWhatTheRunningJobAsksFor()
    {
        TaskQueue queue = new();
        queue.Add(new Waiting(TimeSpan.FromMilliseconds(30)));

        Assert.Equal(TimeSpan.FromMilliseconds(30), queue.IdleDelay);
    }

    [Fact]
    public void IdleDelay_IsZeroForAJobWithWorkOfItsOwn()
    {
        TaskQueue queue = new();
        queue.Add(new Waiting(TimeSpan.Zero));

        Assert.Equal(TimeSpan.Zero, queue.IdleDelay);
    }

    [Fact]
    public void IdleDelay_IsZeroWithNothingToDo()
    {
        // The main loop only consults it while `HasWork`, but a stale delay here would be a trap
        // for the next caller.
        Assert.Equal(TimeSpan.Zero, new TaskQueue().IdleDelay);
    }

    [Fact]
    public void IdleDelay_IgnoresAPausedJob()
    {
        TaskQueue queue = new();
        QueuedTask paused = queue.Add(new Waiting(TimeSpan.FromMilliseconds(30)));
        queue.Add(new Waiting(TimeSpan.Zero));

        queue.Pause(paused);

        Assert.Equal(TimeSpan.Zero, queue.IdleDelay);
    }

    [Fact]
    public void IdleDelay_DefaultsToZeroForAJobThatSaysNothing()
    {
        // `Idle` is a default interface member, so every existing job — copies, moves, directory
        // scans — keeps the old behaviour without being touched.
        TaskQueue queue = new();
        queue.Add(new Plain());

        Assert.Equal(TimeSpan.Zero, queue.IdleDelay);
    }

    /// <summary>A job written before <see cref="ILoadable.Idle"/> existed.</summary>
    private sealed class Plain : ILoadable
    {
        public string Description => "plain";

        public double? Progress => null;

        public IEnumerator<Unit> Steps()
        {
            yield return Unit.Value;
        }
    }
}
