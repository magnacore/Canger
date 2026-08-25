// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;

namespace Canger.Core.Model;

/// <summary>
/// Puts a glyph before each name saying what kind of file it is.
/// </summary>
/// <remarks>
/// <para>
/// In ranger this is a plugin, and a Python one, so a configuration carried over from ranger asks
/// for a linemode Canger cannot load. It is built in here instead: the tables are data, the
/// resolution is three lookups, and there is nothing about it that needs to be a plugin.
/// </para>
/// <para>
/// The glyphs come from a patched font — Nerd Fonts or similar. Without one they render as
/// replacement boxes, which is why this is not the default.
/// </para>
/// </remarks>
/// <param name="fallback">
/// Where the right-hand side comes from, since this mode only changes the name. Defaults to the
/// size, as an ordinary listing shows.
/// </param>
public sealed class DeviconsLinemode(ILinemode? fallback = null) : ILinemode
{
    private readonly ILinemode _fallback = fallback ?? new FilenameLinemode();

    /// <inheritdoc />
    public string Name => "devicons";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        return $"{Glyph(node)} {node.RelativePath}";
    }

    /// <inheritdoc />
    public string? Detail(FsNode node, FileMetadata metadata, in LinemodeContext context) =>
        _fallback.Detail(node, metadata, context);

    /// <summary>
    /// The glyph for an entry.
    /// </summary>
    /// <param name="node">The entry.</param>
    /// <returns>A single glyph, or a generic one when nothing matches.</returns>
    /// <remarks>
    /// A whole filename beats an extension — <c>Makefile</c> and <c>.gitignore</c> have no useful
    /// extension, and <c>Dockerfile</c> would otherwise be indistinguishable from any other
    /// extensionless file.
    /// </remarks>
    public static string Glyph(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        string name = node.RelativePath;

        if (node.IsDirectory)
        {
            return DeviconTables.Directories.TryGetValue(name, out string? directory)
                ? directory
                : GenericDirectory;
        }

        if (DeviconTables.ExactNames.TryGetValue(name, out string? exact))
        {
            return exact;
        }

        string extension = Path.GetExtension(name);

        return extension.Length > 1
               && DeviconTables.Extensions.TryGetValue(extension[1..], out string? byExtension)
            ? byExtension
            : GenericFile;
    }

    /// <summary>Shown for a file nothing else matches.</summary>
    private const string GenericFile = "";

    /// <summary>Shown for a directory nothing else matches.</summary>
    private const string GenericDirectory = "";
}
