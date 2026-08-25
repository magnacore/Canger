// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Preview.Images;

/// <summary>
/// Chooses how to show images.
/// </summary>
/// <remarks>
/// The user names a protocol in the configuration, but naming one the terminal cannot do should
/// leave previews working without images rather than producing rubbish on screen — so a
/// configured protocol that reports itself unavailable falls back to showing nothing.
/// </remarks>
public static class ImageDisplayFactory
{
    /// <summary>The protocols Canger can use, by their configuration names.</summary>
    public static IReadOnlyList<string> SupportedNames { get; } = ["kitty", "ueberzug"];

    /// <summary>
    /// Builds a display for a named protocol.
    /// </summary>
    /// <param name="name">The protocol, from the <c>preview_images_method</c> setting.</param>
    /// <param name="output">Where escape sequences are written, for protocols that use them.</param>
    /// <returns>
    /// The display, or one that does nothing when the protocol is unknown or unusable here.
    /// </returns>
    public static IImageDisplay Create(string name, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        IImageDisplay display = name switch
        {
            "kitty" => new KittyImageDisplay(output),
            "ueberzug" => new UeberzugImageDisplay(),

            // The remaining protocols ranger supports — w3m, iterm2, sixel, terminology, urxvt —
            // are not implemented yet. Naming one shows previews without images rather than
            // failing, which is the same outcome as an unsupported terminal.
            _ => new NoImageDisplay(),
        };

        if (display.IsAvailable)
        {
            return display;
        }

        display.Dispose();
        return new NoImageDisplay();
    }
}
