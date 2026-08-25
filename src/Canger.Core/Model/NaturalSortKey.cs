// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Model;

/// <summary>
/// A sort key that orders embedded numbers by value rather than by digit, so that <c>file2</c>
/// comes before <c>file10</c>.
/// </summary>
/// <remarks>
/// This is Canger's default ordering, as it is ranger's. Plain lexicographic ordering puts
/// <c>track10.mp3</c> before <c>track2.mp3</c>, which is almost never what someone looking at a
/// list of files wants.
/// </remarks>
public sealed class NaturalSortKey
{
    private readonly Segment[] _segments;
    private readonly Segment[] _lowercaseSegments;

    /// <summary>Builds a key from the text a node is displayed as.</summary>
    /// <param name="text">The text to order by.</param>
    public NaturalSortKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        _segments = Split(text);
        _lowercaseSegments = Split(text.ToLowerInvariant());
    }

    /// <summary>The text this key was built from.</summary>
    public string Text { get; }

    /// <summary>Orders keys case-sensitively.</summary>
    public static IComparer<NaturalSortKey> Comparer { get; } = new KeyComparer(ignoreCase: false);

    /// <summary>Orders keys case-insensitively, which is Canger's default.</summary>
    public static IComparer<NaturalSortKey> CaseInsensitiveComparer { get; } =
        new KeyComparer(ignoreCase: true);

    /// <summary>Compares two keys, optionally ignoring case.</summary>
    /// <param name="other">The key to compare against.</param>
    /// <param name="ignoreCase">Whether to compare case-insensitively.</param>
    /// <returns>A negative value, zero or a positive value.</returns>
    public int Compare(NaturalSortKey? other, bool ignoreCase)
    {
        if (other is null)
        {
            return 1;
        }

        Segment[] left = ignoreCase ? _lowercaseSegments : _segments;
        Segment[] right = ignoreCase ? other._lowercaseSegments : other._segments;

        int count = Math.Min(left.Length, right.Length);
        for (int i = 0; i < count; i++)
        {
            int comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// Breaks text into alternating runs of digits and non-digits, so the digit runs can be
    /// compared as numbers.
    /// </summary>
    private static Segment[] Split(string text)
    {
        List<Segment> segments = [];
        int index = 0;

        while (index < text.Length)
        {
            bool isDigit = char.IsAsciiDigit(text[index]);
            int start = index;

            while (index < text.Length && char.IsAsciiDigit(text[index]) == isDigit)
            {
                index++;
            }

            string run = text[start..index];

            // A run of digits longer than a long can hold is compared as text, which is still
            // deterministic and cannot throw.
            segments.Add(isDigit &&
                         long.TryParse(run, NumberStyles.None, CultureInfo.InvariantCulture,
                                       out long value)
                ? new Segment(value, null)
                : new Segment(null, run));
        }

        return [.. segments];
    }

    /// <summary>Orders keys with a fixed case sensitivity.</summary>
    private sealed class KeyComparer(bool ignoreCase) : IComparer<NaturalSortKey>
    {
        public int Compare(NaturalSortKey? x, NaturalSortKey? y) =>
            x is null ? (y is null ? 0 : -1) : x.Compare(y, ignoreCase);
    }

    /// <summary>One run of digits or non-digits.</summary>
    private readonly record struct Segment(long? Number, string? Text) : IComparable<Segment>
    {
        public int CompareTo(Segment other)
        {
            if (Number is { } mine && other.Number is { } theirs)
            {
                return mine.CompareTo(theirs);
            }

            // Numbers sort before text, so "2" precedes "a" consistently.
            if (Number is not null)
            {
                return -1;
            }

            if (other.Number is not null)
            {
                return 1;
            }

            return string.CompareOrdinal(Text, other.Text);
        }
    }
}
