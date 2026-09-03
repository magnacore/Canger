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

    /// <summary>
    /// The green jungle paints its directories, line numbers and progress bar with.
    /// </summary>
    /// <remarks>
    /// Held separately from <see cref="ColorScheme.ProgressBarColor"/>, which it merely supplies
    /// the default for. They were the same property, so configuring the bar's colour would have
    /// repainted every directory name in the browser — a setting reaching a long way past what it
    /// names.
    /// </remarks>
    private static Color Accent => Color.Green;

    /// <inheritdoc />
    protected override Color BarColor => Accent.Bright();

    /// <inheritdoc />
    protected override CellStyle Use(StyleContext context)
    {
        CellStyle style = base.Use(context);

        if (context.Has(ContextKey.Directory) &&
            !context.HasAny(ContextKey.Marked, ContextKey.Link, ContextKey.InactivePane))
        {
            style = style with { Foreground = Accent };
        }

        if (context.Has(ContextKey.LineNumber) && !context.Has(ContextKey.Selected))
        {
            style = style with
            {
                Foreground = Accent,
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
