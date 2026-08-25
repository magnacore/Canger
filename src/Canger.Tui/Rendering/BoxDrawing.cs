// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Tui.Rendering;

/// <summary>
/// The line-drawing characters used for borders.
/// </summary>
/// <remarks>
/// Unicode box-drawing rather than the ASCII approximations <c>-</c>, <c>|</c> and <c>+</c>:
/// terminals have rendered these for decades and they join cleanly at corners, which <c>+</c>
/// never quite does. They are all single-width, so a border costs exactly one cell.
/// </remarks>
public static class BoxDrawing
{
    /// <summary>A horizontal line.</summary>
    public const string Horizontal = "─";

    /// <summary>A vertical line.</summary>
    public const string Vertical = "│";

    /// <summary>The top-left corner.</summary>
    public const string TopLeft = "┌";

    /// <summary>The top-right corner.</summary>
    public const string TopRight = "┐";

    /// <summary>The bottom-left corner.</summary>
    public const string BottomLeft = "└";

    /// <summary>The bottom-right corner.</summary>
    public const string BottomRight = "┘";

    /// <summary>Where a vertical line meets the top edge.</summary>
    public const string TopTee = "┬";

    /// <summary>Where a vertical line meets the bottom edge.</summary>
    public const string BottomTee = "┴";

    /// <summary>A horizontal line, as a rune.</summary>
    public static Rune HorizontalRune { get; } = new(0x2500);

    /// <summary>A vertical line, as a rune.</summary>
    public static Rune VerticalRune { get; } = new(0x2502);
}
