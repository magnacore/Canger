// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Model;

/// <summary>
/// Orders a directory listing.
/// </summary>
/// <remarks>
/// <para>
/// The ordering is built as a composite comparison rather than as several passes: first the
/// directories-first grouping, then the chosen key, then the name as a tie-break so that equal
/// keys still produce a stable, predictable listing.
/// </para>
/// <para>
/// Ranger achieves the same result with two sorts in sequence and relies on Python's sort being
/// stable (<c>container/directory.py:529-563</c>). That approach does not survive translation:
/// <see cref="List{T}.Sort(Comparison{T})"/> is an introsort and is <em>not</em> stable, so
/// sorting by name and then by directory-ness would scramble the names within each group.
/// </para>
/// </remarks>
public static class NodeSorter
{
    /// <summary>Orders nodes.</summary>
    /// <param name="nodes">The nodes to order.</param>
    /// <param name="order">How to order them.</param>
    /// <param name="seed">
    /// Seeds the shuffle for <see cref="SortKey.Random"/>, so a random ordering is stable within
    /// one listing rather than changing on every redraw.
    /// </param>
    /// <returns>A new, ordered list.</returns>
    public static List<FsNode> Sort(IEnumerable<FsNode> nodes, SortOrder order, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        List<FsNode> sorted = [.. nodes];

        if (order.Key == SortKey.Random)
        {
            Shuffle(sorted, seed);
        }
        else
        {
            Comparison<FsNode> comparison = BuildComparison(order);
            sorted.Sort(comparison);
        }

        // Directories are grouped after ordering rather than as part of it, because the grouping
        // must not be inverted when the order is reversed.
        if (order.DirectoriesFirst)
        {
            List<FsNode> directories = [.. sorted.Where(n => n.IsDirectory)];
            List<FsNode> files = [.. sorted.Where(n => !n.IsDirectory)];
            sorted = [.. directories, .. files];
        }

        return sorted;
    }

    /// <summary>Builds the comparison for an ordering, applying reversal to the key only.</summary>
    private static Comparison<FsNode> BuildComparison(SortOrder order)
    {
        Comparison<FsNode> byKey = order.Key switch
        {
            SortKey.Basename => (a, b) => CompareNames(a, b, order),
            SortKey.Size => (a, b) => Nullable.Compare(b.Size, a.Size),
            SortKey.ModificationTime => (a, b) =>
                Nullable.Compare(b.Status?.ModifyTime, a.Status?.ModifyTime),
            SortKey.ChangeTime => (a, b) =>
                Nullable.Compare(b.Status?.ChangeTime, a.Status?.ChangeTime),
            SortKey.AccessTime => (a, b) =>
                Nullable.Compare(b.Status?.AccessTime, a.Status?.AccessTime),
            SortKey.Type => (a, b) =>
                string.CompareOrdinal(a.Status?.Kind.ToString(), b.Status?.Kind.ToString()),
            SortKey.Extension => (a, b) =>
                string.CompareOrdinal(a.Extension, b.Extension),
            _ => (a, b) => a.NaturalSortKey.Compare(b.NaturalSortKey, order.CaseInsensitive),
        };

        return (a, b) =>
        {
            int result = byKey(a, b);

            // Equal keys fall back to the name, so the listing never depends on scan order.
            if (result == 0)
            {
                result = CompareNames(a, b, order);
            }

            return order.Reverse ? -result : result;
        };
    }

    private static int CompareNames(FsNode a, FsNode b, SortOrder order)
    {
        if (order.UseUnicodeCollation)
        {
            // Culture-sensitive on purpose: the sort_unicode setting exists precisely to order
            // names by the user's locale collation rather than by code point, so that accented
            // names fall where a reader of that language expects them.
#pragma warning disable CA1309
            return string.Compare(
                a.RelativePath, b.RelativePath, CultureInfo.CurrentCulture,
                order.CaseInsensitive ? CompareOptions.IgnoreCase : CompareOptions.None);
#pragma warning restore CA1309
        }

        return order.CaseInsensitive
            ? string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase)
            : string.CompareOrdinal(a.RelativePath, b.RelativePath);
    }

    /// <summary>Shuffles in place with a seeded generator, so the order is reproducible.</summary>
    private static void Shuffle(List<FsNode> nodes, int seed)
    {
        Random random = new(seed);
        for (int i = nodes.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (nodes[i], nodes[j]) = (nodes[j], nodes[i]);
        }
    }
}
