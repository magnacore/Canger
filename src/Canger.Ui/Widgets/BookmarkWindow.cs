// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// Lists the bookmarks and where they lead.
/// </summary>
/// <remarks>
/// <para>
/// Shown while <c>'</c> or <c>m</c> is waiting for the key that names a bookmark, which is when
/// the question "which letter was it?" actually arises. Laid out exactly as ranger's
/// (<c>gui/widgets/view_base.py:67-92</c>): one bookmark per row, anchored to the bottom of the
/// listing, under an underlined <c>mark  path</c> heading.
/// </para>
/// <para>
/// The heading's rule is the heading itself drawn underlined rather than a row of dashes beneath
/// it — which is what keeps it to one row and lets the list start immediately below.
/// </para>
/// </remarks>
/// <param name="colorScheme">Decides what colour everything is drawn in.</param>
public sealed class BookmarkWindow(IColorScheme colorScheme) : Widget
{
    /// <summary>The bookmarks to list.</summary>
    public Bookmarks? Bookmarks { get; set; }

    /// <summary>
    /// Whether bookmarks pointing inside a hidden directory are listed.
    /// </summary>
    /// <remarks>
    /// <c>show_hidden_bookmarks</c>. Ranger's test is whether the path contains <c>/.</c>
    /// anywhere, not whether its last component is hidden, so a bookmark deep inside
    /// <c>~/.config</c> is hidden too.
    /// </remarks>
    public bool ShowHidden { get; set; } = true;

    /// <summary>
    /// The rows that would be listed, in order.
    /// </summary>
    /// <remarks>
    /// Exposed so the layout can size the window before drawing it, and so this can be tested
    /// without a screen.
    /// </remarks>
    public IReadOnlyList<(char Key, string Path)> Entries => Collect();

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        IReadOnlyList<(char Key, string Path)> entries = Entries;

        if (entries.Count == 0 || Bounds.Height < 2)
        {
            return;
        }

        CellStyle style = colorScheme.Resolve(StyleContext.Of(ContextKey.InBrowser));

        // The heading carries the rule: ranger sets A_UNDERLINE on that row rather than drawing a
        // separate line of characters.
        CellStyle heading = colorScheme
            .Resolve(StyleContext.Of(ContextKey.InBrowser, ContextKey.Title))
            .With(CellAttributes.Underline);

        // Anchored to the bottom, so the listing above stays visible as far as there is room.
        int rows = Math.Min(Bounds.Height - 1, entries.Count);
        int top = Bounds.Bottom - rows;

        screen.Fill(Bounds.X, top - 1, Bounds.Width, 1, heading);
        screen.Write(Bounds.X, top - 1,
                     new WideString("mark  path").Truncate(Bounds.Width), heading);

        for (int i = 0; i < rows; i++)
        {
            (char key, string path) = entries[i];

            // ranger's spacing exactly: a leading space, the key, three spaces, the path.
            string line = " " + key + "   " + path;

            screen.Fill(Bounds.X, top + i, Bounds.Width, 1, style);
            screen.Write(Bounds.X, top + i, new WideString(line).Truncate(Bounds.Width), style);
        }
    }

    /// <summary>The bookmarks worth listing, ordered as ranger orders them.</summary>
    private List<(char Key, string Path)> Collect()
    {
        if (Bookmarks is not { } bookmarks)
        {
            return [];
        }

        return
        [
            .. bookmarks.Entries
                       .Where(e => ShowHidden || !e.Value.Contains("/.", StringComparison.Ordinal))

                       // Case-insensitively by key, so `a` and `A` sit together rather than being
                       // separated by every capital letter.
                       .OrderBy(e => char.ToLowerInvariant(e.Key))
                       .ThenBy(e => e.Key)
                       .Select(e => (e.Key, e.Value)),
        ];
    }
}
