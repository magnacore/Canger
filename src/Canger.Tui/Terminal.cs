// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using Canger.Tui.Native;
using Canger.Tui.Rendering;

namespace Canger.Tui;

/// <summary>
/// Owns the terminal for the lifetime of the application: raw input, the alternate screen, mouse
/// reporting, bracketed paste, and resize notifications.
/// </summary>
/// <remarks>
/// <para>
/// A terminal is global, shared state that outlives the process if it is left misconfigured — a
/// crash with echo still disabled leaves the user with an apparently dead shell. Everything this
/// class turns on is therefore recorded and undone in <see cref="Dispose"/>, and the original
/// driver settings are captured before anything is changed so they can be restored exactly.
/// </para>
/// <para>
/// Input is left in "cbreak" rather than fully raw mode: keys are delivered as they are typed and
/// nothing is echoed, but the interrupt and suspend characters still generate signals, so Ctrl-Z
/// suspends Canger the way it suspends any other program. That is the mode ranger uses too.
/// </para>
/// </remarks>
public sealed class Terminal : IDisposable
{
    private const int StandardInput = 0;
    private const int StandardOutput = 1;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TermiosSettings _originalSettings;
    private readonly bool _hasTerminal;
    private readonly PosixSignalRegistration? _resizeRegistration;

    /// <summary>
    /// Restores the terminal when the process is being killed rather than quitting.
    /// </summary>
    /// <remarks>
    /// Ctrl-C no longer arrives as a signal, but `kill`, a closing terminal and a logout all
    /// still do. Leaving the alternate screen and cooked mode behind makes the shell the user
    /// lands back in unusable, so these are worth handling even though they end the process.
    /// </remarks>
    private readonly List<PosixSignalRegistration> _terminationRegistrations = [];
    private readonly byte[] _readBuffer = new byte[4096];

    private bool _disposed;
    private bool _suspended;

    /// <summary>Takes control of the terminal.</summary>
    /// <param name="enableMouse">Whether to ask the terminal to report mouse events.</param>
    /// <exception cref="InvalidOperationException">Standard input is not a terminal.</exception>
    public Terminal(bool enableMouse = true)
    {
        _hasTerminal = Termios.IsATty(StandardInput) == 1;
        if (!_hasTerminal)
        {
            throw new InvalidOperationException(
                "Canger needs a terminal on standard input. It cannot run with input redirected.");
        }

        if (Termios.GetAttributes(StandardInput, out _originalSettings) != 0)
        {
            throw new InvalidOperationException(
                $"Could not read the terminal settings (errno {Marshal.GetLastPInvokeError()}).");
        }

        // Deliberately not Console.OpenStandardInput. System.Console reconfigures the terminal
        // driver for its own key handling the first time it is used, which silently undoes raw
        // mode. Opening the descriptors directly keeps Canger the only thing touching termios.
        _input = new FileStream(
            new SafeFileHandle((nint)StandardInput, ownsHandle: false), FileAccess.Read,
            bufferSize: 1, isAsync: false);
        _output = new FileStream(
            new SafeFileHandle((nint)StandardOutput, ownsHandle: false), FileAccess.Write,
            bufferSize: 1, isAsync: false);

        EnterRawMode();

        StringBuilder setup = new();
        setup.Append(Ansi.EnterAlternateScreen)
             .Append(Ansi.HideCursor)
             .Append(Ansi.EnableBracketedPaste)
             .Append(Ansi.ClearScreen);

        MouseEnabled = enableMouse;
        if (enableMouse)
        {
            setup.Append(Ansi.EnableMouse);
        }

        Write(setup.ToString());

        (Width, Height) = QuerySize();

        OpenWakePipe();

        // SIGWINCH is the only notification a terminal gives that it has been resized.
        _resizeRegistration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, OnResize);

        foreach (PosixSignal signal in (PosixSignal[])
                 [PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP,
                  PosixSignal.SIGQUIT])
        {
            _terminationRegistrations.Add(PosixSignalRegistration.Create(signal, OnTermination));
        }
    }

    /// <summary>Raised when the terminal has been resized.</summary>
    /// <remarks>
    /// Raised on a signal-handling thread, so handlers should record the new size and let the
    /// main loop act on it rather than redrawing from here.
    /// </remarks>
    public event EventHandler<(int Width, int Height)>? Resized;

    /// <summary>Columns.</summary>
    public int Width { get; private set; }

    /// <summary>Rows.</summary>
    public int Height { get; private set; }

    /// <summary>Whether mouse reporting was requested.</summary>
    public bool MouseEnabled { get; private set; }

    /// <summary>Reads whatever input is available, blocking until at least one byte arrives.</summary>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>The bytes read, which may be empty when input has ended.</returns>
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
    {
        int read = await _input.ReadAsync(_readBuffer, cancellationToken);
        return _readBuffer.AsMemory(0, read);
    }

    /// <summary>
    /// Waits until input is available or the timeout expires.
    /// </summary>
    /// <remarks>
    /// The main loop needs this for two reasons. A lone escape byte is ambiguous — it may be the
    /// Escape key or the start of a sequence still in flight — and the only way to tell is to
    /// wait briefly and see whether anything follows. And background work needs to make progress
    /// while no one is typing, which means waking on a timeout rather than blocking on a read.
    /// </remarks>
    /// <param name="timeoutMilliseconds">
    /// How long to wait. Zero polls, and a negative value waits indefinitely.
    /// </param>
    /// <returns><see langword="true"/> when input is available.</returns>
    public static bool WaitForInput(int timeoutMilliseconds)
    {
        // Two descriptors: the terminal, and a pipe another thread can write to. Setting a flag
        // cannot end a poll that is already waiting, so without the pipe a preview finishing on
        // a worker went unnoticed until the idle delay expired — a second and a half of looking
        // at a blank preview column for work that took fifteen milliseconds.
        Span<PollDescriptor> descriptors =
        [
            new PollDescriptor { Descriptor = StandardInput, Events = PollDescriptor.Readable },
            new PollDescriptor { Descriptor = _wakeRead, Events = PollDescriptor.Readable },
        ];

        int count = _wakeRead >= 0 ? 2 : 1;
        int ready;

        do
        {
            ready = Termios.Poll(ref descriptors[0], (nuint)count, timeoutMilliseconds);
        }
        while (ready < 0 && Marshal.GetLastPInvokeError() == 4); // EINTR, typically a resize.

        // Drained whether or not it woke us, so a burst of wake-ups does not queue up frames.
        if (count == 2 && (descriptors[1].ReturnedEvents & PollDescriptor.Readable) != 0)
        {
            Span<byte> discard = stackalloc byte[64];
            Termios.Read(_wakeRead, discard, (nuint)discard.Length);
        }

        return ready > 0 && (descriptors[0].ReturnedEvents & PollDescriptor.Readable) != 0;
    }

    /// <summary>Throws away anything typed but not yet handled.</summary>
    /// <remarks>
    /// The <c>flushinput</c> setting. It exists for the moment a keystroke turns out to be slow —
    /// entering a directory of twenty thousand files, say — and the keys pressed meanwhile would
    /// otherwise all arrive at once and run somewhere unintended. Ranger calls
    /// <c>curses.flushinp()</c> after each key for the same reason (<c>gui/ui.py:260-266</c>).
    /// </remarks>
    public static void DiscardPendingInput()
    {
        Span<byte> discard = stackalloc byte[256];

        // Bounded, so a terminal delivering input faster than it is read cannot hold the loop
        // here indefinitely.
        for (int i = 0; i < 64 && WaitForInput(0); i++)
        {
            if (Termios.Read(StandardInput, discard, (nuint)discard.Length) <= 0)
            {
                return;
            }
        }
    }

    private static int _wakeRead = -1;
    private static int _wakeWrite = -1;

    /// <summary>
    /// Ends a wait started by <see cref="WaitForInput"/>, from any thread.
    /// </summary>
    /// <remarks>
    /// A single byte down a pipe the poll is also watching. This is the only way to interrupt a
    /// poll that has already begun; a flag can only be noticed once it ends on its own.
    /// </remarks>
    public static void Wake()
    {
        if (_wakeWrite < 0)
        {
            return;
        }

        ReadOnlySpan<byte> one = [1];
        Termios.Write(_wakeWrite, one, 1);
    }

    /// <summary>Opens the wake-up pipe, if the platform allows it.</summary>
    private static void OpenWakePipe()
    {
        if (_wakeRead >= 0)
        {
            return;
        }

        Span<int> ends = stackalloc int[2];

        // Without the pipe everything still works; background results simply wait for the idle
        // timeout, as they did before. Not worth failing to start over.
        if (Termios.Pipe(ends) == 0)
        {
            _wakeRead = ends[0];
            _wakeWrite = ends[1];
        }
    }

    /// <summary>Writes text to the terminal and flushes it.</summary>
    /// <param name="text">The text, which may contain escape sequences.</param>
    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        _output.Write(bytes, 0, bytes.Length);
        _output.Flush();
    }

    /// <summary>Turns mouse reporting on or off while running.</summary>
    /// <param name="enabled">Whether the terminal should report mouse events.</param>
    public void SetMouseEnabled(bool enabled)
    {
        if (enabled == MouseEnabled)
        {
            return;
        }

        MouseEnabled = enabled;
        Write(enabled ? Ansi.EnableMouse : Ansi.DisableMouse);
    }

    /// <summary>Shows or hides the hardware cursor.</summary>
    /// <param name="visible">Whether the cursor should be visible.</param>
    public void SetCursorVisible(bool visible) =>
        Write(visible ? Ansi.ShowCursor : Ansi.HideCursor);

    /// <summary>Moves the hardware cursor, which is where the console shows the insertion point.</summary>
    /// <param name="x">Column, counting from zero.</param>
    /// <param name="y">Row, counting from zero.</param>
    public void SetCursorPosition(int x, int y) => Write(Ansi.MoveCursor(y + 1, x + 1));

    /// <summary>
    /// Reads the terminal's dimensions from the driver.
    /// </summary>
    /// <remarks>
    /// Static because the size belongs to the process's controlling terminal rather than to any
    /// instance. <see cref="Width"/> and <see cref="Height"/> hold the value last observed.
    /// </remarks>
    /// <returns>The current size.</returns>
    public static (int Width, int Height) QuerySize()
    {
        if (Termios.Ioctl(StandardOutput, Termios.GetWindowSize, out WindowSize size) == 0 &&
            size.Columns > 0 && size.Rows > 0)
        {
            return (size.Columns, size.Rows);
        }

        // A terminal that will not report its size still has to be drawn into; the conventional
        // 80x24 is the safest guess.
        return (80, 24);
    }

    /// <summary>
    /// Hands the terminal back so another program can use it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An editor or a pager expects the terminal as it found it: cooked input, echo on, the
    /// normal screen, a visible cursor. This puts all of that back and leaves it that way until
    /// <see cref="Resume"/> is called.
    /// </para>
    /// <para>
    /// The alternate screen is what makes this safe to do repeatedly: Canger's display is set
    /// aside rather than scrolled away, so resuming restores exactly what was there.
    /// </para>
    /// </remarks>
    public void Suspend()
    {
        if (_suspended || !_hasTerminal)
        {
            return;
        }

        _suspended = true;

        StringBuilder handover = new();
        if (MouseEnabled)
        {
            handover.Append(Ansi.DisableMouse);
        }

        handover.Append(Ansi.DisableBracketedPaste)
                .Append(Ansi.ResetStyle)
                .Append(Ansi.ShowCursor)
                .Append(Ansi.LeaveAlternateScreen);

        Write(handover.ToString());
        Termios.SetAttributes(StandardInput, TermiosFlags.ApplyAfterFlush, _originalSettings);
    }

    /// <summary>
    /// Takes the terminal back after another program has finished with it.
    /// </summary>
    /// <remarks>
    /// The caller must repaint afterwards. Nothing is drawn here because the screen buffer has
    /// no idea what the other program left behind, so only a forced full repaint is correct.
    /// </remarks>
    public void Resume()
    {
        if (!_suspended || !_hasTerminal)
        {
            return;
        }

        _suspended = false;

        EnterRawMode();

        StringBuilder setup = new();
        setup.Append(Ansi.EnterAlternateScreen)
             .Append(Ansi.HideCursor)
             .Append(Ansi.EnableBracketedPaste)
             .Append(Ansi.ClearScreen);

        if (MouseEnabled)
        {
            setup.Append(Ansi.EnableMouse);
        }

        Write(setup.ToString());

        (Width, Height) = QuerySize();
    }

    /// <summary>Whether the terminal has been handed to another program.</summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// Restores the terminal to the state it was in before Canger started.
    /// </summary>
    /// <remarks>
    /// Everything turned on in the constructor is turned off here, in reverse order, and the
    /// original driver settings are put back. Leaving any of this undone would hand the user
    /// back a terminal with no echo and no cursor.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resizeRegistration?.Dispose();

        foreach (PosixSignalRegistration registration in _terminationRegistrations)
        {
            registration.Dispose();
        }

        _terminationRegistrations.Clear();

        if (_hasTerminal)
        {
            StringBuilder teardown = new();

            if (MouseEnabled)
            {
                teardown.Append(Ansi.DisableMouse);
            }

            teardown.Append(Ansi.DisableBracketedPaste)
                    .Append(Ansi.ResetStyle)
                    .Append(Ansi.ShowCursor)
                    .Append(Ansi.LeaveAlternateScreen);

            try
            {
                Write(teardown.ToString());
            }
            catch (IOException)
            {
                // The terminal has gone away; there is nothing left to restore it to.
            }

            Termios.SetAttributes(StandardInput, TermiosFlags.ApplyAfterFlush, _originalSettings);
        }

        _input.Dispose();
        _output.Dispose();
    }

    /// <summary>
    /// Switches the driver to deliver keys as they are typed, without echoing them.
    /// </summary>
    /// <summary>Waits for a single keystroke while another program has the terminal.</summary>
    /// <remarks>
    /// <para>
    /// Read here rather than through <c>Console.In</c>, for the reason given where
    /// <see cref="_input"/> is opened: System.Console reconfigures the terminal when it is first
    /// used. Measured — at the "press any key" prompt it turned <c>ICRNL</c> off, and Canger's raw
    /// mode deliberately leaves that on so Enter arrives as a newline for the <c>&lt;CR&gt;</c>
    /// binding. While it was off, Enter reached the console as a carriage return, which is bound
    /// to nothing, so it was inserted as text and drawn as <c>?</c> — <c>:trash????????</c>.
    /// </para>
    /// <para>
    /// It also does its own line editing in userspace, which is why "press any key" would only
    /// accept Enter and echoed everything else onto the screen.
    /// </para>
    /// </remarks>
    public void WaitForKeyPress()
    {
        if (!_hasTerminal)
        {
            return;
        }

        // The suspended terminal is in the settings the program before us was given. One
        // character, no line editing, no echo, and then put it back exactly as it was.
        TermiosSettings settings = _originalSettings;
        settings.LocalFlags &= ~(TermiosFlags.LocalEcho | TermiosFlags.LocalCanonical);
        settings.Control[TermiosFlags.MinimumCharacters] = 1;
        settings.Control[TermiosFlags.ReadTimeout] = 0;

        if (Termios.SetAttributes(StandardInput, TermiosFlags.ApplyNow, settings) != 0)
        {
            return;
        }

        try
        {
            Span<byte> one = stackalloc byte[1];
            Termios.Read(StandardInput, one, 1);
        }
        finally
        {
            Termios.SetAttributes(StandardInput, TermiosFlags.ApplyAfterFlush, _originalSettings);
        }
    }

    private void EnterRawMode()
    {
        TermiosSettings settings = _originalSettings;

        // Deliver input immediately and do not echo it.
        settings.LocalFlags &= ~(TermiosFlags.LocalEcho |
                                 TermiosFlags.LocalCanonical |
                                 TermiosFlags.LocalExtended);

        // Ctrl-C and Ctrl-Backslash stop being signals and become ordinary bytes, so the
        // `<C-c> abort` binding can act on them. Left as signals they killed Canger outright,
        // with the alternate screen still in use and the driver still in raw mode — which is
        // what leaves a shell prompt in the middle of a half-drawn interface. Signal generation
        // itself stays on, so Ctrl-Z still suspends.
        settings.Control[TermiosFlags.InterruptCharacter] = 0;
        settings.Control[TermiosFlags.QuitCharacter] = 0;

        // Stop the driver from swallowing Ctrl-S and Ctrl-Q as flow control, and from mangling
        // high bytes, so that non-ASCII keys arrive intact.
        settings.InputFlags &= ~(TermiosFlags.InputFlowControl |
                                 TermiosFlags.InputBreakInterrupt |
                                 TermiosFlags.InputParityCheck |
                                 TermiosFlags.InputStripEighthBit);

        // Carriage-return translation is deliberately left on, so Enter arrives as a newline,
        // which is what the <CR> binding expects.
        settings.Control[TermiosFlags.MinimumCharacters] = 1;
        settings.Control[TermiosFlags.ReadTimeout] = 0;

        if (Termios.SetAttributes(StandardInput, TermiosFlags.ApplyNow, settings) != 0)
        {
            throw new InvalidOperationException(
                $"Could not put the terminal into raw mode (errno {Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>
    /// Puts the terminal back before the process dies.
    /// </summary>
    /// <remarks>
    /// Deliberately does <em>not</em> cancel the signal: the process is meant to end. This only
    /// makes sure it ends leaving a usable terminal behind, which the default handler does not.
    /// </remarks>
    private void OnTermination(PosixSignalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dispose();
    }

    private void OnResize(PosixSignalContext context)
    {
        context.Cancel = true;

        (int width, int height) = QuerySize();
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        Resized?.Invoke(this, (width, height));
    }
}
