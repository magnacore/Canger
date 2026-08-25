// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Tui.Text;

/// <summary>
/// Measures text in terminal cells rather than in characters.
/// </summary>
/// <remarks>
/// A terminal lays out text in a grid of fixed cells, and some characters occupy two of them.
/// Every width calculation in Canger — truncating a filename, shrinking the status bar, placing
/// the console cursor, slicing a preview line — has to count cells, because that is what decides
/// where the next thing lands on screen.
/// </remarks>
public static class CellWidth
{
    /// <summary>How many cells a single code point occupies.</summary>
    /// <param name="rune">The code point.</param>
    /// <returns>Two for wide and fullwidth characters, otherwise one.</returns>
    public static int Of(Rune rune) => IsWide(rune.Value) ? 2 : 1;

    /// <summary>How many cells a string occupies.</summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The total width in cells.</returns>
    public static int Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            width += Of(rune);
        }

        return width;
    }

    /// <summary>Whether a code point occupies two cells.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the character is wide or fullwidth.</returns>
    public static bool IsWide(int codePoint)
    {
        // The table is a flat sorted list of inclusive start and end pairs, so a binary search
        // over the starts finds the only range that could contain the code point.
        ReadOnlySpan<int> ranges = EastAsianWidth.Ranges;

        int low = 0;
        int high = (ranges.Length / 2) - 1;

        while (low <= high)
        {
            int middle = (low + high) / 2;
            int start = ranges[middle * 2];
            int end = ranges[(middle * 2) + 1];

            if (codePoint < start)
            {
                high = middle - 1;
            }
            else if (codePoint > end)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
