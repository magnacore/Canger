// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// Decides what colour something is drawn in, from the set of contexts that apply to it.
/// </summary>
/// <remarks>
/// Implementations should derive from <see cref="ColorScheme"/> rather than implementing this
/// directly, so that results are cached: the same handful of context sets recur for every row of
/// every frame, and resolving them repeatedly would be wasted work.
/// </remarks>
public interface IColorScheme
{
    /// <summary>The name this scheme is selected by in the configuration.</summary>
    string Name { get; }

    /// <summary>The colours and attributes for a set of contexts.</summary>
    /// <param name="context">What applies to the thing being drawn.</param>
    /// <returns>The style to draw it in.</returns>
    CellStyle Resolve(StyleContext context);
}

/// <summary>
/// Base class for colourschemes, adding caching around <see cref="Use"/>.
/// </summary>
/// <remarks>
/// A scheme's logic runs once per distinct context set and the answer is kept. In practice a
/// session produces a few dozen distinct sets, so after the first frame every lookup is a
/// dictionary hit.
/// </remarks>
public abstract class ColorScheme : IColorScheme
{
    private readonly Dictionary<StyleContext, CellStyle> _cache = [];

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public CellStyle Resolve(StyleContext context)
    {
        if (_cache.TryGetValue(context, out CellStyle cached))
        {
            return cached;
        }

        CellStyle style = Use(context);
        _cache[context] = style;
        return style;
    }

    /// <summary>Works out the style for a set of contexts.</summary>
    /// <param name="context">What applies to the thing being drawn.</param>
    /// <returns>The style to draw it in.</returns>
    protected abstract CellStyle Use(StyleContext context);

    /// <summary>Discards cached results, after something the scheme depends on has changed.</summary>
    protected void InvalidateCache() => _cache.Clear();
}
