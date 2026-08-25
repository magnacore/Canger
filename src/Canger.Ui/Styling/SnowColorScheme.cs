// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// A monochrome scheme, ported from ranger's <c>colorschemes/snow.py</c>.
/// </summary>
/// <remarks>
/// Nothing here sets a colour: the whole scheme works through bold and reverse video. That makes
/// it the one to reach for on a terminal whose own palette should show through untouched, and on
/// anything that cannot render colour at all.
/// </remarks>
public sealed class SnowColorScheme : ColorScheme
{
    /// <inheritdoc />
    public override string Name => "snow";

    /// <inheritdoc />
    protected override CellStyle Use(StyleContext context)
    {
        if (context.Has(ContextKey.Reset))
        {
            return CellStyle.Default;
        }

        CellAttributes attributes = CellAttributes.None;

        if (context.Has(ContextKey.InBrowser))
        {
            if (context.Has(ContextKey.Selected))
            {
                attributes = CellAttributes.Reverse;
            }

            if (context.Has(ContextKey.Directory))
            {
                attributes |= CellAttributes.Bold;
            }

            if (context.Has(ContextKey.LineNumber) && !context.Has(ContextKey.Selected))
            {
                attributes |= CellAttributes.Bold;
            }
        }
        else if (context.Has(ContextKey.Highlight))
        {
            attributes |= CellAttributes.Reverse;
        }
        else if (context.Has(ContextKey.InTitlebar) &&
                 context.Has(ContextKey.Tab) &&
                 context.Has(ContextKey.Good))
        {
            attributes |= CellAttributes.Reverse;
        }
        else if (context.Has(ContextKey.InStatusbar))
        {
            // Both the progress bar and a marked count are shown by inverting, which is the only
            // emphasis available without colour.
            if (context.HasAny(ContextKey.Loaded, ContextKey.Marked))
            {
                attributes |= CellAttributes.Reverse;
            }
        }
        else if (context.Has(ContextKey.InTaskview))
        {
            if (context.Has(ContextKey.Selected))
            {
                attributes |= CellAttributes.Bold;
            }

            if (context.Has(ContextKey.Loaded))
            {
                attributes |= CellAttributes.Reverse;
            }
        }

        // Where ranger writes "fg += BRIGHT" over the terminal default, the result is the default
        // again; brightness there is carried by bold, which is already set.
        return new CellStyle(Color.Default, Color.Default, attributes);
    }
}
