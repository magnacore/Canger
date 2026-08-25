// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Widgets;

/// <summary>A rectangular region of the screen, in cells.</summary>
/// <param name="X">Left column.</param>
/// <param name="Y">Top row.</param>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>A region with no area.</summary>
    public static Rect Empty => default;

    /// <summary>Whether the region has any area at all.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>One past the rightmost column.</summary>
    public int Right => X + Width;

    /// <summary>One past the bottom row.</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether a position falls inside the region.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns><see langword="true"/> when the position is inside.</returns>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>This region inset by a margin on every side.</summary>
    /// <param name="margin">Cells to remove from each side.</param>
    /// <returns>The smaller region.</returns>
    public Rect Deflate(int margin) =>
        new(X + margin, Y + margin, Width - (margin * 2), Height - (margin * 2));
}

/// <summary>
/// Something that draws itself into a region of the screen.
/// </summary>
/// <remarks>
/// Widgets draw into a <see cref="ScreenBuffer"/> rather than to the terminal, which is what
/// makes them testable: a test lays one out at a chosen size, draws it, and asserts on the
/// resulting text and colours, with no terminal and no escape sequences involved.
/// </remarks>
public abstract class Widget
{
    /// <summary>Where the widget draws.</summary>
    public Rect Bounds { get; private set; }

    /// <summary>Whether the widget is drawn at all.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Places the widget.</summary>
    /// <param name="bounds">The region to draw into.</param>
    public virtual void Layout(Rect bounds) => Bounds = bounds;

    /// <summary>Draws the widget, if it is visible and has room.</summary>
    /// <param name="screen">Where to draw.</param>
    public void Render(ScreenBuffer screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (IsVisible && !Bounds.IsEmpty)
        {
            Draw(screen);
        }
    }

    /// <summary>Draws the widget's content.</summary>
    /// <param name="screen">Where to draw.</param>
    protected abstract void Draw(ScreenBuffer screen);
}
