// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Canger.Tui.Native;

/// <summary>
/// A <c>struct termios</c>, the terminal driver's settings.
/// </summary>
/// <remarks>
/// Layout verified against <c>bits/termios.h</c> on linux-x86_64: four 32-bit flag words, a line
/// discipline byte, 32 control characters, then the two speeds, padded to 60 bytes. The same
/// layout applies on linux-arm64.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 60)]
internal struct TermiosSettings
{
    /// <summary>Input mode flags.</summary>
    internal uint InputFlags;

    /// <summary>Output mode flags.</summary>
    internal uint OutputFlags;

    /// <summary>Control mode flags.</summary>
    internal uint ControlFlags;

    /// <summary>Local mode flags: echo, canonical mode, signal generation.</summary>
    internal uint LocalFlags;

    /// <summary>Line discipline.</summary>
    internal byte LineDiscipline;

    /// <summary>Control characters, indexed by the <c>V*</c> constants.</summary>
    [System.Runtime.CompilerServices.InlineArray(32)]
    internal struct ControlCharacters
    {
        private byte _element;
    }

    /// <summary>Control characters, indexed by the <c>V*</c> constants.</summary>
    internal ControlCharacters Control;
}

/// <summary>Terminal driver flags and control-character indices, from <c>termios.h</c>.</summary>
internal static class TermiosFlags
{
    /// <summary>Translate a carriage return to a newline on input.</summary>
    internal const uint InputCrToNewline = 0x100;

    /// <summary>Enable software flow control, which swallows Ctrl-S and Ctrl-Q.</summary>
    internal const uint InputFlowControl = 0x400;

    /// <summary>Signal an interrupt on a break condition.</summary>
    internal const uint InputBreakInterrupt = 0x2;

    /// <summary>Enable input parity checking.</summary>
    internal const uint InputParityCheck = 0x10;

    /// <summary>Strip the eighth bit from input.</summary>
    internal const uint InputStripEighthBit = 0x20;

    /// <summary>Echo input back to the terminal.</summary>
    internal const uint LocalEcho = 0x8;

    /// <summary>Line-buffer input until Enter, rather than delivering it as it is typed.</summary>
    internal const uint LocalCanonical = 0x2;

    /// <summary>Enable extended input processing, including the literal-next character.</summary>
    internal const uint LocalExtended = 0x8000;

    /// <summary>Turn interrupt, quit and suspend characters into signals.</summary>
    internal const uint LocalSignals = 0x1;

    /// <summary>
    /// Index of the interrupt character in the control array — Ctrl-C by default.
    /// </summary>
    /// <remarks>
    /// Setting it to nothing stops the driver turning Ctrl-C into a signal, so it arrives as an
    /// ordinary byte and a key binding can act on it. Done this way rather than by clearing
    /// <see cref="LocalSignals"/>, which would take Ctrl-Z's suspend with it.
    /// </remarks>
    internal const int InterruptCharacter = 0;

    /// <summary>Index of the quit character in the control array — Ctrl-Backslash by default.</summary>
    internal const int QuitCharacter = 1;

    /// <summary>Index of the minimum-characters setting in the control array.</summary>
    internal const int MinimumCharacters = 6;

    /// <summary>Index of the read-timeout setting in the control array.</summary>
    internal const int ReadTimeout = 5;

    /// <summary>Apply the change immediately.</summary>
    internal const int ApplyNow = 0;

    /// <summary>Apply the change after pending output has drained, discarding pending input.</summary>
    internal const int ApplyAfterFlush = 2;
}

/// <summary>A <c>struct winsize</c>, the terminal's dimensions.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WindowSize
{
    /// <summary>Rows.</summary>
    internal ushort Rows;

    /// <summary>Columns.</summary>
    internal ushort Columns;

    /// <summary>Width in pixels, when the terminal reports it.</summary>
    internal ushort PixelWidth;

    /// <summary>Height in pixels, when the terminal reports it.</summary>
    internal ushort PixelHeight;
}

/// <summary>A <c>struct pollfd</c>, describing what to wait for on one descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PollDescriptor
{
    /// <summary>The descriptor to watch.</summary>
    internal int Descriptor;

    /// <summary>The events of interest.</summary>
    internal short Events;

    /// <summary>The events that occurred.</summary>
    internal short ReturnedEvents;

    /// <summary>There is data to read.</summary>
    internal const short Readable = 0x001;
}

/// <summary>Terminal driver calls.</summary>
internal static partial class Termios
{
    private const string LibraryName = "libc";

    /// <summary>Request code for reading the window size.</summary>
    internal const ulong GetWindowSize = 0x5413;

    /// <summary>Reads the terminal's current settings.</summary>
    /// <param name="fd">The terminal descriptor.</param>
    /// <param name="settings">Receives the settings.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "tcgetattr", SetLastError = true)]
    internal static partial int GetAttributes(int fd, out TermiosSettings settings);

    /// <summary>Applies terminal settings.</summary>
    /// <param name="fd">The terminal descriptor.</param>
    /// <param name="actions">When the change takes effect.</param>
    /// <param name="settings">The settings to apply.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "tcsetattr", SetLastError = true)]
    internal static partial int SetAttributes(int fd, int actions, in TermiosSettings settings);

    /// <summary>Reads the terminal's dimensions.</summary>
    /// <param name="fd">The terminal descriptor.</param>
    /// <param name="request">Must be <see cref="GetWindowSize"/>.</param>
    /// <param name="size">Receives the dimensions.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, ulong request, out WindowSize size);

    /// <summary>Waits for a descriptor to become readable, or for a timeout.</summary>
    /// <param name="descriptors">The descriptors to watch.</param>
    /// <param name="count">How many descriptors are in the array.</param>
    /// <param name="timeoutMilliseconds">
    /// How long to wait; zero returns immediately and a negative value waits indefinitely.
    /// </param>
    /// <returns>How many descriptors are ready, zero on timeout, or -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "poll", SetLastError = true)]
    internal static partial int Poll(ref PollDescriptor descriptors, nuint count,
                                     int timeoutMilliseconds);

    /// <summary>Creates a pipe, for waking a poll from another thread.</summary>
    /// <param name="descriptors">Receives the read and write ends, in that order.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport(LibraryName, EntryPoint = "pipe", SetLastError = true)]
    internal static partial int Pipe(Span<int> descriptors);

    /// <summary>Writes to a descriptor.</summary>
    /// <param name="descriptor">Where to write.</param>
    /// <param name="buffer">What to write.</param>
    /// <param name="count">How many bytes of it.</param>
    /// <returns>How many bytes were written, or -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "write", SetLastError = true)]
    internal static partial nint Write(int descriptor, ReadOnlySpan<byte> buffer, nuint count);

    /// <summary>Reads from a descriptor.</summary>
    /// <param name="descriptor">Where to read from.</param>
    /// <param name="buffer">Receives the bytes.</param>
    /// <param name="count">At most this many.</param>
    /// <returns>How many bytes were read, or -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "read", SetLastError = true)]
    internal static partial nint Read(int descriptor, Span<byte> buffer, nuint count);

    /// <summary>Closes a descriptor.</summary>
    /// <param name="descriptor">The descriptor to close.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport(LibraryName, EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int descriptor);

    /// <summary>Whether a descriptor refers to a terminal.</summary>
    /// <param name="fd">The descriptor.</param>
    /// <returns>One when it is a terminal, zero otherwise.</returns>
    [LibraryImport(LibraryName, EntryPoint = "isatty", SetLastError = true)]
    internal static partial int IsATty(int fd);
}
