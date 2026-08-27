// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tasks;

/// <summary>
/// A job whose extent can be established before it runs, so the queue can speak for the whole of
/// its outstanding work rather than only for the part in progress.
/// </summary>
/// <remarks>
/// <para>
/// The queue serves one job at a time, and a job that only discovers its own size when its turn
/// comes cannot contribute to a figure covering everything waiting. Two films pasted together
/// showed the second one's percentage restarting from nothing, because the bar had only ever
/// described whichever film was moving.
/// </para>
/// <para>
/// Sizing is separated from doing for that reason. Establishing a size reads metadata and moves
/// no data, so — unlike the work itself — it can go on beside a running job without the two
/// contending for the disc, which is what the queue's one-job-at-a-time rule exists to prevent.
/// </para>
/// <para>
/// Implementing this is optional. A job that cannot say how big it is — unpacking an archive,
/// waiting on an external command — is served exactly as before, and simply does not take part
/// in the figures the queue reports for the whole.
/// </para>
/// </remarks>
public interface ISizedWork
{
    /// <summary>Whether the job's extent is now known.</summary>
    bool IsSized { get; }

    /// <summary>
    /// The steps of establishing the extent. Advancing does a little of the work and returns.
    /// </summary>
    /// <remarks>
    /// Called repeatedly, and expected to return the same iterator each time so that the walk is
    /// resumed rather than restarted. The iterator finishes when the job is sized. Draining it
    /// must have no effect other than to make <see cref="IsSized"/> true — in particular it must
    /// not begin the work itself, since the queue drives it while another job is running.
    /// </remarks>
    /// <returns>One element per step.</returns>
    IEnumerator<Unit> SizingSteps();

    /// <summary>
    /// How much the job covers in total, or <see langword="null"/> until it has been sized.
    /// </summary>
    long? TotalBytes { get; }

    /// <summary>
    /// How much is still to move, or <see langword="null"/> until the job has been sized.
    /// </summary>
    /// <remarks>
    /// Not simply the total less what has finished: work the filesystem does instantly — a
    /// reflink, a rename within one filesystem — is already accounted for, so that a queue full
    /// of it does not report hours remaining.
    /// </remarks>
    long? RemainingBytes { get; }

    /// <summary>
    /// How fast the job is moving data, or <see langword="null"/> when nothing has moved yet.
    /// </summary>
    double? BytesPerSecond { get; }

    /// <summary>
    /// What the job is doing, without the numbers — <c>copying Rango.mp4</c>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ILoadable.Description"/>, which appends that job's own figures.
    /// When the queue speaks for several jobs it needs to name the one in hand and then give the
    /// figures for all of them, which it cannot do with the two already joined together.
    /// </remarks>
    string Subject { get; }
}
