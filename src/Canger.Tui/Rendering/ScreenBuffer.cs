// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Tui.Text;

namespace Canger.Tui.Rendering;

/// <summary>One cell of the screen.</summary>
/// <param name="Rune">The character drawn here, or a space.</param>
/// <param name="Style">The colours and attributes.</param>
/// <param name="IsContinuation">
/// Whether this cell is the second half of a wide character that starts in the cell before it.
/// Nothing is drawn for it; it exists so that the grid and the screen stay the same shape.
/// </param>
public readonly record struct Cell(Rune Rune, CellStyle Style, bool IsContinuation = false)
{
    /// <summary>An empty cell in the terminal's own colours.</summary>
    public static Cell Blank => new(new Rune(' '), CellStyle.Default);
}

/// <summary>
/// A grid of terminal cells that widgets draw into, and which knows how to turn itself into the
/// smallest set of escape sequences that will bring the real screen up to date.
/// </summary>
/// <remarks>
/// <para>
/// Everything Canger displays is composed here first and sent to the terminal afterwards. That
/// buys two things. Drawing becomes testable — a widget test renders into a buffer and asserts
/// on the text and colours, with no terminal, no escape sequences and no timing. And output
/// becomes cheap, because <see cref="Flush"/> compares against the previous frame and emits only
/// what changed, instead of repainting the screen on every keystroke.
/// </para>
/// </remarks>
public sealed class ScreenBuffer
{
    private Cell[] _current;
    private Cell[] _previous;

    /// <summary>Creates a buffer of a given size, filled with blanks.</summary>
    /// <param name="width">Columns.</param>
    /// <param name="height">Rows.</param>
    public ScreenBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        Width = width;
        Height = height;
        _current = NewGrid(width, height);
        _previous = NewGrid(width, height);

        // The previous frame starts deliberately different from the current one so that the
        // first flush paints everything rather than assuming the screen is already blank.
        Array.Fill(_previous, new Cell(default, CellStyle.Default));
    }

    /// <summary>Columns.</summary>
    public int Width { get; private set; }

    /// <summary>Rows.</summary>
    public int Height { get; private set; }

    /// <summary>Reads one cell.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>The cell, or a blank when the position is outside the buffer.</returns>
    public Cell this[int x, int y] =>
        Contains(x, y) ? _current[(y * Width) + x] : Cell.Blank;

    /// <summary>Whether a position is inside the buffer.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns><see langword="true"/> when the position exists.</returns>
    public bool Contains(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Resizes the buffer, discarding its contents.</summary>
    /// <param name="width">Columns.</param>
    /// <param name="height">Rows.</param>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        _current = NewGrid(width, height);
        _previous = NewGrid(width, height);

        // Force a full repaint: the terminal's contents no longer correspond to anything known.
        Array.Fill(_previous, new Cell(default, CellStyle.Default));
    }

    /// <summary>Fills the whole buffer with blanks in a style.</summary>
    /// <param name="style">The style to fill with.</param>
    public void Clear(CellStyle style = default) => Fill(0, 0, Width, Height, style);

    /// <summary>Fills a rectangle with blanks in a style.</summary>
    /// <param name="x">Left column.</param>
    /// <param name="y">Top row.</param>
    /// <param name="width">Columns to fill.</param>
    /// <param name="height">Rows to fill.</param>
    /// <param name="style">The style to fill with.</param>
    public void Fill(int x, int y, int width, int height, CellStyle style = default)
    {
        Cell blank = new(new Rune(' '), style);

        for (int row = Math.Max(y, 0); row < Math.Min(y + height, Height); row++)
        {
            for (int column = Math.Max(x, 0); column < Math.Min(x + width, Width); column++)
            {
                _current[(row * Width) + column] = blank;
            }
        }
    }

    /// <summary>Draws a run of one character across a row.</summary>
    /// <param name="x">Where the run starts.</param>
    /// <param name="y">The row.</param>
    /// <param name="width">How many cells.</param>
    /// <param name="style">The style.</param>
    /// <param name="rune">
    /// The character to repeat; a horizontal box-drawing line by default, since that is what
    /// every caller so far wants.
    /// </param>
    public void HorizontalLine(int x, int y, int width, CellStyle style = default,
                               Rune? rune = null)
    {
        Cell cell = new(rune ?? BoxDrawing.HorizontalRune, style);

        if (y < 0 || y >= Height)
        {
            return;
        }

        for (int column = Math.Max(x, 0); column < Math.Min(x + width, Width); column++)
        {
            _current[(y * Width) + column] = cell;
        }
    }

    /// <summary>Draws a run of one character down a column.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">Where the run starts.</param>
    /// <param name="height">How many cells.</param>
    /// <param name="style">The style.</param>
    /// <param name="rune">
    /// The character to repeat; a vertical box-drawing line by default.
    /// </param>
    public void VerticalLine(int x, int y, int height, CellStyle style = default,
                             Rune? rune = null)
    {
        Cell cell = new(rune ?? BoxDrawing.VerticalRune, style);

        if (x < 0 || x >= Width)
        {
            return;
        }

        for (int row = Math.Max(y, 0); row < Math.Min(y + height, Height); row++)
        {
            _current[(row * Width) + x] = cell;
        }
    }

    /// <summary>Draws a single character.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="rune">The character.</param>
    /// <param name="style">The style.</param>
    /// <returns>How many cells were used: two for a wide character, otherwise one.</returns>
    public int Set(int x, int y, Rune rune, CellStyle style = default)
    {
        int width = CellWidth.Of(rune);

        if (!Contains(x, y))
        {
            return width;
        }

        // A wide character with only one cell left would overhang the edge, so a space is drawn
        // instead. This keeps the row exactly Width cells and stops the terminal from wrapping.
        if (width == 2 && x + 1 >= Width)
        {
            _current[(y * Width) + x] = new Cell(new Rune(' '), style);
            return width;
        }

        _current[(y * Width) + x] = new Cell(rune, style);

        if (width == 2)
        {
            _current[(y * Width) + x + 1] = new Cell(new Rune(' '), style, IsContinuation: true);
        }

        return width;
    }

    /// <summary>Draws text, stopping at the right edge.</summary>
    /// <param name="x">Column to start at.</param>
    /// <param name="y">Row.</param>
    /// <param name="text">The text.</param>
    /// <param name="style">The style.</param>
    /// <returns>How many cells were used.</returns>
    public int Write(int x, int y, string text, CellStyle style = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        int column = x;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (column >= Width)
            {
                break;
            }

            column += Set(column, y, rune, style);
        }

        return column - x;
    }

    /// <summary>
    /// Recolours cells without changing their text, which is how the progress bar tints the
    /// status bar and how the task view highlights a row.
    /// </summary>
    /// <param name="x">Left column.</param>
    /// <param name="y">Row.</param>
    /// <param name="width">Columns to recolour.</param>
    /// <param name="style">The new style.</param>
    public void Recolor(int x, int y, int width, CellStyle style)
    {
        if (y < 0 || y >= Height)
        {
            return;
        }

        for (int column = Math.Max(x, 0); column < Math.Min(x + width, Width); column++)
        {
            _current[(y * Width) + column] = _current[(y * Width) + column] with { Style = style };
        }
    }

    /// <summary>
    /// The text of one row, for tests and for debugging.
    /// </summary>
    /// <param name="y">Row.</param>
    /// <returns>The row's text, with continuation cells omitted and trailing blanks kept.</returns>
    public string TextAt(int y)
    {
        if (y < 0 || y >= Height)
        {
            return string.Empty;
        }

        StringBuilder text = new(Width);
        for (int x = 0; x < Width; x++)
        {
            Cell cell = _current[(y * Width) + x];
            if (!cell.IsContinuation)
            {
                text.Append(cell.Rune.ToString());
            }
        }

        return text.ToString();
    }

    /// <summary>Every row's text, for tests and for debugging.</summary>
    /// <returns>One string per row.</returns>
    public IReadOnlyList<string> Snapshot() =>
        [.. Enumerable.Range(0, Height).Select(TextAt)];

    /// <summary>
    /// Writes the escape sequences that bring the terminal up to date, then adopts the current
    /// contents as the baseline for the next comparison.
    /// </summary>
    /// <param name="output">Receives the escape sequences.</param>
    /// <param name="force">
    /// Repaints everything rather than only what changed. Needed after the terminal has been
    /// disturbed by something Canger did not draw, such as an external program or an image.
    /// </param>
    public void Flush(StringBuilder output, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(output);

        CellStyle? activeStyle = null;
        int cursorX = -1;
        int cursorY = -1;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = (y * Width) + x;
                Cell cell = _current[index];

                if (cell.IsContinuation)
                {
                    continue;
                }

                if (!force && _previous[index] == cell)
                {
                    continue;
                }

                // Only move the cursor when it is not already where it needs to be, so a run of
                // changed cells costs one move rather than one per cell.
                if (cursorY != y || cursorX != x)
                {
                    output.Append(Ansi.MoveCursor(y + 1, x + 1));
                    cursorX = x;
                    cursorY = y;
                }

                if (activeStyle != cell.Style)
                {
                    output.Append(Ansi.SetStyle(cell.Style));
                    activeStyle = cell.Style;
                }

                output.Append(cell.Rune.ToString());
                cursorX += CellWidth.Of(cell.Rune);
            }
        }

        if (activeStyle is not null)
        {
            output.Append(Ansi.ResetStyle);
        }

        // Swap the buffers, then copy rather than re-allocate: `_current = [.. _previous]` built a
        // fresh Cell[Width*Height] on every flush — around 96 KB for a 120x40 terminal, handed
        // straight to the collector.
        (_previous, _current) = (_current, _previous);
        Array.Copy(_previous, _current, _previous.Length);
    }

    private static Cell[] NewGrid(int width, int height)
    {
        Cell[] grid = new Cell[width * height];
        Array.Fill(grid, Cell.Blank);
        return grid;
    }
}
