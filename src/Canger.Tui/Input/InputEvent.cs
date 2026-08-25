// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Tui.Input;

/// <summary>Something the user did, decoded from the terminal's input stream.</summary>
public abstract record InputEvent;

/// <summary>
/// A key press, as a key code the binding layer can match against.
/// </summary>
/// <remarks>
/// Codes below 256 are raw input bytes rather than characters, so a multi-byte character arrives
/// as several events. That is what lets a binding for a non-ASCII key match the bytes that key
/// actually produces; reassembling text is the console's job, and only the console needs it.
/// </remarks>
/// <param name="Key">The key code.</param>
public sealed record KeyEvent(int Key) : InputEvent;

/// <summary>Which mouse button was involved.</summary>
public enum MouseButton
{
    /// <summary>Left button.</summary>
    Left,

    /// <summary>Middle button.</summary>
    Middle,

    /// <summary>Right button.</summary>
    Right,

    /// <summary>Wheel scrolled up.</summary>
    WheelUp,

    /// <summary>Wheel scrolled down.</summary>
    WheelDown,

    /// <summary>Movement with no button held.</summary>
    None,
}

/// <summary>What happened to the button.</summary>
public enum MouseAction
{
    /// <summary>The button went down.</summary>
    Press,

    /// <summary>The button came up.</summary>
    Release,

    /// <summary>The mouse moved with a button held.</summary>
    Drag,
}

/// <summary>
/// A mouse event, in character cells with the top left cell at (0, 0).
/// </summary>
/// <param name="Button">Which button.</param>
/// <param name="Action">What happened to it.</param>
/// <param name="X">Column, counting from zero.</param>
/// <param name="Y">Row, counting from zero.</param>
/// <param name="Shift">Whether Shift was held.</param>
/// <param name="Alt">Whether Alt was held.</param>
/// <param name="Control">Whether Control was held.</param>
public sealed record MouseEvent(
    MouseButton Button,
    MouseAction Action,
    int X,
    int Y,
    bool Shift = false,
    bool Alt = false,
    bool Control = false) : InputEvent;

/// <summary>
/// Text the user pasted, delivered whole.
/// </summary>
/// <remarks>
/// Pasted text arrives as one event rather than as key presses so that it can never be mistaken
/// for typing. Without this, pasting a filename containing <c>q</c> into the browser would quit,
/// and a pasted newline would submit a half-finished command.
/// </remarks>
/// <param name="Text">The pasted text.</param>
public sealed record PasteEvent(string Text) : InputEvent;
