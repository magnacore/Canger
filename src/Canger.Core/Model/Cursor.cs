// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model;

/// <summary>
/// A position within a list, kept valid as the list changes underneath it.
/// </summary>
/// <remarks>
/// <para>
/// The cursor tracks both an index and the item it was on. The index is what navigation moves;
/// the remembered item is what restores the position after a reload, so that deleting something
/// above the cursor does not make the selection jump to a different file.
/// </para>
/// <para>
/// This mirrors ranger's <c>ext/accumulator.py</c>, where the same pairing is what keeps the
/// selection sensible across sorting, filtering and refreshes.
/// </para>
/// </remarks>
public sealed class Cursor
{
    /// <summary>Where the cursor is, as an index into the list.</summary>
    public int Index { get; private set; }

    /// <summary>What the cursor was last on, used to restore the position after a change.</summary>
    public FsNode? Current { get; private set; }

    /// <summary>Moves to an index, clamping it into the list.</summary>
    /// <param name="index">The desired index.</param>
    /// <param name="items">The list being moved over.</param>
    public void MoveTo(int index, IReadOnlyList<FsNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            Index = 0;
            Current = null;
            return;
        }

        Index = Math.Clamp(index, 0, items.Count - 1);
        Current = items[Index];
    }

    /// <summary>Moves to a particular node, leaving the cursor alone when it is not present.</summary>
    /// <param name="node">The node to move to.</param>
    /// <param name="items">The list being moved over.</param>
    /// <returns><see langword="true"/> when the node was found.</returns>
    public bool MoveToNode(FsNode? node, IReadOnlyList<FsNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (node is null)
        {
            return false;
        }

        int index = IndexOf(node, items);
        if (index < 0)
        {
            return false;
        }

        Index = index;
        Current = items[index];
        return true;
    }

    /// <summary>
    /// Re-establishes a valid position after the list has changed.
    /// </summary>
    /// <remarks>
    /// The remembered node is looked for first, so that a reload leaves the selection where the
    /// user left it. Only when it has gone does the cursor fall back to the same index, clamped,
    /// which lands on whatever took its place.
    /// </remarks>
    /// <param name="items">The list as it now stands.</param>
    public void Reconcile(IReadOnlyList<FsNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            Index = 0;
            Current = null;
            return;
        }

        if (Current is not null)
        {
            int index = IndexOf(Current, items);
            if (index >= 0)
            {
                Index = index;
                Current = items[index];
                return;
            }
        }

        Index = Math.Clamp(Index, 0, items.Count - 1);
        Current = items[Index];
    }

    /// <summary>Copies another cursor's position, used when a tab adopts a directory's cursor.</summary>
    /// <param name="other">The cursor to copy.</param>
    public void CopyFrom(Cursor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Index = other.Index;
        Current = other.Current;
    }

    private static int IndexOf(FsNode node, IReadOnlyList<FsNode> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Equals(node))
            {
                return i;
            }
        }

        return -1;
    }
}
