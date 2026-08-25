// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// Shows text that is too big for its space, with somewhere to scroll.
/// </summary>
/// <remarks>
/// Used both for file previews in the third column and, in a full-screen form, for the output of
/// commands that produce more than a status line. Colour from the producing program is preserved,
/// because a syntax-highlighted preview with the highlighting stripped is markedly less useful.
/// </remarks>
public sealed class Pager(IColorScheme colorScheme) : Widget
{
    private IReadOnlyList<string> _lines = [];
    private int _verticalOffset;
    private int _horizontalOffset;

    /// <summary>The text being shown.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>How many lines it has.</summary>
    public int LineCount => _lines.Count;

    /// <summary>Which line is at the top.</summary>
    public int VerticalOffset => _verticalOffset;

    /// <summary>How far the view is scrolled sideways.</summary>
    public int HorizontalOffset => _horizontalOffset;

    /// <summary>Whether long lines wrap rather than running off the edge.</summary>
    public bool WrapLines { get; set; }

    /// <summary>Replaces the text and returns to the top.</summary>
    /// <param name="text">What to show.</param>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        _lines = [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        _verticalOffset = 0;
        _horizontalOffset = 0;
    }

    /// <summary>Forgets the text.</summary>
    public void Clear()
    {
        Text = string.Empty;
        _lines = [];
        _verticalOffset = 0;
        _horizontalOffset = 0;
    }

    /// <summary>Scrolls vertically, stopping at either end.</summary>
    /// <param name="lines">How far, negative to go up.</param>
    public void ScrollVertically(int lines)
    {
        // Leaving one line visible at the bottom is friendlier than allowing the view to scroll
        // past the end into blank space.
        int maximum = Math.Max(_lines.Count - 1, 0);
        _verticalOffset = Math.Clamp(_verticalOffset + lines, 0, maximum);
    }

    /// <summary>Scrolls sideways, stopping at the left edge.</summary>
    /// <param name="columns">How far, negative to go left.</param>
    public void ScrollHorizontally(int columns) =>
        _horizontalOffset = Math.Max(_horizontalOffset + columns, 0);

    /// <summary>Moves to the top or the bottom.</summary>
    /// <param name="toEnd">Whether to go to the bottom.</param>
    public void ScrollToEdge(bool toEnd) =>
        _verticalOffset = toEnd ? Math.Max(_lines.Count - 1, 0) : 0;

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        CellStyle baseStyle = colorScheme.Resolve(StyleContext.Of(ContextKey.InPager));
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, baseStyle);

        int row = 0;
        int line = _verticalOffset;

        while (row < Bounds.Height && line < _lines.Count)
        {
            row += DrawLine(screen, _lines[line], Bounds.Y + row, baseStyle);
            line++;
        }
    }

    /// <summary>Draws one line, and reports how many rows it took.</summary>
    private int DrawLine(ScreenBuffer screen, string text, int row, CellStyle baseStyle)
    {
        // Tabs are expanded here rather than passed through, because the terminal would apply
        // its own tab stops and the columns would no longer line up with the widget.
        string expanded = ExpandTabs(text);

        if (!WrapLines)
        {
            DrawRun(screen, expanded, Bounds.X, row, Bounds.Width, _horizontalOffset, baseStyle);
            return 1;
        }

        // Wrapping is done on the visible text, so a line's colour codes do not count towards
        // its width.
        int consumed = 0;
        int drawn = 0;
        int plainWidth = CellWidth.Of(AnsiParser.Strip(expanded));

        while (consumed < plainWidth && row + drawn < Bounds.Bottom)
        {
            DrawRun(screen, expanded, Bounds.X, row + drawn, Bounds.Width, consumed, baseStyle);
            consumed += Bounds.Width;
            drawn++;
        }

        return Math.Max(drawn, 1);
    }

    /// <summary>Draws part of a line, preserving the colours it carries.</summary>
    private static void DrawRun(ScreenBuffer screen, string text, int x, int row, int width,
                                int skipColumns, CellStyle baseStyle)
    {
        int column = 0;
        int drawn = 0;

        foreach (StyledRun run in AnsiParser.Parse(text, baseStyle))
        {
            foreach (System.Text.Rune rune in run.Text.EnumerateRunes())
            {
                int runeWidth = CellWidth.Of(rune);

                // Skipping is counted in cells, so scrolling sideways past a wide character
                // lands where it looks like it should.
                if (column + runeWidth <= skipColumns)
                {
                    column += runeWidth;
                    continue;
                }

                if (drawn >= width)
                {
                    return;
                }

                screen.Set(x + drawn, row, rune, run.Style);
                drawn += runeWidth;
                column += runeWidth;
            }
        }
    }

    /// <summary>Replaces tabs with spaces up to the next tab stop.</summary>
    private static string ExpandTabs(string text, int tabWidth = 4)
    {
        if (!text.Contains('\t', StringComparison.Ordinal))
        {
            return text;
        }

        System.Text.StringBuilder result = new(text.Length + tabWidth);
        int column = 0;

        foreach (char character in text)
        {
            if (character == '\t')
            {
                int spaces = tabWidth - (column % tabWidth);
                result.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                result.Append(character);
                column++;
            }
        }

        return result.ToString();
    }
}
