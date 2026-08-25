// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace Canger.Tui.Rendering;

/// <summary>A run of text sharing one style.</summary>
/// <param name="Text">The characters.</param>
/// <param name="Style">How to draw them.</param>
public readonly record struct StyledRun(string Text, CellStyle Style);

/// <summary>
/// Reads the colour codes in text produced by another program, so it can be shown as it was
/// meant to look.
/// </summary>
/// <remarks>
/// <para>
/// Preview scripts lean on this heavily: <c>highlight</c>, <c>bat</c> and <c>jq</c> all emit
/// colour, and stripping it would leave a preview far less useful than the tools it came from.
/// </para>
/// <para>
/// Only the display attributes are interpreted. Cursor movement, screen clearing and anything
/// else that would take control of the terminal is discarded, because the text is being placed
/// inside a widget rather than printed.
/// </para>
/// </remarks>
public static class AnsiParser
{
    /// <summary>
    /// Splits text into runs, each with the style in effect for it.
    /// </summary>
    /// <param name="text">Text that may contain escape sequences.</param>
    /// <param name="initial">The style in effect before the text starts.</param>
    /// <returns>The runs, in order.</returns>
    public static IReadOnlyList<StyledRun> Parse(string text, CellStyle initial = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<StyledRun> runs = [];
        CellStyle style = initial;
        StringBuilder current = new();

        int index = 0;
        while (index < text.Length)
        {
            if (text[index] != '\e')
            {
                current.Append(text[index]);
                index++;
                continue;
            }

            CellStyle next = style;
            int consumed = ReadSequence(text, index, ref next, out _);

            if (consumed == 0)
            {
                // Not a sequence this understands; keep the character rather than losing it.
                current.Append(text[index]);
                index++;
                continue;
            }

            // Close the run before the style changes, so each run carries the style it was
            // written under rather than the one that follows it.
            if (current.Length > 0)
            {
                runs.Add(new StyledRun(current.ToString(), style));
                current.Clear();
            }

            style = next;
            index += consumed;
        }

        if (current.Length > 0)
        {
            runs.Add(new StyledRun(current.ToString(), style));
        }

        return runs;
    }

    /// <summary>
    /// Reads one escape sequence and applies it.
    /// </summary>
    /// <returns>How many characters it occupied, or zero when it is not a sequence.</returns>
    private static int ReadSequence(string text, int start, ref CellStyle style,
                                    out bool changesStyle)
    {
        changesStyle = false;

        if (start + 1 >= text.Length || text[start + 1] != '[')
        {
            return 0;
        }

        // A control sequence runs until its final byte, in the range @ to ~.
        int end = start + 2;
        while (end < text.Length && text[end] is not (>= '@' and <= '~'))
        {
            end++;
        }

        if (end >= text.Length)
        {
            return 0;
        }

        // Only the display attributes matter here; anything else is discarded rather than
        // allowed to move the cursor or clear the screen inside a widget.
        if (text[end] == 'm')
        {
            changesStyle = true;
            style = ApplyGraphics(text[(start + 2)..end], style);
        }

        return end - start + 1;
    }

    /// <summary>Applies a select-graphic-rendition parameter list.</summary>
    private static CellStyle ApplyGraphics(string parameters, CellStyle style)
    {
        // An empty parameter list means reset, as does an explicit zero.
        if (parameters.Length == 0)
        {
            return CellStyle.Default;
        }

        int[] codes =
        [
            .. parameters
                .Split(';')
                .Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture,
                                          out int value) ? value : 0),
        ];

        for (int i = 0; i < codes.Length; i++)
        {
            int code = codes[i];

            switch (code)
            {
                case 0:
                    style = CellStyle.Default;
                    break;

                case 1:
                    style = style.With(CellAttributes.Bold);
                    break;

                case 2:
                    style = style.With(CellAttributes.Dim);
                    break;

                case 3:
                    style = style.With(CellAttributes.Italic);
                    break;

                case 4:
                    style = style.With(CellAttributes.Underline);
                    break;

                case 5:
                    style = style.With(CellAttributes.Blink);
                    break;

                case 7:
                    style = style.With(CellAttributes.Reverse);
                    break;

                case 22:
                    style = style with
                    {
                        Attributes = style.Attributes & ~(CellAttributes.Bold | CellAttributes.Dim),
                    };
                    break;

                case 23:
                    style = style with { Attributes = style.Attributes & ~CellAttributes.Italic };
                    break;

                case 24:
                    style = style with { Attributes = style.Attributes & ~CellAttributes.Underline };
                    break;

                case 27:
                    style = style with { Attributes = style.Attributes & ~CellAttributes.Reverse };
                    break;

                case >= 30 and <= 37:
                    style = style.On(Color.FromIndex(code - 30));
                    break;

                case 38:
                    style = style.On(ReadExtendedColor(codes, ref i) ?? style.Foreground);
                    break;

                case 39:
                    style = style.On(Color.Default);
                    break;

                case >= 40 and <= 47:
                    style = style.Over(Color.FromIndex(code - 40));
                    break;

                case 48:
                    style = style.Over(ReadExtendedColor(codes, ref i) ?? style.Background);
                    break;

                case 49:
                    style = style.Over(Color.Default);
                    break;

                case >= 90 and <= 97:
                    style = style.On(Color.FromIndex(code - 90 + 8));
                    break;

                case >= 100 and <= 107:
                    style = style.Over(Color.FromIndex(code - 100 + 8));
                    break;

                default:
                    break;
            }
        }

        return style;
    }

    /// <summary>
    /// Reads a 256-colour or 24-bit colour parameter.
    /// </summary>
    /// <remarks>
    /// A 24-bit colour is reduced to the nearest entry in the 256-colour cube. Canger draws
    /// through a palette, and an approximation is better than discarding the colour entirely —
    /// syntax highlighting is largely still legible after the reduction.
    /// </remarks>
    private static Color? ReadExtendedColor(int[] codes, ref int index)
    {
        if (index + 1 >= codes.Length)
        {
            return null;
        }

        int kind = codes[index + 1];

        if (kind == 5 && index + 2 < codes.Length)
        {
            int value = codes[index + 2];
            index += 2;
            return value is >= 0 and <= 255 ? Color.FromIndex(value) : null;
        }

        if (kind == 2 && index + 4 < codes.Length)
        {
            int red = codes[index + 2];
            int green = codes[index + 3];
            int blue = codes[index + 4];
            index += 4;
            return Color.FromIndex(ToPaletteIndex(red, green, blue));
        }

        return null;
    }

    /// <summary>Finds the nearest 256-colour palette entry to a 24-bit colour.</summary>
    /// <param name="red">Red, 0 to 255.</param>
    /// <param name="green">Green, 0 to 255.</param>
    /// <param name="blue">Blue, 0 to 255.</param>
    /// <returns>The palette index.</returns>
    public static int ToPaletteIndex(int red, int green, int blue)
    {
        red = Math.Clamp(red, 0, 255);
        green = Math.Clamp(green, 0, 255);
        blue = Math.Clamp(blue, 0, 255);

        // A near-grey is better served by the palette's 24-step grey ramp than by the colour
        // cube, whose grey diagonal has only six steps.
        if (Math.Abs(red - green) < 12 && Math.Abs(green - blue) < 12)
        {
            int level = (red + green + blue) / 3;

            if (level < 8)
            {
                return 16;
            }

            if (level > 248)
            {
                return 231;
            }

            return 232 + ((level - 8) * 24 / 240);
        }

        static int Axis(int value) => value < 48 ? 0 : value < 115 ? 1 : (value - 35) / 40;

        return 16 + (36 * Axis(red)) + (6 * Axis(green)) + Axis(blue);
    }

    /// <summary>Removes every escape sequence, leaving the text alone.</summary>
    /// <param name="text">Text that may contain escape sequences.</param>
    /// <returns>The text with the sequences taken out.</returns>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!text.Contains('\e', StringComparison.Ordinal))
        {
            return text;
        }

        StringBuilder result = new(text.Length);
        CellStyle ignored = default;

        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\e')
            {
                int consumed = ReadSequence(text, index, ref ignored, out _);
                if (consumed > 0)
                {
                    index += consumed;
                    continue;
                }
            }

            result.Append(text[index]);
            index++;
        }

        return result.ToString();
    }
}
