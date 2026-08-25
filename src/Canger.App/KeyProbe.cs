// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.Configuration;
using Canger.Core.Input;
using Canger.Tui;
using Canger.Tui.Input;
using Canger.Tui.Rendering;

namespace Canger.App;

/// <summary>
/// A diagnostic mode that takes over the terminal and reports what each key resolves to.
/// </summary>
/// <remarks>
/// This exercises the whole input path — raw mode, escape-sequence decoding, the key map loaded
/// from cc.conf, and the screen buffer — against a real terminal, which unit tests cannot do.
/// It is also genuinely useful on its own: it answers "why does this key not work" by showing the
/// key codes a terminal actually sends and the command they resolve to.
/// </remarks>
internal static class KeyProbe
{
    /// <summary>Runs the probe until Ctrl-Q is pressed.</summary>
    /// <param name="keyMaps">The key maps to match against.</param>
    /// <returns>A process exit code.</returns>
    internal static int Run(KeyMaps keyMaps)
    {
        using Terminal terminal = new(enableMouse: true);

        ScreenBuffer screen = new(terminal.Width, terminal.Height);
        InputDecoder decoder = new();
        KeyBuffer keys = new(keyMaps.Browser);

        List<string> log = [];
        bool running = true;

        terminal.Resized += (_, size) => screen.Resize(size.Width, size.Height);

        Draw(terminal, screen, log, keys);

        while (running)
        {
            // A lone escape byte is ambiguous, so when the decoder is holding something and
            // nothing more arrives within the escape delay, ask it to resolve what it has.
            if (!Terminal.WaitForInput(decoder.HasPendingInput ? EscapeDelayMilliseconds : -1))
            {
                foreach (InputEvent pending in decoder.Flush())
                {
                    Handle(pending, keys, log, ref running);
                }

                Draw(terminal, screen, log, keys);
                continue;
            }

            ReadOnlyMemory<byte> input = terminal.ReadAsync().AsTask().GetAwaiter().GetResult();
            if (input.Length == 0)
            {
                break;
            }

            foreach (InputEvent evt in decoder.Feed(input.Span))
            {
                Handle(evt, keys, log, ref running);
            }

            Draw(terminal, screen, log, keys);
        }

        return 0;
    }

    /// <summary>How long to wait for the rest of an escape sequence before giving up on it.</summary>
    /// <remarks>Matches the delay curses uses, which ranger sets to the same value.</remarks>
    private const int EscapeDelayMilliseconds = 25;

    /// <summary>Records one event, and notices the key that ends the probe.</summary>
    private static void Handle(InputEvent evt, KeyBuffer keys, List<string> log, ref bool running)
    {
        switch (evt)
        {
            case KeyEvent key:
                // Ctrl-Q leaves, so the probe stays usable however the other keys are bound.
                if (key.Key == 17)
                {
                    running = false;
                    return;
                }

                log.Add(Describe(keys, key.Key));
                return;

            case MouseEvent mouse:
                log.Add($"mouse   {mouse.Button} {mouse.Action} at ({mouse.X}, {mouse.Y})");
                return;

            case PasteEvent paste:
                log.Add($"paste   {paste.Text.Length} characters: {Abbreviate(paste.Text)}");
                return;

            default:
                return;
        }
    }

    /// <summary>Feeds one key to the buffer and describes what became of it.</summary>
    private static string Describe(KeyBuffer keys, int key)
    {
        string? command = keys.Add(key);
        string rendered = KeyCodes.ToDisplayString(key);
        string quantifier = keys.Quantifier is { } count ? $" count={count}" : string.Empty;

        string outcome = command is not null
            ? $"-> {command}"
            : keys.HasFailed ? "-> (not bound)" : "   (waiting for more)";

        if (keys.IsFinished || keys.HasFailed)
        {
            keys.Clear();
        }

        return $"key {key,5}  {rendered,-14}{quantifier,-10}{outcome}";
    }

    private static void Draw(Terminal terminal, ScreenBuffer screen, List<string> log, KeyBuffer keys)
    {
        screen.Clear();

        CellStyle heading = CellStyle.Default.With(CellAttributes.Bold).On(Color.Cyan);
        screen.Write(0, 0, "Canger key probe — press keys to see how they decode. Ctrl-Q quits.",
                     heading);

        screen.Write(0, 1, $"pending: {keys}", CellStyle.Default.On(Color.Yellow));

        int firstRow = 3;
        int rows = Math.Max(screen.Height - firstRow, 0);
        foreach ((string line, int index) in log.TakeLast(rows).Select((l, i) => (l, i)))
        {
            screen.Write(0, firstRow + index, line);
        }

        StringBuilder output = new();
        screen.Flush(output);
        terminal.Write(output.ToString());
    }

    private static string Abbreviate(string text) =>
        text.Length <= 40 ? text : string.Concat(text.AsSpan(0, 37), "...");
}
