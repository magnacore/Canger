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

    private Color? _barColor;
    private Color? _barTextColor;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>The colour a progress bar fills a row with.</summary>
    /// <remarks>
    /// The configured colour where there is one, and otherwise the scheme's own.
    /// </remarks>
    public Color ProgressBarColor => _barColor ?? BarColor;

    /// <summary>The colour of text drawn over that fill.</summary>
    /// <remarks>
    /// <para>
    /// Ranger sets only the background and leaves the text at whatever it was — measured, the
    /// status bar emits <c>ESC[0;44m</c>, a reset followed by background blue, so the text keeps
    /// the terminal's default foreground. That reads well only if the terminal renders that
    /// background dark, and a palette that maps blue to something light leaves pale text on a pale
    /// bar. Naming both halves is what makes the pairing hold whatever the palette does.
    /// </para>
    /// <para>
    /// A deliberate divergence, and the reason schemes here pick a <em>bright</em> fill: bright
    /// colours are light by convention, so dark text over them is legible in any palette, while
    /// the direction of contrast for an ordinary colour cannot be known in advance.
    /// </para>
    /// </remarks>
    public Color ProgressBarTextColor => _barTextColor ?? BarTextColor;

    /// <summary>The scheme's own fill colour, before any configuration.</summary>
    protected virtual Color BarColor => Color.Blue.Bright();

    /// <summary>The scheme's own colour for text over the fill.</summary>
    protected virtual Color BarTextColor => Color.Black;

    /// <summary>Overrides the progress bar's colours, as the configuration asks.</summary>
    /// <param name="fill">The fill colour, or <see langword="null"/> for the scheme's own.</param>
    /// <param name="text">The text colour, or <see langword="null"/> for the scheme's own.</param>
    /// <returns><see langword="true"/> when this changed anything, so the caller can redraw.</returns>
    /// <remarks>
    /// Every resolved style is memoised, so a change of colour has to discard what was worked out
    /// under the old one — otherwise the setting appears to do nothing until something else forces
    /// a repaint.
    /// </remarks>
    public bool SetProgressBarColors(Color? fill, Color? text)
    {
        if (_barColor == fill && _barTextColor == text)
        {
            return false;
        }

        _barColor = fill;
        _barTextColor = text;
        InvalidateCache();

        return true;
    }

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
