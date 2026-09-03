// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Tasks;

/// <summary>
/// Reads a byte count a command prints for the purpose.
/// </summary>
/// <remarks>
/// <para>
/// <c>tar</c> takes <c>--checkpoint=N --checkpoint-action=echo=...</c> and will then report every
/// N records; giving it a marker Canger recognises turns that into a live count of the bytes
/// <em>read</em>, which paired with the size of the selection is a true percentage rather than an
/// estimate. Nothing here knows that it is tar: it looks for a marker and a number, and the
/// command line that emits them is written elsewhere.
/// </para>
/// <para>
/// The last occurrence wins, and the figure never goes backwards. A count that retreats is worse
/// than one that is briefly stale, and a checkpoint line interleaved with an error message can
/// arrive torn.
/// </para>
/// </remarks>
/// <param name="marker">The text immediately before the number, such as <c>canger-bytes:</c>.</param>
/// <param name="total">What the work amounts to, when it is known.</param>
public sealed class MarkerProgress(string marker, long? total) : ICommandProgress
{
    private readonly string _marker =
        string.IsNullOrEmpty(marker) ? throw new ArgumentException("A marker is required.", nameof(marker))
                                     : marker;

    private long? _completed;
    private long? _total = total;

    /// <inheritdoc />
    /// <remarks>
    /// Dropped the moment the command's own output exceeds it, because a count that runs past its
    /// total proves the total was the wrong quantity — and once that is known, showing bytes is
    /// honest where showing a percentage is not. It was reported as "the extraction goes on for a
    /// few more seconds after the progress bar shows 100%": extraction had been given the archive's
    /// size on disk while tar counts the uncompressed stream, so a 4 456-byte archive reported
    /// 26 603 520 bytes and the bar sat pinned at the top. The caller was fixed too, but clamping
    /// is what let a wrong total look like a finished one, and any future caller can get it wrong
    /// the same way.
    /// </remarks>
    public long? Total => _total;

    /// <inheritdoc />
    public long? Completed => _completed;

    /// <inheritdoc />
    public bool Reports(string line) =>
        line is not null && line.Contains(_marker, StringComparison.Ordinal);

    /// <inheritdoc />
    /// <remarks>
    /// Every occurrence is read and the largest kept, rather than only the last. The last can be
    /// torn — the text arrives as it is written, so a final <c>canger-bytes:</c> with its digits
    /// still to come would otherwise hide the complete reading before it. Taking the largest also
    /// makes the figure monotonic for free, which matters because a count that retreats reads as
    /// a fault rather than as a redraw.
    /// </remarks>
    public void Update(string reported)
    {
        ArgumentNullException.ThrowIfNull(reported);

        long best = _completed ?? 0;
        int at = reported.IndexOf(_marker, StringComparison.Ordinal);

        while (at >= 0)
        {
            int start = at + _marker.Length;
            int end = start;

            while (end < reported.Length && char.IsAsciiDigit(reported[end]))
            {
                end++;
            }

            if (end > start &&
                long.TryParse(reported.AsSpan(start, end - start), CultureInfo.InvariantCulture,
                              out long bytes) &&
                bytes > best)
            {
                best = bytes;
            }

            at = reported.IndexOf(_marker, start, StringComparison.Ordinal);
        }

        if (best > 0 || _completed is not null)
        {
            _completed = best;
        }

        if (_total is { } stated && best > stated)
        {
            _total = null;
        }
    }
}
