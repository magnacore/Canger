// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Tui.Rendering;

/// <summary>Text attributes a terminal cell can carry, beyond its colours.</summary>
[Flags]
public enum CellAttributes
{
    /// <summary>No attributes.</summary>
    None = 0,

    /// <summary>Bold, which many terminals render as a brighter colour.</summary>
    Bold = 1 << 0,

    /// <summary>Faint.</summary>
    Dim = 1 << 1,

    /// <summary>Italic.</summary>
    Italic = 1 << 2,

    /// <summary>Underlined.</summary>
    Underline = 1 << 3,

    /// <summary>Blinking.</summary>
    Blink = 1 << 4,

    /// <summary>Foreground and background swapped, which is how the cursor row is drawn.</summary>
    Reverse = 1 << 5,

    /// <summary>Hidden.</summary>
    Invisible = 1 << 6,
}
