// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// Lists what can follow a partly-typed key sequence.
/// </summary>
/// <remarks>
/// <para>
/// Pressing <c>g</c> is a question — "what are my options?" — and this answers it rather than
/// leaving the user to remember. It appears only while a sequence is genuinely unfinished, so it
/// never interrupts a key that means something on its own.
/// </para>
/// <para>
/// Long lists are collapsed a group at a time: with more than
/// <c>hint_collapse_threshold</c> continuations, each first key that leads to several commands is
/// shown once as <c>...</c>. Forty lines of bindings would bury the answer rather than give it.
/// </para>
/// </remarks>
/// <param name="colorScheme">Decides what colour everything is drawn in.</param>
public sealed class HintWindow(IColorScheme colorScheme) : Widget
{
    /// <summary>Width given to the key column, which keeps the commands aligned.</summary>
    private const int KeyColumnWidth = 11;

    /// <summary>The branch of the key map the user has reached, or <see langword="null"/>.</summary>
    public KeyMapNode? Position { get; set; }

    /// <summary>How many continuations are tolerated before groups are collapsed.</summary>
    public int CollapseThreshold { get; set; } = 10;

    /// <summary>
    /// The continuations that would be listed, in the order they are shown.
    /// </summary>
    /// <remarks>
    /// Exposed so the layout can size the window and so this can be tested without a screen.
    /// </remarks>
    public IReadOnlyList<(string Keys, string Command)> Hints => Collect();

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        IReadOnlyList<(string Keys, string Command)> hints = Hints;

        if (hints.Count == 0 || Bounds.Height < 2)
        {
            return;
        }

        CellStyle style = colorScheme.Resolve(StyleContext.Of(ContextKey.InBrowser));
        // Underlined, which is how ranger draws the rule beneath the heading: the attribute on
        // that row rather than a separate line of characters (view_base.py:173).
        CellStyle heading = colorScheme
            .Resolve(StyleContext.Of(ContextKey.InBrowser, ContextKey.Title))
            .With(CellAttributes.Underline);

        // Drawn from the bottom up, so the newest information is nearest the status bar and the
        // listing above stays visible as far as there is room.
        int rows = Math.Min(Bounds.Height - 1, hints.Count);
        int top = Bounds.Bottom - rows;

        screen.Fill(Bounds.X, top - 1, Bounds.Width, 1, heading);
        screen.Write(Bounds.X, top - 1,
                     new WideString("key".PadRight(KeyColumnWidth + 1) + " command")
                         .Truncate(Bounds.Width),
                     heading);

        for (int i = 0; i < rows; i++)
        {
            (string keys, string command) = hints[i];
            string line = " " + keys.PadRight(KeyColumnWidth) + " " + command;

            screen.Fill(Bounds.X, top + i, Bounds.Width, 1, style);
            screen.Write(Bounds.X, top + i, new WideString(line).Truncate(Bounds.Width), style);
        }
    }

    /// <summary>
    /// Walks the branch and returns what could follow, grouped and ordered.
    /// </summary>
    /// <remarks>
    /// Grouped by first key and then sorted by command, which is what puts related bindings
    /// together — every <c>gh</c>, <c>gu</c>, <c>gd</c> beside each other rather than scattered by
    /// keycode.
    /// </remarks>
    private List<(string Keys, string Command)> Collect()
    {
        if (Position is not { } root)
        {
            return [];
        }

        List<(string Keys, string Command)> hints = [];
        Walk(root, string.Empty, hints);

        // Grouped by the first key of each continuation, each group ordered by what it does.
        List<List<(string Keys, string Command)>> groups =
        [
            .. hints
                .GroupBy(h => h.Keys.Length > 0 ? h.Keys[0] : '\0')
                .Select(g => g.OrderBy(h => h.Command, StringComparer.Ordinal).ToList())
        ];

        if (hints.Count > CollapseThreshold)
        {
            groups =
            [
                .. groups.Select(g => g.Count > 1
                    ? [(g[0].Keys[..1], "...")]
                    : g)
            ];
        }

        return [.. groups.OrderBy(g => g[0].Command, StringComparer.Ordinal).SelectMany(g => g)];
    }

    /// <summary>Collects every leaf beneath a node, with the keys that reach it.</summary>
    private static void Walk(KeyMapNode node, string prefix,
                             List<(string Keys, string Command)> into)
    {
        foreach ((int key, KeyMapNode child) in node.Children)
        {
            string keys = prefix + KeyCodes.ToDisplayString(key);

            if (child.IsLeaf)
            {
                // A binding whose only job is to show these would be circular.
                if (child.Command is { } command && !command.StartsWith("hint",
                                                                       StringComparison.Ordinal))
                {
                    into.Add((keys, command));
                }

                continue;
            }

            Walk(child, keys, into);
        }
    }
}
