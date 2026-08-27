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

    /// <summary>Whether it will not be advanced again.</summary>
    public bool IsComplete =>
        State is TaskState.Finished or TaskState.Cancelled or TaskState.Failed;

    /// <summary>The step iterator, created on first use so a queued job does nothing until run.</summary>
    internal IEnumerator<Unit> Steps => _steps ??= Work.Steps();
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

    /// <summary>The spinner's frames, in order.</summary>
    private static readonly char[] ThrobberChars = ['/', '-', '\\', '|'];

    /// <summary>What is shown in place of the spinner when the running task is paused.</summary>
    private const char ThrobberPaused = '#';

    private int _throbber;

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
        QueuedTask? task = NextRunnable();
        if (task is null)
        {
            return false;
        }

        Rotate();

        TimeSpan budget = slice ?? DefaultSlice;
        long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;

        task.State = TaskState.Running;
        bool advanced = false;

        do
        {
            try
            {
                if (!task.Steps.MoveNext())
                {
                    task.State = TaskState.Finished;
                    task.Work.Dispose();
                    OnChanged();
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                task.State = TaskState.Cancelled;
                task.Work.Dispose();
                OnChanged();
                return true;
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                // One failing job must not take the queue, or the application, down with it.
                task.State = TaskState.Failed;
                task.Error = e;
                task.Work.Dispose();
                OnChanged();
                return true;
            }

            advanced = true;
        }
        while (Environment.TickCount64 < deadline && task.Work.Idle <= TimeSpan.Zero);

        return advanced;
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
        OnChanged();
    }

    /// <summary>
    /// The average progress of everything outstanding, which the status bar draws as a bar.
    /// </summary>
    /// <returns>A value from 0 to 1, or <see langword="null"/> when nothing reports progress.</returns>
    public double? OverallProgress()
    {
        double[] values =
        [
            .. _tasks.Where(t => !t.IsComplete)
                     .Select(t => t.Progress)
                     .OfType<double>(),
        ];

        return values.Length == 0 ? null : values.Average();
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
