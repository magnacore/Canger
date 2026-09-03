// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// Reads a colour written as configuration.
/// </summary>
/// <remarks>
/// The eight names every terminal has, optionally brightened, or a palette index for the 256
/// colours beyond them. Names rather than numbers are what make a setting portable between
/// terminals, since each one decides for itself what its blue looks like; the index is there for
/// anyone who has a particular shade in mind and knows their palette.
/// </remarks>
public static class ColorNames
{
    /// <summary>The eight, in the order every terminal numbers them.</summary>
    private static readonly string[] Names =
        ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white"];

    /// <summary>Reads a colour.</summary>
    /// <param name="text">A name, a name with a <c>bright_</c> prefix, or a number from 0 to 255.</param>
    /// <param name="color">The colour read.</param>
    /// <returns><see langword="false"/> when the text names no colour.</returns>
    /// <remarks>
    /// <c>default</c> is the terminal's own foreground or background, which is a colour like any
    /// other here. An empty setting is not handled by this at all: it means "whatever the scheme
    /// chose", which is a different thing from "the terminal's default" and is decided by the
    /// caller.
    /// </remarks>
    public static bool TryParse(string? text, out Color color)
    {
        color = Color.Default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string wanted = text.Trim().ToLowerInvariant().Replace('-', '_');

        if (wanted == "default")
        {
            return true;
        }

        if (int.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
            index is >= 0 and <= 255)
        {
            color = Color.FromIndex(index);
            return true;
        }

        bool bright = wanted.StartsWith("bright_", StringComparison.Ordinal);
        string name = bright ? wanted["bright_".Length..] : wanted;
        int at = Array.IndexOf(Names, name);

        if (at < 0)
        {
            return false;
        }

        color = bright ? Color.FromIndex(at).Bright() : Color.FromIndex(at);

        return true;
    }

    /// <summary>Every value a colour setting accepts, for <c>:set</c> and for the manual.</summary>
    public static IReadOnlyList<string> Accepted =>
        ["", "default", .. Names, .. Names.Select(name => "bright_" + name)];
}
