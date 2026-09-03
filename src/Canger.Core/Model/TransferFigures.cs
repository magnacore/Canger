// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace Canger.Core.Model;

/// <summary>
/// Writes the figures a transfer reports — percentage, bytes, rate, time remaining — in columns
/// that stay where they are.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these fields changes width as it counts: <c>5%</c> becomes <c>48%</c> becomes
/// <c>100%</c>, and <c>159 M</c> becomes <c>1.08 G</c>. Written plainly, each change shoves
/// everything after it sideways, and the rate and the estimate — the two a person actually
/// watches — jitter left and right several times a second. Each field is therefore given a width
/// wide enough for anything it can hold, so the columns are still.
/// </para>
/// <para>
/// The widths suit decimal prefixes, which is what this line uses: three significant figures and
/// a one-letter unit reach six characters at their widest, <c>88.5 M</c>, because the count rolls
/// over to the next prefix at a thousand rather than at 1024. Padding only ever grows a field, so
/// a value that somehow exceeded its column would still be shown in full.
/// </para>
/// <para>
/// One place, so that the line a single transfer shows and the line the queue shows for all of
/// them line up with each other as well as with themselves.
/// </para>
/// </remarks>
public static class TransferFigures
{
    /// <summary>Room for <c>100%</c>.</summary>
    private const int PercentWidth = 4;

    /// <summary>Room for the widest a decimal byte count reaches — <c>88.5 M</c>.</summary>
    private const int SizeWidth = 6;

    /// <summary>Room for <c>88.5 M</c> and its two bytes of unit — <c>88.5 M/s</c>.</summary>
    private const int RateWidth = SizeWidth + 2;

    /// <summary>Room for the widest <c>completed/total</c> pair.</summary>
    private const int PairWidth = (SizeWidth * 2) + 1;

    /// <summary>
    /// Describes how far a transfer has got.
    /// </summary>
    /// <param name="fraction">
    /// How far along, from 0 to 1, or <see langword="null"/> when the work cannot say — an
    /// archiver with no total to measure against, which reports what it has written and no more.
    /// </param>
    /// <param name="completedBytes">Bytes accounted for.</param>
    /// <param name="totalBytes">Bytes the transfer covers, or zero when that is not known.</param>
    /// <param name="bytesPerSecond">Throughput, or <see langword="null"/> when nothing has moved.</param>
    /// <param name="estimate">Time remaining, or <see langword="null"/> when it cannot be guessed.</param>
    /// <returns>A single line, its columns aligned.</returns>
    public static string Describe(double? fraction, long completedBytes, long totalBytes,
                                  double? bytesPerSecond, TimeSpan? estimate)
    {
        StringBuilder text = new();

        if (fraction is { } portion)
        {
            text.Append((portion * 100).ToString("F0", CultureInfo.InvariantCulture)
                                       .PadLeft(PercentWidth - 1))
                .Append('%');
        }

        if (totalBytes > 0)
        {
            // The total is fixed for the length of a run, so within one transfer this pair is
            // already a constant width; the padding is what keeps the rate still across a
            // revision of the total, which happens when something else is queued behind.
            string pair = $"{HumanReadable.Format(completedBytes).PadLeft(SizeWidth)}"
                          + $"/{HumanReadable.Format(totalBytes)}";

            Separate(text).Append(pair.PadRight(PairWidth));
        }
        else if (completedBytes > 0)
        {
            // No total to measure against, so the count stands alone. Same column as the pair
            // above starts in, so a queue holding one of each does not read as ragged.
            Separate(text).Append(HumanReadable.Format(completedBytes).PadLeft(SizeWidth)
                                               .PadRight(PairWidth));
        }

        if (bytesPerSecond is { } rate)
        {
            Separate(text).Append($"{HumanReadable.Format((long)rate)}/s".PadRight(RateWidth));
        }

        // Last, and so the one field that needs no width of its own: nothing follows it for a
        // change of width to push along.
        if (estimate is { } remaining)
        {
            Separate(text).Append("ETA ").Append(HumanReadable.Duration(remaining));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Opens the next field, gapped from the last unless it is the first.</summary>
    /// <param name="text">The line so far.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Written out rather than prefixing every field with two spaces, because a line whose first
    /// field is absent — a percentage nobody can compute — would otherwise begin with the gap
    /// that was meant to follow it, and sit a couple of columns in from every other line.
    /// </remarks>
    private static StringBuilder Separate(StringBuilder text) =>
        text.Length > 0 ? text.Append("  ") : text;
}
