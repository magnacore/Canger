// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Ui.Styling;

/// <summary>
/// The set of context keys that apply to one thing being drawn.
/// </summary>
/// <remarks>
/// <para>
/// A row is rarely just one thing: it might be in the browser, in the main column, a directory,
/// a symbolic link, and selected, all at once. The colourscheme is handed the whole set and
/// decides from it, which is what lets a scheme say "selected wins over everything" or "a broken
/// link is red unless it is also the cursor row".
/// </para>
/// <para>
/// Stored as a bitset in two words rather than a collection, because one is built per row per
/// frame and is used as a dictionary key for the colourscheme's cache. A set is therefore free
/// to build, free to compare and free to hash.
/// </para>
/// </remarks>
public readonly struct StyleContext : IEquatable<StyleContext>
{
    private readonly ulong _low;
    private readonly ulong _high;

    private StyleContext(ulong low, ulong high)
    {
        _low = low;
        _high = high;
    }

    /// <summary>The empty set.</summary>
    public static StyleContext Empty => default;

    /// <summary>Builds a set from keys.</summary>
    /// <param name="keys">The keys that apply.</param>
    /// <returns>The set.</returns>
    public static StyleContext Of(params ReadOnlySpan<ContextKey> keys)
    {
        StyleContext context = Empty;
        foreach (ContextKey key in keys)
        {
            context = context.With(key);
        }

        return context;
    }

    /// <summary>This set with a key added.</summary>
    /// <param name="key">The key to add.</param>
    /// <returns>The extended set.</returns>
    public StyleContext With(ContextKey key)
    {
        int index = (int)key;
        return index < 64
            ? new StyleContext(_low | (1UL << index), _high)
            : new StyleContext(_low, _high | (1UL << (index - 64)));
    }

    /// <summary>This set with a key added when a condition holds.</summary>
    /// <param name="condition">Whether to add it.</param>
    /// <param name="key">The key to add.</param>
    /// <returns>The set, extended or unchanged.</returns>
    public StyleContext With(bool condition, ContextKey key) => condition ? With(key) : this;

    /// <summary>This set with a key removed.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>The reduced set.</returns>
    public StyleContext Without(ContextKey key)
    {
        int index = (int)key;
        return index < 64
            ? new StyleContext(_low & ~(1UL << index), _high)
            : new StyleContext(_low, _high & ~(1UL << (index - 64)));
    }

    /// <summary>Whether a key is in the set.</summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> when it applies.</returns>
    public bool Has(ContextKey key)
    {
        int index = (int)key;
        return index < 64
            ? (_low & (1UL << index)) != 0
            : (_high & (1UL << (index - 64))) != 0;
    }

    /// <summary>Whether any of the given keys is in the set.</summary>
    /// <param name="keys">The keys to test.</param>
    /// <returns><see langword="true"/> when at least one applies.</returns>
    public bool HasAny(params ReadOnlySpan<ContextKey> keys)
    {
        foreach (ContextKey key in keys)
        {
            if (Has(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the set is empty.</summary>
    public bool IsEmpty => _low == 0 && _high == 0;

    /// <summary>The keys in the set, in declaration order.</summary>
    /// <returns>The keys that apply.</returns>
    public IEnumerable<ContextKey> Keys()
    {
        for (int index = 0; index < ContextKeys.Count; index++)
        {
            if (Has((ContextKey)index))
            {
                yield return (ContextKey)index;
            }
        }
    }

    /// <inheritdoc />
    public bool Equals(StyleContext other) => _low == other._low && _high == other._high;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StyleContext other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_low, _high);

    /// <summary>Compares two sets.</summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><see langword="true"/> when they contain the same keys.</returns>
    public static bool operator ==(StyleContext left, StyleContext right) => left.Equals(right);

    /// <summary>Compares two sets.</summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(StyleContext left, StyleContext right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        IsEmpty ? "(none)" : string.Join(" ", Keys().Select(ContextKeys.NameOf));
}
