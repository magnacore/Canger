// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model;

/// <summary>
/// A bounded history that can be walked backwards and forwards.
/// </summary>
/// <remarks>
/// <para>
/// This is a list with a position in it rather than a stack, which is what makes going back and
/// then forward again work. Adding an entry while part-way back discards what was ahead, exactly
/// as a browser does.
/// </para>
/// <para>
/// Used for both directory history per tab and command history in the console. The console wants
/// repeated entries collapsed so that pressing up does not walk through the same command five
/// times; directory history does not, so that is a option rather than a fixed behaviour.
/// </para>
/// </remarks>
/// <typeparam name="T">What is remembered.</typeparam>
public sealed class History<T>(int capacity, bool unique = false)
    where T : notnull
{
    private readonly List<T> _entries = [];

    /// <summary>The entries, oldest first.</summary>
    public IReadOnlyList<T> Entries => _entries;

    /// <summary>Where in the history the cursor sits.</summary>
    public int Position { get; private set; } = -1;

    /// <summary>How many entries are remembered.</summary>
    public int Count => _entries.Count;

    /// <summary>The most entries this will keep.</summary>
    public int Capacity => capacity;

    /// <summary>Whether anything is remembered.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>The entry at the current position.</summary>
    public T? Current => Position >= 0 && Position < _entries.Count ? _entries[Position] : default;

    /// <summary>Whether there is anywhere to go back to.</summary>
    public bool CanGoBack => Position > 0;

    /// <summary>Whether there is anywhere to go forward to.</summary>
    public bool CanGoForward => Position >= 0 && Position < _entries.Count - 1;

    /// <summary>
    /// Records an entry, discarding anything that was ahead of the current position.
    /// </summary>
    /// <param name="entry">What to remember.</param>
    public void Add(T entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Going back and then somewhere new abandons the old forward path.
        if (Position >= 0 && Position < _entries.Count - 1)
        {
            _entries.RemoveRange(Position + 1, _entries.Count - Position - 1);
        }

        if (unique)
        {
            _entries.Remove(entry);
        }
        else if (_entries.Count > 0 && _entries[^1].Equals(entry))
        {
            // Repeating the current entry is not a move, so it is not recorded again.
            Position = _entries.Count - 1;
            return;
        }

        _entries.Add(entry);

        // Trim from the front, so the oldest entries are the ones lost.
        while (capacity > 0 && _entries.Count > capacity)
        {
            _entries.RemoveAt(0);
        }

        Position = _entries.Count - 1;
    }

    /// <summary>Steps back and returns the entry there.</summary>
    /// <returns>The entry, or <see langword="default"/> when already at the oldest.</returns>
    public T? Back()
    {
        if (!CanGoBack)
        {
            return default;
        }

        Position--;
        return Current;
    }

    /// <summary>Steps forward and returns the entry there.</summary>
    /// <returns>The entry, or <see langword="default"/> when already at the newest.</returns>
    public T? Forward()
    {
        if (!CanGoForward)
        {
            return default;
        }

        Position++;
        return Current;
    }

    /// <summary>Moves several steps at once, clamping at either end.</summary>
    /// <param name="offset">How far to move. Negative goes back.</param>
    /// <returns>The entry arrived at.</returns>
    public T? Move(int offset)
    {
        if (_entries.Count == 0)
        {
            return default;
        }

        Position = Math.Clamp(Position + offset, 0, _entries.Count - 1);
        return Current;
    }

    /// <summary>
    /// Finds the nearest earlier entry whose text starts with a prefix, for prefix-filtered
    /// history search in the console.
    /// </summary>
    /// <param name="prefix">What the entry must start with.</param>
    /// <param name="direction">Which way to search. Negative searches backwards.</param>
    /// <returns>The entry found, or <see langword="default"/> when there is none.</returns>
    public T? Search(string prefix, int direction)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (_entries.Count == 0 || direction == 0)
        {
            return default;
        }

        int step = Math.Sign(direction);
        for (int index = Position + step; index >= 0 && index < _entries.Count; index += step)
        {
            if (_entries[index].ToString() is { } text &&
                text.StartsWith(prefix, StringComparison.Ordinal))
            {
                Position = index;
                return Current;
            }
        }

        return default;
    }

    /// <summary>Moves to the newest entry.</summary>
    public void FastForward() => Position = _entries.Count - 1;

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        _entries.Clear();
        Position = -1;
    }

    /// <summary>
    /// Adopts another history's past, so that a newly opened tab inherits where its parent has
    /// been rather than starting blank.
    /// </summary>
    /// <param name="other">The history to copy from.</param>
    public void InheritFrom(History<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        _entries.Clear();
        _entries.AddRange(other._entries);
        Position = other.Position;
    }
}
