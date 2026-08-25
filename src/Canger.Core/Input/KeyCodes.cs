// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>
/// The numeric key codes Canger works in, and the sentinels that stand for wildcards rather than
/// real keys.
/// </summary>
/// <remarks>
/// <para>
/// Codes below 256 are raw input bytes, not Unicode code points. That looks like a limitation
/// and is in fact deliberate: it is what lets a binding for a non-ASCII key work at all. A
/// multi-byte character arrives as several bytes, so a binding written for it matches the same
/// byte sequence that arrives, and reassembling text into characters is left to the console,
/// which is the only place that needs whole characters. Ranger reaches the same arrangement by
/// re-encoding bindings through latin-1 (<c>ext/keybinding_parser.py:178-185</c>).
/// </para>
/// <para>
/// Codes from 256 upward name keys that produce no bytes of their own — arrows, function keys,
/// page up and so on — and use the same numbering as curses, so that a binding written against
/// a raw code such as <c>&lt;330&gt;</c> means what it means in ranger.
/// </para>
/// </remarks>
public static class KeyCodes
{
    /// <summary>Matches any key. Written <c>&lt;any&gt;</c>.</summary>
    /// <remarks>
    /// An exact binding always beats this, and <see cref="Escape"/> never matches it, so that
    /// pressing Escape can always abandon a partly typed sequence.
    /// </remarks>
    public const int Any = 9001;

    /// <summary>
    /// Marks a binding that fires as soon as its prefix is reached, without ending the sequence.
    /// Written <c>&lt;bg&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This is how <c>map m&lt;bg&gt; draw_bookmarks</c> shows the bookmark overlay the moment
    /// <c>m</c> is pressed while <c>m&lt;any&gt;</c> still waits for the bookmark letter.
    /// </remarks>
    public const int PassiveAction = 9002;

    /// <summary>Prefix emitted before the key an Alt combination modifies.</summary>
    public const int Alt = 9003;

    /// <summary>
    /// Marks the pseudo-binding that enables or disables numeric prefixes in a key map. Written
    /// <c>&lt;allow_quantifiers&gt;</c>.
    /// </summary>
    public const int AllowQuantifiers = 9004;

    /// <summary>No key. Reported when input ends or a read is interrupted.</summary>
    public const int None = -1;

    /// <summary>Escape.</summary>
    public const int Escape = 27;

    /// <summary>Space.</summary>
    public const int Space = 32;

    /// <summary>Tab, which is also Ctrl-I.</summary>
    public const int Tab = 9;

    /// <summary>Line feed, which is also Ctrl-J and what Enter reports.</summary>
    public const int Enter = 10;

    /// <summary>Cursor up.</summary>
    public const int Up = 259;

    /// <summary>Cursor down.</summary>
    public const int Down = 258;

    /// <summary>Cursor left.</summary>
    public const int Left = 260;

    /// <summary>Cursor right.</summary>
    public const int Right = 261;

    /// <summary>Home.</summary>
    public const int Home = 262;

    /// <summary>End.</summary>
    public const int End = 360;

    /// <summary>Page up.</summary>
    public const int PageUp = 339;

    /// <summary>Page down.</summary>
    public const int PageDown = 338;

    /// <summary>Insert.</summary>
    public const int Insert = 331;

    /// <summary>Delete.</summary>
    public const int Delete = 330;

    /// <summary>Shift held with Delete.</summary>
    public const int ShiftDelete = 383;

    /// <summary>Backspace.</summary>
    public const int Backspace = 263;

    /// <summary>Shift held with Tab, which terminals report as back-tab.</summary>
    public const int ShiftTab = 353;

    /// <summary>The code of the first function key, F0.</summary>
    public const int FirstFunctionKey = 264;

    /// <summary>The code for a numbered function key.</summary>
    /// <param name="number">The function key number, where F1 is 1.</param>
    /// <returns>The key code.</returns>
    public static int FunctionKey(int number) => FirstFunctionKey + number;

    /// <summary>The lowest code reserved for keys that produce no input bytes.</summary>
    public const int FirstSpecial = 256;

    /// <summary>Whether a code is one of the wildcard sentinels rather than a real key.</summary>
    /// <param name="key">The key code.</param>
    /// <returns><see langword="true"/> for the sentinels.</returns>
    public static bool IsSentinel(int key) => key is >= Any and <= AllowQuantifiers;

    /// <summary>
    /// Renders a key code the way it is shown back to the user, in the key buffer on the title
    /// bar and in the hint list.
    /// </summary>
    /// <remarks>
    /// Printable ASCII renders as itself; anything else renders as a bracketed name, falling back
    /// to the bracketed number when the code has no name. Note that space is <em>not</em>
    /// printable by this rule, so it renders as <c>&lt;space&gt;</c> and stays visible.
    /// </remarks>
    /// <param name="key">The key code.</param>
    /// <returns>The display string.</returns>
    public static string ToDisplayString(int key)
    {
        if (key is >= 33 and < 127)
        {
            return ((char)key).ToString();
        }

        return SpecialKeys.Names.TryGetValue(key, out string? name)
            ? $"<{name}>"
            : $"<{key.ToString(System.Globalization.CultureInfo.InvariantCulture)}>";
    }

    /// <summary>Renders a whole key sequence the way the title bar shows it.</summary>
    /// <param name="keys">The key codes.</param>
    /// <returns>The concatenated display string.</returns>
    public static string ToDisplayString(IEnumerable<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return string.Concat(keys.Select(ToDisplayString));
    }
}
