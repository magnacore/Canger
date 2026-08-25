// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Tui.Rendering;

/// <summary>The colours and attributes a cell is drawn with.</summary>
/// <param name="Foreground">The text colour.</param>
/// <param name="Background">The background colour.</param>
/// <param name="Attributes">Bold, underline and the rest.</param>
public readonly record struct CellStyle(
    Color Foreground,
    Color Background,
    CellAttributes Attributes = CellAttributes.None)
{
    /// <summary>The terminal's own colours with no attributes.</summary>
    public static CellStyle Default => new(Color.Default, Color.Default);

    /// <summary>This style with attributes added.</summary>
    /// <param name="attributes">The attributes to add.</param>
    /// <returns>The combined style.</returns>
    public CellStyle With(CellAttributes attributes) =>
        this with { Attributes = Attributes | attributes };

    /// <summary>This style with a different foreground.</summary>
    /// <param name="color">The text colour.</param>
    /// <returns>The new style.</returns>
    public CellStyle On(Color color) => this with { Foreground = color };

    /// <summary>This style with a different background.</summary>
    /// <param name="color">The background colour.</param>
    /// <returns>The new style.</returns>
    public CellStyle Over(Color color) => this with { Background = color };
}
