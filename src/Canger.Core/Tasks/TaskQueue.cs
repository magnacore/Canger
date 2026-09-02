// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;

namespace Canger.Core.Tasks;

/// <summary>What is happening to a queued job.</summary>
public enum TaskState
{
    /// <summary>Waiting for its turn.</summary>
    Waiting,

    /// <summary>Currently being advanced.</summary>
    Running,

    /// <summary>Held, and will not advance until resumed.</summary>
    Paused,

    /// <summary>Ran to completion.</summary>
    Finished,

    /// <summary>Stopped before finishing.</summary>
    Cancelled,

    /// <summary>Stopped by an error.</summary>
    Failed,
}

/// <summary>A job in the queue, with the state the task view displays.</summary>
public sealed class QueuedTask(ILoadable work)
{
    private IEnumerator<Unit>? _steps;
    private IEnumerator<Unit>? _sizingSteps;

    /// <summary>The job itself.</summary>
    public ILoadable Work { get; } = work ?? throw new ArgumentNullException(nameof(work));

    /// <summary>What is happening to it.</summary>
    public TaskState State { get; internal set; } = TaskState.Waiting;

    /// <summary>Why it failed, when it did.</summary>
    public Exception? Error { get; internal set; }

    /// <summary>What the task view calls it.</summary>
    public string Description => Work.Description;

    /// <summary>How far along it is, from 0 to 1, or <see langword="null"/> when unknown.</summary>
    public double? Progress => Work.Progress;

    /// <summary>Bytes moved so far, for work that reports them without a total.</summary>
    /// <remarks>
    /// What the task view shows where there is no percentage to justify a bar — an archiver that
    /// can be watched writing but cannot say how much is left.
    /// </remarks>
    public long? Transferred => (Work as IReportsBytes)?.Transferred;

    /// <summary>Whether it will not be advanced again.</summary>
    public bool IsComplete =>
        State is TaskState.Finished or TaskState.Cancelled or TaskState.Failed;

    /// <summary>The step iterator, created on first use so a queued job does nothing until run.</summary>
    internal IEnumerator<Unit> Steps => _steps ??= Work.Steps();

    /// <summary>The job as work the queue can size ahead of time, when it is any.</summary>
    internal ISizedWork? Sized => Work as ISizedWork;

    /// <summary>
    /// The sizing iterator, or <see langword="null"/> for a job that cannot be sized.
    /// </summary>
    /// <remarks>
    /// Held here as well as by the job so that the queue drives one walk and not several, even
    /// if an implementation forgets to return the same iterator every time.
    /// </remarks>
    internal IEnumerator<Unit>? SizingSteps =>
        Sized is { } sized ? _sizingSteps ??= sized.SizingSteps() : null;

    /// <summary>
    /// Whether the sizing walk has stopped, however it stopped.
    /// </summary>
    /// <remarks>
    /// Distinct from having a size. A walk that ran out — because the job was cancelled, or the
    /// tree could not be read — leaves the job unsized, and without this the queue would ask it
    /// for another step forever.
    /// </remarks>
    internal bool SizingExhausted { get; set; }
}

/// <summary>
/// Runs queued jobs a slice at a time, so that background work never blocks the interface.
/// </summary>
/// <remarks>
/// <para>
/// The main loop calls <see cref="Work"/> once per frame with a time budget. The queue advances
/// the job at the front for that long and then returns, whether or not the job has finished, so
/// the next keystroke is handled promptly however much work is outstanding.
/// </para>
/// <para>
/// Only the front job advances. That is deliberate: copying two large trees at once is slower
/// than copying them one after the other, and a queue the user can see and reorder is easier to
/// reason about than an unbounded pool. Jobs that genuinely parallelise do so inside a step.
/// </para>
/// <para>
/// Sizing is the one exception, and only because it is not the work. Establishing how big a
/// queued job is reads metadata and moves no data, so the reason for serving one job at a time
/// does not apply to it: two walks do not halve each other's throughput the way two copies do.
/// A small share of each slice therefore goes to sizing whatever is waiting, which is what lets
/// the queue report a figure for all of its work instead of only for the part in progress.
/// Sizing does not appear in the task view. Putting it there would mean a sizing job at the
/// front of the queue taking every slice until it finished, stopping the copy behind it dead —
/// a worse trade than the better number is worth.
/// </para>
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
                 Justification = "This is a queue of work in the domain sense, and the task " +
                                 "view presents it to the user as one. Any other name would " +
                                 "describe it less accurately.")]
public sealed class TaskQueue
{
    private readonly List<QueuedTask> _tasks = [];

    /// <summary>How long a single call to <see cref="Work"/> may spend advancing a job.</summary>
    /// <remarks>
    /// Ranger uses the same 30 milliseconds (<c>core/loader.py:332</c>). It is short enough that
    /// input still feels immediate and long enough that a job makes real progress each frame.
    /// </remarks>
    public static readonly TimeSpan DefaultSlice = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// The largest share of a slice that may go to sizing queued jobs.
    /// </summary>
    /// <remarks>
    /// Three milliseconds of a thirty millisecond slice — a tenth, and never more, so that a
    /// caller passing a shorter slice does not find most of it spent on arithmetic. Small as it
    /// is, it amounts to roughly a fifth of a second of walking per second of real time, which
    /// sizes a very large tree in a few seconds while the copy in front of it runs on.
    /// </remarks>
    public static readonly TimeSpan MaximumSizingSlice = TimeSpan.FromMilliseconds(3);

    /// <summary>The spinner's frames, in order.</summary>
    private static readonly char[] ThrobberChars = ['/', '-', '\\', '|'];

    /// <summary>What is shown in place of the spinner when the running task is paused.</summary>
    private const char ThrobberPaused = '#';

    private int _throbber;

    /// <summary>
    /// The last throughput any job reported, kept for the moment one job hands over to the next.
    /// </summary>
    /// <remarks>
    /// A job that has just started has moved nothing yet, so it has no rate and the estimate for
    /// the whole queue would vanish for a frame or two each time a file finished — a figure that
    /// blinks out is read as a figure that broke. The most recent measurement of the disc is a
    /// better answer than none, and it is replaced by the new job's own within a tenth of a
    /// second. Forgotten when the queue empties, since the next thing pasted may well be going
    /// somewhere else entirely.
    /// </remarks>
    private double? _lastObservedRate;

    /// <summary>The queued jobs, in the order they will run.</summary>
    public IReadOnlyList<QueuedTask> Tasks => _tasks;

    /// <summary>The job being worked on, or <see langword="null"/> when nothing is.</summary>
    /// <remarks>
    /// The one the queue would serve next, not simply the first in the list. Those differ as soon
    /// as anything finishes out of order: a second paste goes to the front and runs, and when it
    /// finishes the first is picked up again — but the list still begins with the completed one.
    /// Reading the list's head then said "nothing is running" while a copy was plainly running,
    /// which took the description and its time remaining out of the status bar and left
    /// <c>abort</c> with nothing to stop.
    /// </remarks>
    public QueuedTask? Current => NextRunnable();

    /// <summary>
    /// How long the main loop may wait for input before the queue wants another turn.
    /// </summary>
    /// <remarks>
    /// Zero when the running job has work of its own, so the loop comes straight back to it.
    /// Longer when it is only waiting on something outside — an external command — in which case
    /// the loop should spend the time watching the keyboard rather than spinning.
    /// </remarks>
    public TimeSpan IdleDelay => NextRunnable()?.Work.Idle ?? TimeSpan.Zero;

    /// <summary>
    /// The spinner shown while work is going on, one character wide.
    /// </summary>
    /// <remarks>
    /// Turns once per slice, so its speed is a rough indication that the queue is being served
    /// rather than a decoration. A paused task shows <c>#</c> instead, which is the one state a
    /// spinner cannot express — a stopped spinner and a slow one look the same.
    ///
    /// Ranger's, characters and all (<c>core/loader.py:333-351</c>).
    /// </remarks>
    public char Throbber =>
        // Paused when there is work left and none of it is runnable — which is not the same
        // question `Current` answers, since that skips a paused task to find one that can run.
        Current is null && _tasks.Exists(t => t is { IsComplete: false, State: TaskState.Paused })
            ? ThrobberPaused
            : ThrobberChars[_throbber];

    /// <summary>Advances the spinner.</summary>
    private void Rotate() => _throbber = (_throbber + 1) % ThrobberChars.Length;

    /// <summary>Whether anything is left to do.</summary>
    public bool HasWork => _tasks.Exists(t => !t.IsComplete && t.State != TaskState.Paused);

    /// <summary>Raised whenever the queue changes, so the task view can redraw.</summary>
    public event EventHandler? Changed;

    /// <summary>Adds a job.</summary>
    /// <param name="work">The job to run.</param>
    /// <param name="atFront">
    /// Whether it should run before the jobs already queued. Ranger's paste command uses this so
    /// that a copy the user just asked for starts immediately.
    /// </param>
    /// <returns>The queued job, for progress and cancellation.</returns>
    public QueuedTask Add(ILoadable work, bool atFront = false)
    {
        ArgumentNullException.ThrowIfNull(work);

        QueuedTask task = new(work);

        if (atFront)
        {
            _tasks.Insert(0, task);
        }
        else
        {
            _tasks.Add(task);
        }

        OnChanged();
        return task;
    }

    /// <summary>
    /// Advances the job at the front for a slice of time.
    /// </summary>
    /// <param name="slice">
    /// How long to spend. Defaults to <see cref="DefaultSlice"/>. A zero slice advances exactly
    /// one step, which is what tests use to step through a job deterministically.
    /// </param>
    /// <remarks>
    /// The slice also ends as soon as the job reports itself idle. Without that, a job whose step
    /// is a non-blocking poll would spin out the whole budget asking the same question thousands
    /// of times — which is worse than the blocking wait it replaced, both for the key latency and
    /// for the processor.
    /// </remarks>
    /// <returns><see langword="true"/> when any work was done.</returns>
    public bool Work(TimeSpan? slice = null)
    {
        TimeSpan budget = slice ?? DefaultSlice;

        // Before the job in hand, not after: a job queued a moment ago should have a size by the
        // time the bar is next drawn, and going second would mean waiting out the copy's whole
        // slice first. A zero slice sizes nothing, which is what keeps "advance exactly one
        // step" exact for callers stepping a job deliberately.
        bool sized = Size(Divide(budget, SizingShare, MaximumSizingSlice));

        QueuedTask? task = NextRunnable();
        if (task is null)
        {
            return sized;
        }

        Rotate();

        long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;

        task.State = TaskState.Running;
        bool advanced = false;

        if (task.Sized?.BytesPerSecond is { } rate)
        {
            _lastObservedRate = rate;
        }

        do
        {
            switch (Step(task, task.Steps))
            {
                case StepOutcome.Finished:
                    Stop(task, TaskState.Finished, null);
                    return true;

                case StepOutcome.Stopped:
                    return true;

                default:
                    advanced = true;
                    break;
            }
        }
        while (Environment.TickCount64 < deadline && task.Work.Idle <= TimeSpan.Zero);

        return advanced;
    }

    /// <summary>What advancing an iterator by one step did.</summary>
    private enum StepOutcome
    {
        /// <summary>It did some work and there is more to come.</summary>
        Advanced,

        /// <summary>It ran out of steps.</summary>
        Finished,

        /// <summary>It threw, and the job has been marked accordingly.</summary>
        Stopped,
    }

    /// <summary>
    /// Advances one of a job's iterators by a step, turning a throw into a state change.
    /// </summary>
    /// <param name="task">The job the iterator belongs to.</param>
    /// <param name="steps">The iterator to advance.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// Running out of steps is reported rather than acted on, because it means different things
    /// on the two channels: the job is done on one, and merely sized on the other. A throw means
    /// the same thing on both — the job is over — so it is dealt with here, once. Sizing a
    /// transfer walks the same tree the transfer will, and a failure there is the failure the
    /// transfer would have hit, so it is right that it stops the job just as it would have.
    /// </remarks>
    private StepOutcome Step(QueuedTask task, IEnumerator<Unit> steps)
    {
        try
        {
            return steps.MoveNext() ? StepOutcome.Advanced : StepOutcome.Finished;
        }
        catch (OperationCanceledException)
        {
            Stop(task, TaskState.Cancelled, null);
            return StepOutcome.Stopped;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // One failing job must not take the queue, or the application, down with it.
            Stop(task, TaskState.Failed, e);
            return StepOutcome.Stopped;
        }
    }

    /// <summary>Ends a job, releasing what it holds and telling the task view.</summary>
    private void Stop(QueuedTask task, TaskState state, Exception? error)
    {
        task.State = state;
        task.Error = error;
        task.Work.Dispose();
        OnChanged();
    }

    /// <summary>How much of a slice may go to sizing.</summary>
    private const double SizingShare = 0.1;

    /// <summary>Takes a share of a budget, up to a ceiling.</summary>
    private static TimeSpan Divide(TimeSpan budget, double share, TimeSpan ceiling) =>
        budget <= TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(
                Math.Min(budget.TotalMilliseconds * share, ceiling.TotalMilliseconds));

    /// <summary>
    /// Establishes the size of queued jobs that do not have one yet, for a slice of time.
    /// </summary>
    /// <param name="budget">How long to spend. Zero does nothing at all.</param>
    /// <returns><see langword="true"/> when any walking was done.</returns>
    /// <remarks>
    /// Front of the queue first, so the job about to run is sized before the ones behind it and
    /// starts no later than it would have. Public so that a caller driving the queue by hand —
    /// a test, or a plugin with its own loop — can size deliberately; <see cref="Work"/> already
    /// calls it, so nothing has to remember to.
    /// </remarks>
    public bool Size(TimeSpan budget)
    {
        if (budget <= TimeSpan.Zero)
        {
            return false;
        }

        long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
        bool advanced = false;

        while (NextUnsized() is { } task)
        {
            if (Step(task, task.SizingSteps!) is StepOutcome.Advanced)
            {
                advanced = true;
            }
            else
            {
                // Either it finished, or it threw and `Step` has already ended the job. Both
                // mean there is nothing more to ask this walk for.
                task.SizingExhausted = true;
                OnChanged();
            }

            if (Environment.TickCount64 >= deadline)
            {
                break;
            }
        }

        return advanced;
    }

    /// <summary>The first job that could have a size and does not yet.</summary>
    private QueuedTask? NextUnsized()
    {
        foreach (QueuedTask task in _tasks)
        {
            if (!task.IsComplete && !task.SizingExhausted && task.Sized is { IsSized: false })
            {
                return task;
            }
        }

        return null;
    }

    /// <summary>Holds a job, so the queue moves on to the next one.</summary>
    /// <param name="task">The job to hold.</param>
    public void Pause(QueuedTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.IsComplete)
        {
            task.State = TaskState.Paused;
            OnChanged();
        }
    }

    /// <summary>Releases a held job.</summary>
    /// <param name="task">The job to release.</param>
    public void Resume(QueuedTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.State == TaskState.Paused)
        {
            task.State = TaskState.Waiting;
            OnChanged();
        }
    }

    /// <summary>Stops a job and removes it.</summary>
    /// <param name="task">The job to stop.</param>
    public void Cancel(QueuedTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.IsComplete)
        {
            task.State = TaskState.Cancelled;
            task.Work.Dispose();
        }

        _tasks.Remove(task);
        OnChanged();
    }

    /// <summary>Moves a job within the queue, which is how the task view reorders work.</summary>
    /// <param name="task">The job to move.</param>
    /// <param name="offset">How far to move it. Negative moves it earlier.</param>
    public void Move(QueuedTask task, int offset)
    {
        ArgumentNullException.ThrowIfNull(task);

        int index = _tasks.IndexOf(task);
        if (index < 0)
        {
            return;
        }

        int destination = Math.Clamp(index + offset, 0, _tasks.Count - 1);
        if (destination == index)
        {
            return;
        }

        _tasks.RemoveAt(index);
        _tasks.Insert(destination, task);
        OnChanged();
    }

    /// <summary>Forgets jobs that have finished, failed or been cancelled.</summary>
    /// <returns>How many were removed.</returns>
    public int RemoveCompleted()
    {
        int removed = _tasks.RemoveAll(t => t.IsComplete);
        if (removed > 0)
        {
            OnChanged();
        }

        Forget();
        return removed;
    }

    /// <summary>Stops everything, releasing whatever the jobs hold.</summary>
    public void CancelAll()
    {
        foreach (QueuedTask task in _tasks.Where(t => !t.IsComplete))
        {
            task.State = TaskState.Cancelled;
            task.Work.Dispose();
        }

        _tasks.Clear();
        Forget();
        OnChanged();
    }

    /// <summary>Drops what was learned about the last run of work, once there is none left.</summary>
    private void Forget()
    {
        if (_tasks.Count == 0)
        {
            _lastObservedRate = null;
        }
    }

    /// <summary>
    /// The average progress of everything outstanding, which the status bar draws as a bar.
    /// </summary>
    /// <returns>A value from 0 to 1, or <see langword="null"/> when nothing reports progress.</returns>
    public double? OverallProgress()
    {
        // By size when every outstanding job has one, which is what makes a four gigabyte film
        // count for more than a hundred megabyte one instead of the two being averaged as equals.
        //
        // This was tried once before and reverted, because a job waiting its turn weighed nothing
        // until it started, so the figure *fell* the moment its size arrived — and a number that
        // goes backwards is worse than one that is only roughly right. What changed is that jobs
        // are now sized while they wait, so the size arrives within a frame or two of the job
        // being queued rather than minutes later when its turn comes. The guard below is the
        // other half: until everything outstanding has a size, the old average is used, so the
        // weighted figure never starts from a total it is about to revise.
        (long completed, long total, bool whole) = SizedBytes();

        if (whole && total > 0)
        {
            return Math.Clamp((double)completed / total, 0, 1);
        }

        // Finished jobs included. They are swept only once the whole queue drains, so while
        // anything is still running they are part of the work this figure speaks for — and
        // leaving them out was what made it fall back: two films queued, and the bar returned to
        // nothing the moment the first one finished, because it had begun describing only the
        // second.
        double[] values = [.. _tasks.Select(t => t.Progress).OfType<double>()];

        return values.Length == 0 ? null : values.Average();
    }

    /// <summary>
    /// Adds up the bytes across the queue, and says whether that covers all of it.
    /// </summary>
    /// <returns>
    /// What is done, what there is, and whether every outstanding job was counted. A job that
    /// cannot report a size — unpacking an archive, running an external command — makes the last
    /// of those false, because a byte figure that quietly leaves work out is worse than none.
    /// </returns>
    private (long Completed, long Total, bool Whole) SizedBytes()
    {
        long completed = 0;
        long total = 0;
        bool whole = true;

        foreach (QueuedTask task in _tasks)
        {
            if (task.Sized is { IsSized: true } sized
                && sized.TotalBytes is { } size && sized.RemainingBytes is { } remaining)
            {
                total += size;
                completed += task.IsComplete ? size : Math.Max(size - remaining, 0);
                continue;
            }

            // A finished job with no size is simply not counted. It contributes nothing either
            // way, and it is over, so it cannot make the figure move.
            if (!task.IsComplete)
            {
                whole = false;
            }
        }

        return (completed, total, whole);
    }

    /// <summary>
    /// What the queue has to say about all of its outstanding work.
    /// </summary>
    /// <returns>
    /// The summary, or <see langword="null"/> when nothing is running.
    /// </returns>
    /// <remarks>
    /// The figures cover the queue rather than the job in hand, which is the difference between
    /// a bar that runs to the end once and one that restarts from nothing every time a file
    /// finishes. The rate comes from the running job alone, since only one job moves data at a
    /// time; a paused job is still counted as work to come, because pausing is a deliberate act
    /// and the user can see what they held.
    /// </remarks>
    public QueueSummary? Summary()
    {
        if (NextRunnable() is not { } running)
        {
            return null;
        }

        (long completed, long total, bool whole) = SizedBytes();
        bool known = total > 0;

        double? rate = running.Sized?.BytesPerSecond ?? _lastObservedRate;
        TimeSpan? estimate = known && rate is > 0
            ? TimeSpan.FromSeconds(Math.Max(total - completed, 0) / rate.Value)
            : null;

        // Cancelled and failed jobs are left out of the count: saying "2 of 5" when three of the
        // five were abandoned describes the list rather than the work.
        int count = _tasks.Count(t => t.State is not (TaskState.Cancelled or TaskState.Failed));
        int position = 1 + _tasks.TakeWhile(t => t != running)
                                 .Count(t => t.State is not (TaskState.Cancelled or TaskState.Failed));

        return new QueueSummary(
            running.Sized?.Subject ?? running.Description,
            running.Description,
            position,
            count,
            known ? completed : null,
            known ? total : null,
            rate,
            estimate,
            !whole);
    }

    /// <summary>The first job that is neither complete nor held.</summary>
    private QueuedTask? NextRunnable()
    {
        foreach (QueuedTask task in _tasks)
        {
            if (!task.IsComplete && task.State != TaskState.Paused)
            {
                return task;
            }
        }

        return null;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
