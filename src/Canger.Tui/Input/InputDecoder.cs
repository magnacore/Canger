// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using Canger.Core.Input;

namespace Canger.Tui.Input;

/// <summary>
/// Turns the bytes a terminal sends into key, mouse and paste events.
/// </summary>
/// <remarks>
/// <para>
/// A terminal reports everything down one byte stream. Ordinary keys arrive as themselves, but
/// arrows, function keys, mouse movements and pastes all arrive as escape sequences, and those
/// sequences can be split across reads. This class is therefore a state machine fed bytes as
/// they arrive, emitting events only once it has enough to be sure.
/// </para>
/// <para>
/// The one genuine ambiguity is a lone <c>ESC</c>: it may be the Escape key, or the start of a
/// sequence whose remaining bytes have not arrived yet. Terminals do not distinguish the two, so
/// the decoder holds a pending <c>ESC</c> until either more bytes arrive or the caller decides
/// enough time has passed and calls <see cref="Flush"/>. Ranger resolves the same ambiguity with
/// curses' <c>ESCDELAY</c>, set to 25 milliseconds.
/// </para>
/// </remarks>
public sealed class InputDecoder
{
    private const int Escape = 0x1B;

    /// <summary>Key codes for the letters that end a cursor-key sequence.</summary>
    private static readonly Dictionary<char, int> FinalKeys = new()
    {
        ['A'] = KeyCodes.Up,
        ['B'] = KeyCodes.Down,
        ['C'] = KeyCodes.Right,
        ['D'] = KeyCodes.Left,
        ['H'] = KeyCodes.Home,
        ['F'] = KeyCodes.End,
        ['Z'] = KeyCodes.ShiftTab,
        ['P'] = KeyCodes.FunctionKey(1),
        ['Q'] = KeyCodes.FunctionKey(2),
        ['R'] = KeyCodes.FunctionKey(3),
        ['S'] = KeyCodes.FunctionKey(4),
    };

    /// <summary>Key codes for the numbered <c>ESC [ n ~</c> sequences.</summary>
    private static readonly Dictionary<int, int> TildeKeys = new()
    {
        [1] = KeyCodes.Home,
        [2] = KeyCodes.Insert,
        [3] = KeyCodes.Delete,
        [4] = KeyCodes.End,
        [5] = KeyCodes.PageUp,
        [6] = KeyCodes.PageDown,
        [7] = KeyCodes.Home,
        [8] = KeyCodes.End,
        [11] = KeyCodes.FunctionKey(1),
        [12] = KeyCodes.FunctionKey(2),
        [13] = KeyCodes.FunctionKey(3),
        [14] = KeyCodes.FunctionKey(4),
        [15] = KeyCodes.FunctionKey(5),
        [17] = KeyCodes.FunctionKey(6),
        [18] = KeyCodes.FunctionKey(7),
        [19] = KeyCodes.FunctionKey(8),
        [20] = KeyCodes.FunctionKey(9),
        [21] = KeyCodes.FunctionKey(10),
        [23] = KeyCodes.FunctionKey(11),
        [24] = KeyCodes.FunctionKey(12),
    };

    private readonly List<byte> _pending = [];
    private readonly StringBuilder _paste = new();
    private bool _inPaste;

    /// <summary>Whether the decoder is holding bytes it has not yet been able to interpret.</summary>
    /// <remarks>
    /// When this is true and no more input is arriving, the caller should wait briefly and then
    /// call <see cref="Flush"/>, which resolves a lone escape into the Escape key.
    /// </remarks>
    public bool HasPendingInput => _pending.Count > 0;

    /// <summary>
    /// Feeds bytes to the decoder.
    /// </summary>
    /// <param name="bytes">Bytes as read from the terminal.</param>
    /// <returns>The events those bytes completed, in order.</returns>
    public IReadOnlyList<InputEvent> Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            _pending.Add(value);
        }

        List<InputEvent> events = [];

        while (_pending.Count > 0)
        {
            if (!TryDecode(events, out int consumed))
            {
                // Not enough bytes yet; keep them for the next read.
                break;
            }

            _pending.RemoveRange(0, consumed);
        }

        return events;
    }

    /// <summary>
    /// Resolves whatever is still pending, on the assumption that no more of it is coming.
    /// </summary>
    /// <remarks>
    /// Call this after a short wait with no input. It is what turns a lone escape byte into the
    /// Escape key rather than leaving it held forever waiting for a sequence that will never
    /// arrive.
    /// </remarks>
    /// <returns>The events the pending bytes resolved to.</returns>
    public IReadOnlyList<InputEvent> Flush()
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        List<InputEvent> events = [];

        // An escape that never became a sequence is the Escape key. Anything else pending is a
        // sequence the decoder does not recognise, and is dropped rather than typed as text.
        if (_pending[0] == Escape && _pending.Count == 1)
        {
            events.Add(new KeyEvent(KeyCodes.Escape));
        }
        else if (_pending[0] == Escape && _pending.Count == 2)
        {
            // Escape immediately followed by a key is how terminals report Alt.
            events.Add(new KeyEvent(KeyCodes.Alt));
            events.Add(new KeyEvent(_pending[1]));
        }

        _pending.Clear();
        return events;
    }

    /// <summary>
    /// Attempts to decode one event from the front of the pending bytes.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when more bytes are needed, in which case nothing is consumed.
    /// </returns>
    private bool TryDecode(List<InputEvent> events, out int consumed)
    {
        consumed = 0;

        if (_inPaste)
        {
            return TryDecodePasteBody(events, out consumed);
        }

        byte first = _pending[0];

        if (first != Escape)
        {
            events.Add(new KeyEvent(first));
            consumed = 1;
            return true;
        }

        if (_pending.Count == 1)
        {
            // Could be the Escape key, could be the start of a sequence. Wait and see.
            return false;
        }

        return _pending[1] switch
        {
            (byte)'[' => TryDecodeCsi(events, out consumed),
            (byte)'O' => TryDecodeSs3(events, out consumed),
            _ => TryDecodeAlt(events, out consumed),
        };
    }

    /// <summary>Decodes <c>ESC O x</c>, the alternative encoding for cursor and function keys.</summary>
    private bool TryDecodeSs3(List<InputEvent> events, out int consumed)
    {
        consumed = 0;

        if (_pending.Count < 3)
        {
            return false;
        }

        char final = (char)_pending[2];
        consumed = 3;

        if (FinalKeys.TryGetValue(final, out int key))
        {
            events.Add(new KeyEvent(key));
        }

        return true;
    }

    /// <summary>Decodes <c>ESC x</c>, which is how a terminal reports Alt held with a key.</summary>
    private bool TryDecodeAlt(List<InputEvent> events, out int consumed)
    {
        events.Add(new KeyEvent(KeyCodes.Alt));
        events.Add(new KeyEvent(_pending[1]));
        consumed = 2;
        return true;
    }

    /// <summary>Decodes a control sequence: <c>ESC [</c> followed by parameters and a final byte.</summary>
    private bool TryDecodeCsi(List<InputEvent> events, out int consumed)
    {
        consumed = 0;

        // Scan for the final byte, which is the first in the range @ to ~.
        int end = -1;
        for (int i = 2; i < _pending.Count; i++)
        {
            if (_pending[i] is >= 0x40 and <= 0x7E)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return false;
        }

        string body = Encoding.ASCII.GetString([.. _pending.GetRange(2, end - 2)]);
        char final = (char)_pending[end];
        consumed = end + 1;

        // Mouse reports use the SGR encoding, introduced by '<'.
        if (body.StartsWith('<') && final is 'M' or 'm')
        {
            DecodeMouse(events, body[1..], isPress: final == 'M');
            return true;
        }

        if (final == '~')
        {
            DecodeTilde(events, body);
            return true;
        }

        if (FinalKeys.TryGetValue(final, out int key))
        {
            events.Add(new KeyEvent(key));
        }

        return true;
    }

    /// <summary>Decodes <c>ESC [ n ~</c>, including the paste markers and modified keys.</summary>
    private void DecodeTilde(List<InputEvent> events, string body)
    {
        string[] parameters = body.Split(';');

        if (!int.TryParse(parameters[0], NumberStyles.None, CultureInfo.InvariantCulture,
                          out int number))
        {
            return;
        }

        // 200 and 201 bracket pasted text rather than naming a key.
        if (number == 200)
        {
            _inPaste = true;
            _paste.Clear();
            return;
        }

        if (number == 201)
        {
            _inPaste = false;
            events.Add(new PasteEvent(_paste.ToString()));
            _paste.Clear();
            return;
        }

        // Shift-Delete has its own key code; other modifiers fall back to the unmodified key,
        // which is what curses reports for them too.
        if (number == 3 && parameters.Length > 1 && parameters[1] == "2")
        {
            events.Add(new KeyEvent(KeyCodes.ShiftDelete));
            return;
        }

        if (TildeKeys.TryGetValue(number, out int key))
        {
            events.Add(new KeyEvent(key));
        }
    }

    /// <summary>Accumulates pasted text until the closing marker arrives.</summary>
    private bool TryDecodePasteBody(List<InputEvent> events, out int consumed)
    {
        // The closing marker is an escape sequence, so hand control back for it.
        if (_pending[0] == Escape)
        {
            _inPaste = false;
            bool decoded = TryDecodeCsi(events, out consumed);
            _inPaste = !decoded || events.Count == 0 || events[^1] is not PasteEvent;
            return decoded;
        }

        // Paste content is text, so it is decoded as UTF-8 rather than kept as raw bytes.
        int length = 1;
        while (length < _pending.Count && _pending[length] != Escape)
        {
            length++;
        }

        _paste.Append(Encoding.UTF8.GetString([.. _pending.GetRange(0, length)]));
        consumed = length;
        return true;
    }

    /// <summary>Decodes an SGR mouse report: <c>ESC [ &lt; button ; column ; row M</c>.</summary>
    private static void DecodeMouse(List<InputEvent> events, string body, bool isPress)
    {
        string[] parts = body.Split(';');
        if (parts.Length < 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int code) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int column) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int row))
        {
            return;
        }

        bool shift = (code & 0x04) != 0;
        bool alt = (code & 0x08) != 0;
        bool control = (code & 0x10) != 0;
        bool isDrag = (code & 0x20) != 0;
        bool isWheel = (code & 0x40) != 0;

        MouseButton button = isWheel
            ? (code & 0x01) == 0 ? MouseButton.WheelUp : MouseButton.WheelDown
            : (code & 0x03) switch
            {
                0 => MouseButton.Left,
                1 => MouseButton.Middle,
                2 => MouseButton.Right,
                _ => MouseButton.None,
            };

        MouseAction action = isDrag
            ? MouseAction.Drag
            : isPress ? MouseAction.Press : MouseAction.Release;

        // The terminal counts from one; everything above this counts from zero.
        events.Add(new MouseEvent(button, action, column - 1, row - 1, shift, alt, control));
    }
}
