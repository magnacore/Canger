// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Core.Input;

/// <summary>
/// Turns a key binding as written in cc.conf — <c>gg</c>, <c>&lt;C-a&gt;</c>, <c>p'&lt;any&gt;</c>
/// — into the sequence of key codes that must arrive to trigger it.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is a single-pass scan, faithful to
/// <c>ranger/ext/keybinding_parser.py:76-131</c>. Outside angle brackets every character stands
/// for itself and case matters, so <c>gg</c> and <c>GG</c> are different bindings. Inside angle
/// brackets the name is looked up case-insensitively.
/// </para>
/// <para>
/// Three fallbacks make the notation forgiving, and each is relied on by real configurations:
/// </para>
/// <list type="bullet">
///   <item><description>
///     A bracketed name that is all digits is the raw key code, so <c>&lt;330&gt;</c> binds the
///     Delete key by number. This is what makes <see cref="KeyCodes.ToDisplayString(int)"/>
///     round-trip for codes that have no name.
///   </description></item>
///   <item><description>
///     A bracketed name that is not recognised yields its literal characters, angle brackets
///     included, so a stray <c>&lt;</c> in a binding is harmless rather than an error.
///   </description></item>
///   <item><description>
///     An unterminated bracket at the end of the input yields the <c>&lt;</c> and whatever
///     followed it, with no closing bracket.
///   </description></item>
/// </list>
/// <para>
/// Use <c>&lt;lt&gt;</c> to bind a literal <c>&lt;</c>.
/// </para>
/// </remarks>
public static class KeyBindingParser
{
    /// <summary>
    /// Parses a key binding into its key codes.
    /// </summary>
    /// <param name="binding">The binding as written, for example <c>"x&lt;A-Left&gt;"</c>.</param>
    /// <returns>The key codes that must arrive in order, for example <c>[120, 9003, 260]</c>.</returns>
    public static int[] Parse(string binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        List<int> keys = [];

        // Bindings are matched against raw input bytes rather than characters, so that a
        // binding for a non-ASCII key matches the byte sequence that key actually produces.
        byte[] bytes = Encoding.UTF8.GetBytes(binding);

        StringBuilder bracket = new();
        bool inBracket = false;

        foreach (byte value in bytes)
        {
            char character = (char)value;

            if (!inBracket)
            {
                if (character == '<')
                {
                    inBracket = true;
                    bracket.Clear();
                }
                else
                {
                    keys.Add(value);
                }

                continue;
            }

            if (character != '>')
            {
                bracket.Append(character);
                continue;
            }

            inBracket = false;
            AppendBracketed(keys, bracket.ToString());
        }

        if (inBracket)
        {
            // An unterminated bracket contributes its opening character and its contents.
            keys.Add('<');
            keys.AddRange(bracket.ToString().Select(c => (int)c));
        }

        return [.. keys];
    }

    /// <summary>Resolves the contents of one <c>&lt;...&gt;</c> group.</summary>
    private static void AppendBracketed(List<int> keys, string content)
    {
        string name = content.ToLowerInvariant();

        if (SpecialKeys.Sequence.TryGetValue(name, out int[]? sequence))
        {
            keys.AddRange(sequence);
            return;
        }

        if (SpecialKeys.Single.TryGetValue(name, out int single))
        {
            keys.Add(single);
            return;
        }

        if (content.Length > 0 && content.All(char.IsAsciiDigit) &&
            int.TryParse(content, System.Globalization.NumberStyles.None,
                         System.Globalization.CultureInfo.InvariantCulture, out int raw))
        {
            keys.Add(raw);
            return;
        }

        // Not a name and not a number: hand the characters back verbatim, brackets included.
        keys.Add('<');
        keys.AddRange(content.Select(c => (int)c));
        keys.Add('>');
    }
}
