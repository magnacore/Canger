// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tasks;

/// <summary>
/// A long-running job that reports progress and can be interrupted between steps.
/// </summary>
/// <remarks>
/// <para>
/// Work is expressed as an iterator: each step does a little and yields, and the queue decides
/// when to ask for the next one. That is what lets a copy of ten thousand files be paused,
/// reordered or cancelled from the task view, and keeps the interface responsive while it runs,
/// without the job needing to know anything about threads.
/// </para>
/// <para>
/// Ranger reaches the same arrangement with Python generators (<c>core/loader.py:29-51</c>).
/// Translating it to <c>async</c> would lose what matters here: the queue's strict ordering, the
/// ability to stop between steps, and the fact that only one job advances at a time. Parallelism
/// belongs inside a step, not between them.
/// </para>
/// </remarks>
public interface ILoadable
{
    /// <summary>What the task view calls this job.</summary>
    string Description { get; }

    /// <summary>How far along the job is, from 0 to 1, or <see langword="null"/> when unknown.</summary>
    double? Progress { get; }

    /// <summary>
    /// The steps of the job. Advancing the iterator performs a little work and returns.
    /// </summary>
    /// <returns>One element per step. The values themselves are not used.</returns>
    IEnumerator<Unit> Steps();

    /// <summary>
    /// How long there is no point in asking for another step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero — the default — means the job has work of its own to get on with, so the queue should
    /// come straight back. A job that is only waiting for something outside itself says how long
    /// it expects to wait, and the main loop spends that time watching the keyboard instead. The
    /// job is polled just as often either way; the difference is that a keystroke now interrupts
    /// the wait rather than queueing behind it.
    /// </para>
    /// <para>
    /// This has no counterpart in ranger, whose <c>CommandLoader</c> blocks in
    /// <c>select(..., 0.03)</c> on the process's pipes and does not watch stdin while it does
    /// (<c>core/loader.py:239</c>) — so a keypress there waits out the timeout. Doing the same
    /// measured a 40 ms worst-case key latency while an archive was unpacking, against 1 ms
    /// idle, which is exactly the sluggishness this avoids.
    /// </para>
    /// </remarks>
    TimeSpan Idle => TimeSpan.Zero;

    /// <summary>Releases anything the job holds, whether it finished or was cancelled.</summary>
    void Dispose()
    {
    }
}

/// <summary>
/// The unit type, used where a step yields nothing meaningful.
/// </summary>
/// <remarks>
/// A step's job is to make progress, not to produce a value, so there is nothing for it to
/// return. This makes that explicit rather than yielding a placeholder integer or boolean that a
/// reader would have to check the meaning of.
/// </remarks>
public readonly record struct Unit
{
    /// <summary>The only value.</summary>
    public static Unit Value => default;
}
