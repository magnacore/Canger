// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// Canger's default colours, ported from ranger's <c>colorschemes/default.py</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rules are applied in order and later ones refine earlier ones, so a broken symbolic link
/// under the cursor picks up both the link colouring and the selected highlight. That layering is
/// why the method reads as a sequence of conditions rather than a lookup table.
/// </para>
/// <para>
/// Colours are palette indices rather than fixed RGB, so the result follows whatever the user's
/// terminal theme defines. Where ranger writes <c>fg += BRIGHT</c>, Canger calls
/// <see cref="Color.Bright"/>, which is idempotent and so cannot double-apply.
/// </para>
/// </remarks>
/// <remarks>
/// Not sealed: the jungle scheme is a handful of overrides on top of these rules, exactly as in
/// ranger, and deriving keeps the two from drifting apart.
/// </remarks>
public class DefaultColorScheme : ColorScheme
{
    /// <inheritdoc />
    public override string Name => "default";

    /// <summary>The colour of the progress bar drawn over the status bar.</summary>
    public virtual Color ProgressBarColor => Color.Blue;

    /// <inheritdoc />
    protected override CellStyle Use(StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.None;

        if (context.Has(ContextKey.Reset))
        {
            return CellStyle.Default;
        }

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
        else if (context.Has(ContextKey.InTaskview))
        {
            (foreground, background, attributes) = TaskView(context);
        }

        if (context.Has(ContextKey.Highlight))
        {
            attributes |= CellAttributes.Reverse;
        }

        return new CellStyle(foreground, background, attributes);
    }

    /// <summary>Colours for a row in a file listing.</summary>
    private static (Color Foreground, Color Background, CellAttributes Attributes) Browser(
        StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.None;

        if (context.HasAny(ContextKey.Empty, ContextKey.Error))
        {
            background = Color.Red;
        }

        if (context.Has(ContextKey.Media))
        {
            foreground = context.Has(ContextKey.Image) ? Color.Yellow : Color.Magenta;
        }

        if (context.Has(ContextKey.Container))
        {
            foreground = Color.Red;
        }

        if (context.Has(ContextKey.Directory))
        {
            attributes |= CellAttributes.Bold;
            foreground = Color.Blue.Bright();
        }
        else if (context.Has(ContextKey.Executable) &&
                 !context.HasAny(ContextKey.Media, ContextKey.Container,
                                 ContextKey.Fifo, ContextKey.Socket))
        {
            attributes |= CellAttributes.Bold;
            foreground = Color.Green.Bright();
        }

        if (context.Has(ContextKey.Socket))
        {
            attributes |= CellAttributes.Bold;
            foreground = Color.Magenta.Bright();
        }

        if (context.Has(ContextKey.Fifo))
        {
            foreground = Color.Yellow;
        }

        if (context.Has(ContextKey.Device))
        {
            attributes |= CellAttributes.Bold;
            foreground = Color.Yellow.Bright();
        }

        if (context.Has(ContextKey.Link))
        {
            // A link that resolves is cyan; one that does not is magenta, so a broken link is
            // obvious without having to follow it.
            foreground = context.Has(ContextKey.Good) ? Color.Cyan : Color.Magenta;
        }

        if (context.Has(ContextKey.TagMarker) && !context.Has(ContextKey.Selected))
        {
            attributes |= CellAttributes.Bold;
            foreground = foreground == Color.Red || foreground == Color.Magenta
                ? Color.White.Bright()
                : Color.Red.Bright();
        }

        if (context.Has(ContextKey.LineNumber) && !context.Has(ContextKey.Selected))
        {
            foreground = Color.Default;
            attributes &= ~CellAttributes.Bold;
        }

        // The version-control mark beside a name. Every one of these keys was already being set
        // on the marker and no scheme read any of them, so all six statuses drew in whatever
        // colour the row happened to have — which is half of why the glyphs had to carry so much.
        // Ranger's own assignment (`colorschemes/default.py:156-171`), including leaving ignored
        // at the default colour: the quiet mark stays quiet.
        if (context.Has(ContextKey.VcsFile) && !context.Has(ContextKey.Selected))
        {
            attributes &= ~CellAttributes.Bold;

            if (context.Has(ContextKey.VcsConflict))
            {
                foreground = Color.Magenta;
            }
            else if (context.Has(ContextKey.VcsUntracked))
            {
                foreground = Color.Cyan;
            }
            else if (context.HasAny(ContextKey.VcsChanged, ContextKey.VcsUnknown))
            {
                foreground = Color.Red;
            }
            else if (context.HasAny(ContextKey.VcsStaged, ContextKey.VcsSync))
            {
                foreground = Color.Green;
            }
            else if (context.Has(ContextKey.VcsIgnored))
            {
                foreground = Color.Default;
            }
        }

        if (context.HasAny(ContextKey.Cut, ContextKey.Copied) && !context.Has(ContextKey.Selected))
        {
            attributes |= CellAttributes.Bold;
            foreground = Color.Black.Bright();
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
                foreground = Color.Yellow;
            }
        }

        if (context.Has(ContextKey.BadInfo))
        {
            foreground = Color.Magenta;
        }

        if (context.Has(ContextKey.InactivePane))
        {
            foreground = Color.Cyan;
        }

        // The cursor row is drawn in reverse video, which is what makes it visible whatever
        // colour the entry itself is.
        if (context.Has(ContextKey.Selected))
        {
            attributes |= CellAttributes.Reverse;
        }

        return (foreground, background, attributes);
    }

    /// <summary>Colours for the title bar.</summary>
    private static (Color Foreground, Color Background, CellAttributes Attributes) TitleBar(
        StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.Bold;

        if (context.Has(ContextKey.Hostname))
        {
            // Red rather than green when running as root, as a standing warning.
            foreground = context.Has(ContextKey.Bad) ? Color.Red : Color.Green;
        }

        if (context.Has(ContextKey.Directory))
        {
            foreground = Color.Blue;
        }

        if (context.Has(ContextKey.Tab) && context.Has(ContextKey.Good))
        {
            background = Color.Green;
        }

        if (context.Has(ContextKey.Link))
        {
            foreground = Color.Cyan;
        }

        return (foreground, background, attributes);
    }

    /// <summary>Colours for the status bar.</summary>
    private (Color Foreground, Color Background, CellAttributes Attributes) StatusBar(
        StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.None;

        if (context.Has(ContextKey.Permissions))
        {
            foreground = context.Has(ContextKey.Good) ? Color.Cyan : Color.Magenta;
        }

        if (context.Has(ContextKey.Marked))
        {
            attributes |= CellAttributes.Bold | CellAttributes.Reverse;
            foreground = Color.Yellow.Bright();
        }

        if (context.Has(ContextKey.Frozen))
        {
            attributes |= CellAttributes.Bold | CellAttributes.Reverse;
            foreground = Color.Cyan.Bright();
        }

        if (context.Has(ContextKey.Message) && context.Has(ContextKey.Bad))
        {
            attributes |= CellAttributes.Bold;
            foreground = Color.Red.Bright();
        }

        if (context.Has(ContextKey.Loaded))
        {
            background = ProgressBarColor;
        }

        if (context.Has(ContextKey.VcsInfo))
        {
            foreground = Color.Blue;
            attributes &= ~CellAttributes.Bold;
        }

        if (context.Has(ContextKey.VcsCommit))
        {
            foreground = Color.Yellow;
            attributes &= ~CellAttributes.Bold;
        }

        if (context.Has(ContextKey.VcsDate))
        {
            foreground = Color.Cyan;
            attributes &= ~CellAttributes.Bold;
        }

        return (foreground, background, attributes);
    }

    /// <summary>Colours for the task view.</summary>
    private (Color Foreground, Color Background, CellAttributes Attributes) TaskView(
        StyleContext context)
    {
        Color foreground = Color.Default;
        Color background = Color.Default;
        CellAttributes attributes = CellAttributes.None;

        if (context.Has(ContextKey.Title))
        {
            foreground = Color.Blue;
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
