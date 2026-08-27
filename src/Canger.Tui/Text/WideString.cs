// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Tui.Text;

/// <summary>
/// A string addressed by terminal cell rather than by character, so it can be sliced to fit a
/// column without ever splitting a double-width character in half.
/// </summary>
/// <remarks>
/// <para>
/// Slicing text for a fixed-width column is where naive string handling breaks. Cutting
/// <c>モヒカン</c> after three cells lands in the middle of the second character, which a
/// terminal cannot draw. The fix, and what ranger does
/// (<c>ext/widestring.py:104-141</c>), is to emit a space for the half that survives, so the
/// result still occupies exactly the requested number of cells and the following column starts
/// where it should.
/// </para>
/// <para>
/// Internally each cell holds either a character or a continuation marker for the second half of
/// a wide character, which makes cell index and array index the same thing.
/// </para>
/// </remarks>
public sealed class WideString
{
    /// <summary>
    /// The text occupying each cell, or <see langword="null"/> for the second half of a wide one.
    /// </summary>
    /// <remarks>
    /// A <em>string</em> per cell rather than a rune, because a cell can hold more than one code
    /// point: a letter and the marks drawn on it. Holding a rune each made the array index and the
    /// cell index the same thing, which is true only while every rune takes a cell — and a
    /// Devanagari matra does not. <see cref="Slice"/> then advanced by the rune's width and a
    /// zero-width mark left it where it was, which is an infinite loop.
    /// </remarks>
    private readonly string?[] _cells;

    /// <summary>Creates a cell-addressed view of a string.</summary>
    /// <param name="text">The text.</param>
    public WideString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;

        List<string?> cells = [];
        int last = -1;

        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = CellWidth.Of(rune);

            if (width == 0)
            {
                // Drawn on the character before it. With nothing before it there is no cell to
                // join, and a mark with no base is not text anyone meant to write.
                if (last >= 0)
                {
                    cells[last] += rune.ToString();
                }

                continue;
            }

            last = cells.Count;
            cells.Add(rune.ToString());

            // A wide character owns the following cell too. Marking it keeps cell index and
            // array index aligned, so a slice can tell it has landed mid-character.
            if (width == 2)
            {
                cells.Add(null);
            }
        }

        _cells = [.. cells];
    }

    /// <summary>The underlying text.</summary>
    public string Text { get; }

    /// <summary>How many cells this text occupies.</summary>
    public int Width => _cells.Length;

    /// <summary>
    /// Takes the cells in a range, substituting a space for either half of a wide character the
    /// range cuts through.
    /// </summary>
    /// <param name="start">First cell, clamped to the start of the text.</param>
    /// <param name="length">How many cells to take.</param>
    /// <returns>Text occupying exactly <paramref name="length"/> cells, or fewer if it runs out.</returns>
    public string Slice(int start, int length)
    {
        if (length <= 0 || _cells.Length == 0)
        {
            return string.Empty;
        }

        int from = Math.Max(start, 0);
        int to = Math.Min(start + length, _cells.Length);

        if (from >= to)
        {
            return string.Empty;
        }

        StringBuilder result = new();

        // A continuation cell at the start means the range began inside a wide character.
        // Its first half is outside the range, so only a space remains.
        if (_cells[from] is null)
        {
            result.Append(' ');
            from++;
        }

        while (from < to)
        {
            string? cell = _cells[from];
            if (cell is null)
            {
                from++;
                continue;
            }

            // A wide character whose second half falls outside the range is likewise replaced,
            // so the result still fills the cells it was asked to fill.
            bool wide = from + 1 < _cells.Length && _cells[from + 1] is null;

            if (wide && from + 1 >= to)
            {
                result.Append(' ');
                break;
            }

            result.Append(cell);

            // One cell per step, plus the continuation of a wide one. Never the rune's width:
            // that is what looped forever on a mark measuring zero.
            from += wide ? 2 : 1;
        }

        return result.ToString();
    }

    /// <summary>Takes the first cells of the text.</summary>
    /// <param name="length">How many cells to take.</param>
    /// <returns>The leading text.</returns>
    public string Take(int length) => Slice(0, length);

    /// <summary>
    /// Shortens text to fit a column, appending an ellipsis when anything was removed.
    /// </summary>
    /// <param name="width">The cells available.</param>
    /// <param name="ellipsis">
    /// The marker for removed text. Ranger uses <c>~</c> by default and <c>…</c> when
    /// <c>unicode_ellipsis</c> is on.
    /// </param>
    /// <returns>Text that fits within <paramref name="width"/> cells.</returns>
    public string Truncate(int width, string ellipsis = "~")
    {
        ArgumentNullException.ThrowIfNull(ellipsis);

        if (Width <= width)
        {
            return Text;
        }

        int ellipsisWidth = CellWidth.Of(ellipsis);
        return width <= ellipsisWidth
            ? Slice(0, width)
            : Slice(0, width - ellipsisWidth) + ellipsis;
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}
