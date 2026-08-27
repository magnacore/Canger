// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
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
    /// <returns>Zero for a combining mark, two for wide and fullwidth, otherwise one.</returns>
    /// <remarks>
    /// <para>
    /// The zero is the part that is easy to miss. A Devanagari matra, a Hebrew point, an accent
    /// applied to the letter before it — these are drawn <em>on</em> the previous cell and take no
    /// column of their own. Counting them as one made every such name measure wider than it
    /// renders: <c>हेल्थ इंश्यो</c> is twelve code points and eight columns.
    /// </para>
    /// <para>
    /// Two symptoms, one cause. Text was truncated early, so the cells past the end were never
    /// written and whatever the previous frame left there — a count from the column behind —
    /// showed through. And every chunk after a mark was positioned a column too far right, which
    /// is the gaps that appear inside the words.
    /// </para>
    /// <para>
    /// Spacing marks (<c>Mc</c>) keep their column, and that was tried the other way round and
    /// reverted. Zeroing them closed the gaps that appear inside Devanagari words — <c>का</c>
    /// drawn as one cluster where two columns were reserved — and then every name containing one
    /// measured narrower than it drew, so text overran its column and the neighbouring column was
    /// written over. A gap inside a word is a blemish; a listing whose columns bleed into each
    /// other is unusable, and the second is what zeroing them produced.
    /// </para>
    /// <para>
    /// The lesson is about the shape of the rule rather than the value. Whether a cluster takes
    /// one column or two is a question about the font and the terminal's shaping, and it cannot
    /// be answered from the Unicode category alone. Until it is measured against the terminal,
    /// over-reserving is the safe direction: it wastes a column and keeps the grid.
    /// </para>
    /// <para>
    /// Ranger measures by East Asian Width alone (<c>ext/widestring.py:27</c>) and has the same
    /// fault. The terminal is the authority here, not ranger.
    /// </para>
    /// </remarks>
    public static int Of(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.EnclosingMark or
        UnicodeCategory.Format => 0,
        _ => IsWide(rune.Value) ? 2 : 1,
    };

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
