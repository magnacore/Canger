// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tasks;

/// <summary>
/// Something going on that is not a queued task, but is worth a line in the status bar.
/// </summary>
/// <remarks>
/// <para>
/// The task queue is for work Canger is doing and will finish: a copy, an archive. Audio playing
/// in the background is neither — it has no end the queue could wait for, it must not hold a
/// place in a queue that other work is waiting behind, and stopping it is the user's business
/// rather than a cancellation. So it reports itself here instead, and the status bar shows it
/// only when the queue has nothing to say.
/// </para>
/// <para>
/// A pull, not a push: <see cref="Describe"/> is called while the bar is being drawn, which is
/// what gives a plugin its heartbeat. Nothing else in Canger would give one — there is no timer a
/// plugin can hang off — so an implementation is expected to do its polling here, and to be quick
/// about it.
/// </para>
/// </remarks>
public interface IBackgroundActivity
{
    /// <summary>
    /// What to show, or <see langword="null"/> when nothing is happening.
    /// </summary>
    /// <returns>One short line, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Short, because it shares the bar with what is already there: the permissions on the left
    /// and the free space and position on the right both stay visible. A queued task replaces the
    /// lot; this does not.
    /// </remarks>
    string? Describe();

    /// <summary>
    /// How far through it is, from 0 to 1, or <see langword="null"/> for no bar.
    /// </summary>
    double? Progress { get; }
}
