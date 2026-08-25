// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// The default scheme with green in place of blue, ported from ranger's
/// <c>colorschemes/jungle.py</c>.
/// </summary>
/// <remarks>
/// Deriving rather than restating means every rule the default gains is inherited, which is what
/// ranger does and why the two never drift apart. Only what jungle actually changes appears here.
/// </remarks>
public sealed class JungleColorScheme : DefaultColorScheme
{
    /// <inheritdoc />
    public override string Name => "jungle";

    /// <inheritdoc />
    public override Color ProgressBarColor => Color.Green;

    /// <inheritdoc />
    protected override CellStyle Use(StyleContext context)
    {
        CellStyle style = base.Use(context);

        if (context.Has(ContextKey.Directory) &&
            !context.HasAny(ContextKey.Marked, ContextKey.Link, ContextKey.InactivePane))
        {
            style = style with { Foreground = ProgressBarColor };
        }

        if (context.Has(ContextKey.LineNumber) && !context.Has(ContextKey.Selected))
        {
            style = style with
            {
                Foreground = ProgressBarColor,
                Attributes = style.Attributes & ~CellAttributes.Bold,
            };
        }

        if (context.Has(ContextKey.InTitlebar) && context.Has(ContextKey.Hostname))
        {
            // Red when running as root, blue otherwise — the default's green would be lost among
            // everything else jungle paints green.
            style = style with
            {
                Foreground = context.Has(ContextKey.Bad) ? Color.Red : Color.Blue,
            };
        }

        return style;
    }
}
