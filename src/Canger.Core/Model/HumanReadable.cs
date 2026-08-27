// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Model;

/// <summary>Formats numbers for display in a narrow column.</summary>
public static class HumanReadable
{
    private static readonly string[] DecimalUnits = ["B", "k", "M", "G", "T", "P"];
    private static readonly string[] BinaryUnits = ["B", "Ki", "Mi", "Gi", "Ti", "Pi"];

    /// <summary>
    /// Formats a byte count compactly, so it fits beside a filename.
    /// </summary>
    /// <param name="bytes">The count.</param>
    /// <param name="binary">
    /// Whether to divide by 1024 and use binary prefixes, as the <c>binary_size_prefix</c>
    /// setting asks for, rather than dividing by 1000.
    /// </param>
    /// <param name="separator">
    /// What goes between the number and the unit. Ranger passes <c>"? "</c> to mark a figure it
    /// can no longer vouch for.
    /// </param>
    /// <param name="exact">
    /// Whether to write the count out in full with the locale's thousands separators instead of
    /// a prefix. The <c>size_in_bytes</c> setting, which ranger checks before anything else
    /// (<c>ext/human_readable.py:36-37</c>).
    /// </param>
    /// <returns>A short representation, such as <c>7.59 M</c>.</returns>
    /// <remarks>
    /// <para>
    /// Three significant figures, which is <c>%.3g</c> — what ranger uses
    /// (<c>ext/human_readable.py:51</c>). Not one decimal place: 19.9 M and 825 k both carry
    /// three digits, and rounding by decimal places instead turned them into 20 M and 825 k,
    /// throwing away a digit exactly where a file listing is being read for one.
    /// </para>
    /// <para>
    /// Four figures once the value reaches 1000, which happens only with binary prefixes — 1023
    /// Mi is a real quantity and 1.02 Gi is not what it says. Ranger's <c>%.4g</c>, same reason.
    /// </para>
    /// <para>
    /// Trailing zeros are dropped, as <c>%g</c> does, so 1500 is <c>1.5 k</c> rather than
    /// <c>1.50 k</c> and 20 million is <c>20 M</c> rather than <c>20.0 M</c>.
    /// </para>
    /// </remarks>
    public static string Format(long bytes, bool binary = false, string separator = " ",
                                bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(separator);

        // Before the zero check, as in ranger: asking for exact bytes and being told "0" with no
        // unit is the same answer either way, but asking for them and being told "1.2 k" is not.
        if (exact)
        {
            return bytes.ToString("N0", CultureInfo.CurrentCulture);
        }

        // Bare, with no unit and no separator, as ranger has it
        // (`ext/human_readable.py:34-35`). A size of nothing needs no unit to be understood.
        if (bytes <= 0)
        {
            return "0";
        }

        double step = binary ? 1024 : 1000;
        string[] units = binary ? BinaryUnits : DecimalUnits;

        double value = bytes;
        int unit = 0;

        while (value >= step && unit < units.Length - 1)
        {
            value /= step;
            unit++;
        }

        // The separator sits between the number and the unit, which is what lets ranger mark a
        // figure it is no longer sure of by passing "? " and getting `4.5? k`
        // (`ext/human_readable.py:52`).
        return $"{SignificantFigures(value, value < 1000 ? 3 : 4)}{separator}{units[unit]}";
    }

    /// <summary>Rounds to a number of significant figures and drops trailing zeros.</summary>
    /// <param name="value">The value, which is always at least one by the time it gets here.</param>
    /// <param name="digits">How many significant figures to keep.</param>
    /// <returns>The rounded number, without a trailing zero or point.</returns>
    /// <remarks>
    /// <para>
    /// C's <c>%g</c> in the one respect that matters here, with one deliberate difference: it
    /// switches to exponential notation when rounding carries the value up a digit, so ranger
    /// renders 999 999 bytes as <c>1e+03 k</c>. That is five characters of nothing in a column
    /// measured in characters, and it says less than the number it replaced. Written plainly it
    /// is <c>1000 k</c>. The band this can happen in is the last 0.05% below each unit boundary.
    /// </para>
    /// <para>
    /// <c>"0.##"</c> is enough to hold the result: two decimals is the most three significant
    /// figures can call for once the value is at least one, and four figures are only ever asked
    /// for at a thousand or more, where none are wanted.
    /// </para>
    /// </remarks>
    private static string SignificantFigures(double value, int digits)
    {
        int exponent = (int)Math.Floor(Math.Log10(value));
        int decimals = Math.Max(digits - 1 - exponent, 0);

        return Math.Round(value, decimals, MidpointRounding.AwayFromZero)
                   .ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a timestamp the way a file listing does: time for today, weekday within a week,
    /// day and month within a year, and the year beyond that.
    /// </summary>
    /// <param name="timestamp">The time to format.</param>
    /// <param name="now">The moment to judge "recent" against.</param>
    /// <returns>A short representation.</returns>
    public static string FormatTime(DateTimeOffset timestamp, DateTimeOffset now)
    {
        TimeSpan age = now - timestamp;

        if (age < TimeSpan.FromHours(24) && timestamp.Date == now.Date)
        {
            return timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (age < TimeSpan.FromDays(7))
        {
            return timestamp.ToString("ddd", CultureInfo.InvariantCulture);
        }

        return age < TimeSpan.FromDays(365)
            ? timestamp.ToString("d MMM", CultureInfo.InvariantCulture)
            : timestamp.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a duration as minutes and seconds, or hours when it is long.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>A short representation, such as <c>12:40</c> or <c>1:05:03</c>.</returns>
    /// <remarks>
    /// Here beside the byte formatter because both the figures for a single transfer and the
    /// figures the queue reports for all of them need it, and they live in different places.
    /// </remarks>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            return "--:--";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
    }
}
