// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Tui.Rendering;

/// <summary>
/// A terminal colour: the terminal's own default, one of the sixteen palette colours, or an
/// index into the 256-colour cube.
/// </summary>
/// <remarks>
/// All three forms are needed. Colourschemes written against the base palette use 0 to 7 and add
/// 8 for the bright variants, the solarized scheme uses raw 256-colour indices such as 166 and
/// 244, and <see cref="Default"/> lets a cell inherit whatever the user configured their
/// terminal to use, which is what makes Canger look at home in any theme.
/// </remarks>
public readonly record struct Color
{
    /// <summary>
    /// The palette index plus one, so that the all-zero value of the struct is the terminal
    /// default rather than black.
    /// </summary>
    /// <remarks>
    /// This offset is the whole reason the field is private. Styles are routinely passed as
    /// <c>default</c>, and a struct's default value is all zero bits; without the offset that
    /// would mean palette index 0 on index 0, and every unstyled cell would be drawn black on
    /// black. Storing index + 1 makes "unspecified" and "the terminal's own colour" the same
    /// thing, which is what callers already assume.
    /// </remarks>
    private readonly int _offsetIndex;

    private Color(int index) => _offsetIndex = index + 1;

    /// <summary>The palette index, or -1 for the terminal's default.</summary>
    public int Index => _offsetIndex - 1;

    /// <summary>The terminal's own foreground or background colour.</summary>
    public static Color Default => default;

    /// <summary>Whether this is the terminal's default rather than a chosen colour.</summary>
    public bool IsDefault => _offsetIndex == 0;

    /// <summary>Creates a colour from a palette index.</summary>
    /// <param name="index">0 to 255, or -1 for the terminal default.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the palette.</exception>
    public static Color FromIndex(int index)
    {
        if (index is < -1 or > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, "A colour index must be -1 for default, or 0 to 255.");
        }

        return new Color(index);
    }

    /// <summary>Black.</summary>
    public static Color Black => new(0);

    /// <summary>Red.</summary>
    public static Color Red => new(1);

    /// <summary>Green.</summary>
    public static Color Green => new(2);

    /// <summary>Yellow.</summary>
    public static Color Yellow => new(3);

    /// <summary>Blue.</summary>
    public static Color Blue => new(4);

    /// <summary>Magenta.</summary>
    public static Color Magenta => new(5);

    /// <summary>Cyan.</summary>
    public static Color Cyan => new(6);

    /// <summary>White.</summary>
    public static Color White => new(7);

    /// <summary>
    /// The bright variant of one of the eight base colours.
    /// </summary>
    /// <remarks>
    /// Colourschemes traditionally express this by adding 8 to the index. Doing it through a
    /// method instead means brightening is idempotent: ranger's <c>fg += BRIGHT</c> idiom
    /// silently produces a different colour if it is ever applied twice.
    /// </remarks>
    /// <returns>The bright variant, or this colour unchanged when it has no bright form.</returns>
    public Color Bright() => Index is >= 0 and < 8 ? new Color(Index + 8) : this;

    /// <inheritdoc />
    public override string ToString() =>
        IsDefault ? "default" : Index.ToString(CultureInfo.InvariantCulture);
}
