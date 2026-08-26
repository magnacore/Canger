// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Ui;

/// <summary>
/// The arithmetic behind a visual selection: which rows lie between the anchor and the cursor,
/// and what marking them does to everything else.
/// </summary>
/// <remarks>
/// Separate from <see cref="Browser"/> because it is the part worth testing on its own — it needs
/// only a listing and two positions, where the browser around it needs a terminal.
/// </remarks>
internal static class VisualRange
{
    /// <summary>Where the selection's fixed end currently sits in a listing.</summary>
    /// <param name="entries">The listing as it stands now.</param>
    /// <param name="anchorPath">The file the selection was started from.</param>
    /// <param name="fallback">The row it was on when the mode began.</param>
    /// <returns>The anchor's row.</returns>
    /// <remarks>
    /// <para>
    /// Found by name rather than remembered as a row number, because a listing can change under a
    /// selection — a file is written, an extraction finishes — and every row below the change
    /// shifts. A remembered row then names a different file, and the next movement sweeps a range
    /// the user never chose.
    /// </para>
    /// <para>
    /// Ranger keeps only the number (<c>_visual_pos_start</c>, <c>core/actions.py:90</c>) and
    /// clamps it to the listing's length. That is the fallback here, used when the anchor's file
    /// has gone altogether — it is the best guess left, and it is what ranger would have done.
    /// </para>
    /// </remarks>
    public static int AnchorIndex(IReadOnlyList<FsNode> entries, string? anchorPath, int fallback)
    {
        if (anchorPath is not null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Path, anchorPath, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        // Ranger's `min(self._visual_pos_start, (len(cwd.files) - 1))`, and never below zero so
        // an empty listing does not produce a negative row.
        return Math.Clamp(fallback, 0, Math.Max(entries.Count - 1, 0));
    }

    /// <summary>Marks everything between the two ends, and restores everything outside them.</summary>
    /// <param name="entries">The listing to mark.</param>
    /// <param name="anchor">The row the selection is fixed at.</param>
    /// <param name="cursor">The row the cursor is on.</param>
    /// <param name="reverse">Whether the mode unmarks rather than marks.</param>
    /// <param name="selectionBefore">What was already marked when the mode began, by path.</param>
    /// <remarks>
    /// Ranger expresses this as set arithmetic (<c>core/actions.py:549-558</c>); written out per
    /// entry it is simply: inside the range takes the mode's value, outside it reverts to whatever
    /// the entry had before the mode began. Either way, moving back over your own path undoes it
    /// while an unrelated earlier mark survives.
    /// </remarks>
    public static void Apply(IReadOnlyList<FsNode> entries, int anchor, int cursor, bool reverse,
                             IReadOnlySet<string>? selectionBefore)
    {
        (int low, int high) = anchor <= cursor ? (anchor, cursor) : (cursor, anchor);

        for (int i = 0; i < entries.Count; i++)
        {
            bool previously = selectionBefore?.Contains(entries[i].Path) ?? false;

            entries[i].IsMarked = i >= low && i <= high ? !reverse : previously;
        }
    }
}
