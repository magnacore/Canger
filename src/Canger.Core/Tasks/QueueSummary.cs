// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.Model;

namespace Canger.Core.Tasks;

/// <summary>
/// What the queue has to say about everything outstanding, not only the job in hand.
/// </summary>
/// <param name="Subject">What the running job is doing, named without its figures.</param>
/// <param name="RunningDescription">
/// The running job's own line, figures included, which is what is shown when there is nothing
/// else queued for it to be measured against.
/// </param>
/// <param name="Position">Which job is running, counting from one.</param>
/// <param name="Count">How many jobs the queue has taken on, finished ones included.</param>
/// <param name="CompletedBytes">
/// How much of the sized work is done, or <see langword="null"/> when none of it has a size yet.
/// </param>
/// <param name="TotalBytes">How much the sized work comes to, or <see langword="null"/>.</param>
/// <param name="BytesPerSecond">How fast data is moving, or <see langword="null"/>.</param>
/// <param name="Estimate">How much longer it should all take, or <see langword="null"/>.</param>
/// <param name="Partial">
/// Whether something outstanding has no size yet, so the figures speak for part of the queue
/// rather than all of it.
/// </param>
public sealed record QueueSummary(string Subject, string RunningDescription, int Position,
                                  int Count, long? CompletedBytes, long? TotalBytes,
                                  double? BytesPerSecond, TimeSpan? Estimate, bool Partial)
{
    /// <summary>
    /// Renders the summary as the single line the status bar shows.
    /// </summary>
    /// <returns>One line of text.</returns>
    /// <remarks>
    /// <para>
    /// With one job queued this is that job's own line, unchanged: there is no whole for the
    /// figures to speak for beyond the part, and <c>(1 of 1)</c> would be noise.
    /// </para>
    /// <para>
    /// With more than one it names the job in hand and then gives the figures for all of them,
    /// which is the point of the exercise — the bar used to restart from nothing each time a
    /// film finished, because it had only ever described whichever film was moving.
    /// </para>
    /// </remarks>
    public string Describe()
    {
        if (Count <= 1 || TotalBytes is not { } total || total <= 0
            || CompletedBytes is not { } completed)
        {
            return RunningDescription;
        }

        System.Text.StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"{Subject} ({Position} of {Count}): ");
        text.Append(TransferFigures.Describe((double)completed / total, completed, total,
                                             BytesPerSecond, Estimate));

        // A quiet mark rather than a sentence. Something queued has not been sized yet, so the
        // figures cover part of the queue; it is gone within a frame or two for files, and lasts
        // as long as the walk does for a large tree. Saying so is cheap, and a total that
        // silently grows is the kind of thing that teaches people not to trust the number.
        if (Partial)
        {
            text.Append('+');
        }

        return text.ToString();
    }
}
