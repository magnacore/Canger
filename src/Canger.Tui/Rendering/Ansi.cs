// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace Canger.Tui.Rendering;

/// <summary>
/// The escape sequences Canger writes to the terminal.
/// </summary>
/// <remarks>
/// These are the standard ECMA-48 control sequences plus the handful of widely supported private
/// modes that a full-screen application needs. They are kept in one place so that the wire format
/// is auditable and so <see cref="ScreenBuffer"/> can stay concerned with layout rather than with
/// byte sequences.
/// </remarks>
public static class Ansi
{
    /// <summary>Control Sequence Introducer.</summary>
    public const string Csi = "\e[";

    /// <summary>Returns colours and attributes to the terminal's defaults.</summary>
    public const string ResetStyle = $"{Csi}0m";

    /// <summary>Switches to the alternate screen, so the user's scrollback survives.</summary>
    public const string EnterAlternateScreen = $"{Csi}?1049h";

    /// <summary>Returns to the normal screen, restoring what was there before.</summary>
    public const string LeaveAlternateScreen = $"{Csi}?1049l";

    /// <summary>Hides the hardware cursor.</summary>
    public const string HideCursor = $"{Csi}?25l";

    /// <summary>Shows the hardware cursor.</summary>
    public const string ShowCursor = $"{Csi}?25h";

    /// <summary>Clears the whole screen.</summary>
    public const string ClearScreen = $"{Csi}2J";

    /// <summary>
    /// Reports mouse presses, releases and drags using the SGR encoding.
    /// </summary>
    /// <remarks>
    /// SGR (mode 1006) is used rather than the original X10 encoding because the latter packs
    /// coordinates into single bytes and so cannot address a terminal wider than 223 columns.
    /// </remarks>
    public const string EnableMouse = $"{Csi}?1000h{Csi}?1002h{Csi}?1006h";

    /// <summary>Stops mouse reporting.</summary>
    public const string DisableMouse = $"{Csi}?1006l{Csi}?1002l{Csi}?1000l";

    /// <summary>
    /// Wraps pasted text in markers, so a paste is not mistaken for typing.
    /// </summary>
    /// <remarks>
    /// Without this, pasting text containing <c>q</c> into the browser would quit, and a pasted
    /// newline would submit a half-finished command.
    /// </remarks>
    public const string EnableBracketedPaste = $"{Csi}?2004h";

    /// <summary>Stops bracketed paste.</summary>
    public const string DisableBracketedPaste = $"{Csi}?2004l";

    /// <summary>Moves the cursor. Rows and columns are one-based.</summary>
    /// <param name="row">One-based row.</param>
    /// <param name="column">One-based column.</param>
    /// <returns>The escape sequence.</returns>
    public static string MoveCursor(int row, int column) =>
        string.Create(CultureInfo.InvariantCulture, $"{Csi}{row};{column}H");

    /// <summary>Builds the sequence that selects a style.</summary>
    /// <param name="style">The colours and attributes.</param>
    /// <returns>The escape sequence.</returns>
    public static string SetStyle(CellStyle style)
    {
        StringBuilder sequence = new(Csi);

        // Reset first, so the sequence describes the style completely rather than depending on
        // whatever was in effect before.
        sequence.Append('0');

        CellAttributes attributes = style.Attributes;
        if (attributes.HasFlag(CellAttributes.Bold))
        {
            sequence.Append(";1");
        }

        if (attributes.HasFlag(CellAttributes.Dim))
        {
            sequence.Append(";2");
        }

        if (attributes.HasFlag(CellAttributes.Italic))
        {
            sequence.Append(";3");
        }

        if (attributes.HasFlag(CellAttributes.Underline))
        {
            sequence.Append(";4");
        }

        if (attributes.HasFlag(CellAttributes.Blink))
        {
            sequence.Append(";5");
        }

        if (attributes.HasFlag(CellAttributes.Reverse))
        {
            sequence.Append(";7");
        }

        if (attributes.HasFlag(CellAttributes.Invisible))
        {
            sequence.Append(";8");
        }

        AppendColor(sequence, style.Foreground, isForeground: true);
        AppendColor(sequence, style.Background, isForeground: false);

        return sequence.Append('m').ToString();
    }

    /// <summary>
    /// Appends a colour, choosing the shortest encoding the terminal will understand.
    /// </summary>
    private static void AppendColor(StringBuilder sequence, Color color, bool isForeground)
    {
        if (color.IsDefault)
        {
            // The leading reset already restored the default, so nothing need be said.
            return;
        }

        int index = color.Index;

        // The base sixteen have dedicated codes that even minimal terminals accept; anything
        // beyond needs the 256-colour form.
        if (index < 8)
        {
            sequence.Append(';').Append(
                ((isForeground ? 30 : 40) + index).ToString(CultureInfo.InvariantCulture));
        }
        else if (index < 16)
        {
            sequence.Append(';').Append(
                ((isForeground ? 90 : 100) + index - 8).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            sequence.Append(isForeground ? ";38;5;" : ";48;5;")
                    .Append(index.ToString(CultureInfo.InvariantCulture));
        }
    }
}
