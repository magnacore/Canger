// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// A Solarized-like scheme, ported from ranger's <c>colorschemes/solarized.py</c>.
/// </summary>
/// <remarks>
/// Unlike the default scheme this one names specific 256-colour palette indices rather than the
/// eight basic colours, so it looks the same whatever the terminal's own theme is — which is the
/// point of Solarized. It therefore needs a terminal with a 256-colour palette.
/// </remarks>
public sealed class SolarizedColorScheme : ColorScheme
{
    // The palette indices Solarized uses, named so the rules below read as intent rather than
    // as a list of numbers.
    private static Color Base00 => Color.FromIndex(244);
    private static Color Base02 => Color.FromIndex(235);
    private static Color Base03 => Color.FromIndex(234);
    private static Color Inactive => Color.FromIndex(241);
    private static Color Marked => Color.FromIndex(237);
    private static Color Blue => Color.FromIndex(33);
    private static Color Green => Color.FromIndex(64);
    private static Color BrightGreen => Color.FromIndex(47);
    private static Color Cyan => Color.FromIndex(37);
    private static Color Yellow => Color.FromIndex(136);
    private static Color Orange => Color.FromIndex(166);
    private static Color RedIndex => Color.FromIndex(160);
    private static Color Violet => Color.FromIndex(61);
    private static Color Purple => Color.FromIndex(93);
    private static Color Paper => Color.FromIndex(230);
    private static Color Foreground => Color.FromIndex(255);
    private static Color Tab => Color.FromIndex(239);

    /// <inheritdoc />
    public override string Name => "solarized";

    /// <summary>The colour of the progress bar drawn over the status bar.</summary>
    public static Color ProgressBarColor => Blue;

    /// <inheritdoc />
    protected override CellStyle Use(StyleContext context)
    {
        if (context.Has(ContextKey.Reset))
        {
            return CellStyle.Default;
        }

        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.None;

        if (context.Has(ContextKey.InBrowser))
        {
            (foreground, background, attributes) = Browser(context);
        }
        else if (context.Has(ContextKey.InTitlebar))
        {
            (foreground, background, attributes) = TitleBar(context);
        }
        else if (context.Has(ContextKey.InStatusbar))
        {
            (foreground, background, attributes) = StatusBar(context);
        }

        // Unlike the browser and bars, these two are additive rather than exclusive: ranger tests
        // them outside the chain, so a highlighted task title picks up both.
        if (context.Has(ContextKey.Text) && context.Has(ContextKey.Highlight))
        {
            attributes |= CellAttributes.Reverse;
        }

        if (context.Has(ContextKey.InTaskview))
        {
            (foreground, background, attributes) = TaskView(context, foreground, background,
                                                            attributes);
        }

        return new CellStyle(foreground, background, attributes);
    }

    /// <summary>Colours for a row in a file listing.</summary>
    private static (Color Foreground, Color Background, CellAttributes Attributes) Browser(
        StyleContext context)
    {
        Color foreground = Base00;
        Color background = Color.Default;

        // Solarized sets rather than accumulates the attribute here, so a selected row starts
        // from reverse alone and an unselected one from nothing.
        CellAttributes attributes = context.Has(ContextKey.Selected)
            ? CellAttributes.Reverse
            : CellAttributes.None;

        if (context.HasAny(ContextKey.Empty, ContextKey.Error))
        {
            foreground = Base02;
            background = RedIndex;
        }

        if (context.Has(ContextKey.Border))
        {
            foreground = Color.Default;
        }

        if (context.Has(ContextKey.Media))
        {
            foreground = context.Has(ContextKey.Image) ? Yellow : Orange;
        }

        if (context.Has(ContextKey.Container))
        {
            foreground = Violet;
        }

        if (context.Has(ContextKey.Directory))
        {
            foreground = Blue;
        }
        else if (context.Has(ContextKey.Executable) &&
                 !context.HasAny(ContextKey.Media, ContextKey.Container,
                                 ContextKey.Fifo, ContextKey.Socket))
        {
            foreground = Green;
            attributes |= CellAttributes.Bold;
        }

        if (context.Has(ContextKey.Socket) || context.Has(ContextKey.Fifo))
        {
            foreground = Yellow;
            background = Paper;
            attributes |= CellAttributes.Bold;
        }

        if (context.Has(ContextKey.Device))
        {
            foreground = Base00;
            background = Paper;
            attributes |= CellAttributes.Bold;
        }

        if (context.Has(ContextKey.Link))
        {
            foreground = context.Has(ContextKey.Good) ? Cyan : RedIndex;
            attributes |= CellAttributes.Bold;

            if (context.Has(ContextKey.Bad))
            {
                background = Base02;
            }
        }

        if (context.Has(ContextKey.TagMarker) && !context.Has(ContextKey.Selected))
        {
            attributes |= CellAttributes.Bold;
            foreground = foreground == Color.Red || foreground == Color.Magenta
                ? Color.White
                : Color.Red;
        }

        if (!context.Has(ContextKey.Selected) &&
            context.HasAny(ContextKey.Cut, ContextKey.Copied))
        {
            foreground = Base03;
            attributes |= CellAttributes.Bold;
        }

        if (context.Has(ContextKey.MainColumn))
        {
            if (context.Has(ContextKey.Selected))
            {
                attributes |= CellAttributes.Bold;
            }

            if (context.Has(ContextKey.Marked))
            {
                attributes |= CellAttributes.Bold;
                background = Marked;
            }
        }

        if (context.Has(ContextKey.BadInfo))
        {
            // On a reversed row the foreground is what the terminal paints as background, so the
            // warning has to go on the other channel to stay visible.
            if ((attributes & CellAttributes.Reverse) != 0)
            {
                background = Color.Magenta;
            }
            else
            {
                foreground = Color.Magenta;
            }
        }

        if (context.Has(ContextKey.InactivePane))
        {
            foreground = Inactive;
        }

        return (foreground, background, attributes);
    }

    /// <summary>Colours for the title bar.</summary>
    private static (Color Foreground, Color Background, CellAttributes Attributes) TitleBar(
        StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;

        if (context.Has(ContextKey.Hostname))
        {
            // Orange rather than the usual foreground when running as root, as a standing warning.
            foreground = context.Has(ContextKey.Bad) ? Color.Default : Foreground;

            if (context.Has(ContextKey.Bad))
            {
                background = Orange;
            }
        }
        else if (context.Has(ContextKey.Directory))
        {
            foreground = Blue;
        }
        else if (context.Has(ContextKey.Tab))
        {
            foreground = context.Has(ContextKey.Good) ? BrightGreen : Blue;
            background = Tab;
        }
        else if (context.Has(ContextKey.Link))
        {
            foreground = Color.Cyan;
        }

        return (foreground, background, CellAttributes.Bold);
    }

    /// <summary>Colours for the status bar.</summary>
    private static (Color Foreground, Color Background, CellAttributes Attributes) StatusBar(
        StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.None;

        if (context.Has(ContextKey.Permissions))
        {
            if (context.Has(ContextKey.Good))
            {
                foreground = Purple;
            }
            else if (context.Has(ContextKey.Bad))
            {
                foreground = RedIndex;
                background = Base02;
            }
        }

        if (context.Has(ContextKey.Marked))
        {
            attributes |= CellAttributes.Bold | CellAttributes.Reverse;
            foreground = Marked;
            background = BrightGreen;
        }

        if (context.Has(ContextKey.Message) && context.Has(ContextKey.Bad))
        {
            attributes |= CellAttributes.Bold;
            foreground = RedIndex;
            background = Base02;
        }

        if (context.Has(ContextKey.Loaded))
        {
            background = ProgressBarColor;
        }

        return (foreground, background, attributes);
    }

    /// <summary>Colours for the task view, layered over whatever was decided already.</summary>
    private static (Color Foreground, Color Background, CellAttributes Attributes) TaskView(
        StyleContext context, Color foreground, Color background, CellAttributes attributes)
    {
        if (context.Has(ContextKey.Title))
        {
            foreground = Purple;
        }

        if (context.Has(ContextKey.Selected))
        {
            attributes |= CellAttributes.Reverse;
        }

        if (context.Has(ContextKey.Loaded))
        {
            if (context.Has(ContextKey.Selected))
            {
                foreground = ProgressBarColor;
            }
            else
            {
                background = ProgressBarColor;
            }
        }

        return (foreground, background, attributes);
    }
}
