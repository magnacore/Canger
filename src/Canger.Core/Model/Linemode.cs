// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.FileSystem;
using Canger.Core.State;

namespace Canger.Core.Model;

/// <summary>
/// What a linemode needs to know beyond the entry itself.
/// </summary>
/// <param name="Now">
/// The moment "recent" is judged against. Passed in rather than read from the clock so that every
/// row of a listing agrees and so tests can pin it.
/// </param>
/// <param name="BinaryPrefix">
/// Whether sizes divide by 1024 and use binary prefixes, as <c>binary_size_prefix</c> asks.
/// </param>
/// <param name="ExactBytes">
/// Whether byte counts are written out in full rather than with a prefix, per
/// <c>size_in_bytes</c>.
/// </param>
/// <param name="CountFiles">
/// Whether a directory that has not been opened should be read to count its entries, as
/// <c>automatically_count_files</c> asks. One shallow read per visible row, cached.
/// </param>
public readonly record struct LinemodeContext(
    DateTimeOffset Now,
    bool BinaryPrefix = false,
    bool CountFiles = true,
    bool ExactBytes = false);

/// <summary>
/// Decides what a row shows.
/// </summary>
/// <remarks>
/// The same listing answers different questions depending on what each row carries: size when
/// tidying up, modification time when looking for recent work, permissions when something will
/// not run. Switching between them is a keystroke rather than a different tool. Plugins add their
/// own by implementing this and registering it with <see cref="LinemodeRegistry"/>.
/// </remarks>
public interface ILinemode
{
    /// <summary>The name this mode is selected by, in <c>default_linemode</c> and <c>:linemode</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this mode reads the <c>.metadata.json</c> database, which is only loaded for modes
    /// that say they need it.
    /// </summary>
    bool UsesMetadata => false;

    /// <summary>
    /// Metadata fields without which this mode cannot render. When any is missing the default
    /// mode is used for that entry instead, so an annotated library degrades one file at a time
    /// rather than all at once.
    /// </summary>
    IReadOnlyList<string> RequiredMetadata => [];

    /// <summary>
    /// The left-aligned part of the row.
    /// </summary>
    /// <param name="node">The entry.</param>
    /// <param name="metadata">What is recorded about the entry.</param>
    /// <param name="context">The surrounding state.</param>
    /// <returns>The text.</returns>
    string Title(FsNode node, FileMetadata metadata, in LinemodeContext context);

    /// <summary>
    /// The right-aligned part of the row.
    /// </summary>
    /// <param name="node">The entry.</param>
    /// <param name="metadata">What is recorded about the entry.</param>
    /// <param name="context">The surrounding state.</param>
    /// <returns>
    /// The text; an empty string to show nothing there; or <see langword="null"/> to let the
    /// caller decide, which yields the usual size and symbolic-link marker. Only the caller knows
    /// how much room is left, so this is how a mode declines to fill the space itself.
    /// </returns>
    string? Detail(FsNode node, FileMetadata metadata, in LinemodeContext context) => null;
}

/// <summary>
/// The linemodes available by name, including any a plugin has added.
/// </summary>
public sealed class LinemodeRegistry
{
    /// <summary>The mode used when nothing selects another.</summary>
    public const string DefaultName = "filename";

    private readonly Dictionary<string, ILinemode> _modes = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>Creates a registry holding the built-in modes.</summary>
    public LinemodeRegistry()
    {
        foreach (ILinemode mode in BuiltIn)
        {
            Register(mode);
        }
    }

    /// <summary>The modes Canger ships, in the order completion offers them.</summary>
    public static IReadOnlyList<ILinemode> BuiltIn { get; } =
    [
        new FilenameLinemode(),
        new MetatitleLinemode(),
        new PermissionsLinemode(),
        new FileInfoLinemode(),
        new ModificationTimeLinemode(),
        new SizeAndModificationTimeLinemode(),
        new HumanReadableTimeLinemode(),
        new SizeAndHumanReadableTimeLinemode(),

        // `devicons` is deliberately absent: it is a plugin, as it is in ranger, generated from
        // ranger_devicons' own tables into `config/plugins/devicons.cs` and shipped there. Having
        // it here as well meant two copies of the same four hundred glyphs, generated from one
        // source by two scripts, and one of them silently shadowed by the other whenever the
        // plugin was present.
    ];

    /// <summary>The mode used when nothing selects another.</summary>
    public ILinemode Default => _modes[DefaultName];

    /// <summary>The registered names, in registration order, for completion and for help.</summary>
    public IReadOnlyList<string> Names => _order;

    /// <summary>
    /// Adds a mode, replacing any with the same name.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <remarks>
    /// Replacing rather than rejecting lets a plugin override a built-in — substituting its own
    /// <c>filename</c> mode to prefix icons, as ranger's devicons plugin does.
    /// </remarks>
    public void Register(ILinemode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        if (_modes.TryAdd(mode.Name, mode))
        {
            _order.Add(mode.Name);
        }
        else
        {
            _modes[mode.Name] = mode;
        }
    }

    /// <summary>Finds a mode by name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The mode, or <see langword="null"/> when there is no such mode.</returns>
    public ILinemode? Find(string? name) =>
        name is not null && _modes.TryGetValue(name, out ILinemode? mode) ? mode : null;

    /// <summary>Finds a mode by name, falling back to the default.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The mode.</returns>
    public ILinemode FindOrDefault(string? name) => Find(name) ?? Default;
}

/// <summary>Pieces shared by the built-in modes, and useful to a plugin writing its own.</summary>
public static class LinemodeText
{
    /// <summary>Shown where something should be known and is not.</summary>
    public const string Unknown = "?";

    /// <summary>
    /// Formats what a listing shows for an entry's size.
    /// </summary>
    /// <param name="node">The entry.</param>
    /// <param name="binaryPrefix">Whether to use binary prefixes.</param>
    /// <param name="exactBytes">Whether to write byte counts out in full, per <c>size_in_bytes</c>.</param>
    /// <param name="countFiles">Whether to read unopened directories to count them.</param>
    /// <returns>The text, or an empty string when there is no size to show.</returns>
    /// <remarks>
    /// A directory that has not been visited has not been scanned, and scanning everything in view
    /// just to print a count would make entering a large tree crawl. An unknown count shows as
    /// nothing rather than a placeholder, which also keeps the column quiet.
    /// </remarks>
    public static string Size(FsNode node, bool binaryPrefix = false, bool countFiles = true,
                             bool exactBytes = false)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.IsDirectory)
        {
            // A directory that has been measured shows what it holds rather than how many entries
            // it has. Ranger does the same by overwriting the row's infostring
            // (`container/directory.py:582-585`), which is why `dc` changes the size column rather
            // than printing somewhere else — the answer belongs beside the directory it is about.
            if (node.CumulativeSize is { } measured)
            {
                // A `?` between the number and the unit — `4.5? k` — when the directory has been
                // re-read since it was measured. Ranger marks it the same way, by handing the
                // formatter a different separator (`container/directory.py:385`).
                return (node.IsSymbolicLink ? "-> " : string.Empty)
                     + HumanReadable.Format(measured, binaryPrefix,
                                            node.CumulativeSizeStale ? "? " : " ", exactBytes);
            }

            if (node is not DirectoryNode directory)
            {
                return string.Empty;
            }

            if (directory.IsLoaded)
            {
                return directory.Count.ToString(CultureInfo.InvariantCulture);
            }

            // Not opened yet, so the count has to be read. Only when asked for: on a slow or
            // networked filesystem one read per visible row is noticeable, which is why ranger
            // makes it a setting rather than always doing it.
            return countFiles && directory.EnsureCounted() is { } counted
                ? counted.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        return node.Size is { } size
            ? HumanReadable.Format(size, binaryPrefix, exact: exactBytes)
            : string.Empty;
    }

    /// <summary>Renders the mode bits the way <c>ls -l</c> does.</summary>
    /// <param name="status">The entry's status.</param>
    /// <returns>Ten characters: the kind, then three groups of <c>rwx</c>.</returns>
    public static string Permissions(FileStatus status)
    {
        char kind = status.Kind switch
        {
            FileKind.Directory => 'd',
            FileKind.SymbolicLink => 'l',
            FileKind.Fifo => 'p',
            FileKind.Socket => 's',
            FileKind.CharacterDevice => 'c',
            FileKind.BlockDevice => 'b',
            _ => '-',
        };

        return string.Create(10, (kind, status.Permissions), static (span, state) =>
        {
            (char type, UnixFileMode mode) = state;
            ReadOnlySpan<UnixFileMode> bits =
            [
                UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute,
                UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute,
                UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute,
            ];

            span[0] = type;

            for (int i = 0; i < bits.Length; i++)
            {
                span[i + 1] = (mode & bits[i]) == 0 ? '-' : "rwx"[i % 3];
            }
        });
    }

    /// <summary>The exact modification time, to the minute.</summary>
    /// <param name="status">The entry's status.</param>
    /// <returns>The text.</returns>
    public static string ModifyTime(FileStatus status) =>
        status.ModifyTime.ToLocalTime()
              .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>The default: the name, with whatever the column normally shows beside it.</summary>
public sealed class FilenameLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "filename";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.RelativePath;
    }
}

/// <summary>
/// The title recorded in <c>.metadata.json</c> rather than the filename, with the author beside
/// it — a library view for a directory of books or films.
/// </summary>
public sealed class MetatitleLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "metatitle";

    /// <inheritdoc />
    public bool UsesMetadata => true;

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredMetadata => ["title"];

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return metadata.Year.Length > 0 ? $"{metadata.Year} - {metadata.Title}" : metadata.Title;
    }

    /// <inheritdoc />
    public string? Detail(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        string authors = metadata.Authors;

        if (authors.Length == 0)
        {
            return string.Empty;
        }

        // Only the first author: the rest would not fit and the first identifies the work.
        int comma = authors.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? authors : authors[..comma];
    }
}

/// <summary>Permissions, owner and group before the name, for when something will not run.</summary>
public sealed class PermissionsLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "permissions";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Status is { } status
            ? string.Join(' ',
                          LinemodeText.Permissions(status),
                          UserDatabase.UserName(status.Uid),
                          UserDatabase.GroupName(status.Gid),
                          node.RelativePath)
            : node.RelativePath;
    }

    /// <inheritdoc />
    /// <remarks>The row is already full; adding a size would push the name out of view.</remarks>
    public string Detail(FsNode node, FileMetadata metadata, in LinemodeContext context) =>
        string.Empty;
}

/// <summary>What <c>file(1)</c> makes of the entry, which names a format no extension reveals.</summary>
/// <remarks>
/// Asking <c>file</c> means running a program per row, which would stall drawing on a large
/// directory. The answer is therefore looked up in a cache that a background worker fills, and a
/// row shows nothing until its answer arrives — a redraw follows, so the wait is invisible.
/// </remarks>
/// <param name="describer">
/// Supplies the descriptions. When absent the mode shows nothing on the right, so registering a
/// configured instance over the built-in is what turns the mode on.
/// </param>
public sealed class FileInfoLinemode(IFileDescriber? describer = null) : ILinemode
{
    /// <inheritdoc />
    public string Name => "fileinfo";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.RelativePath;
    }

    /// <inheritdoc />
    public string? Detail(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        // A directory has no description worth printing, so the column shows its usual count.
        return node.IsDirectory ? null : describer?.Describe(node.Path) ?? string.Empty;
    }
}

/// <summary>The exact modification time.</summary>
public sealed class ModificationTimeLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "mtime";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.RelativePath;
    }

    /// <inheritdoc />
    public string Detail(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Status is { } status
            ? LinemodeText.ModifyTime(status)
            : LinemodeText.Unknown;
    }
}

/// <summary>The modification time, abbreviated to whatever identifies it.</summary>
public sealed class HumanReadableTimeLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "humanreadablemtime";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.RelativePath;
    }

    /// <inheritdoc />
    public string Detail(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Status is { } status
            ? HumanReadable.FormatTime(status.ModifyTime.ToLocalTime(), context.Now)
            : LinemodeText.Unknown;
    }
}

/// <summary>The size and the exact modification time.</summary>
public sealed class SizeAndModificationTimeLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "sizemtime";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.RelativePath;
    }

    /// <inheritdoc />
    public string Detail(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Status is { } status
            ? $"{LinemodeText.Size(node, context.BinaryPrefix, context.CountFiles, context.ExactBytes)} {LinemodeText.ModifyTime(status)}"
            : LinemodeText.Unknown;
    }
}

/// <summary>The size and an abbreviated modification time — the most that still fits.</summary>
public sealed class SizeAndHumanReadableTimeLinemode : ILinemode
{
    /// <inheritdoc />
    public string Name => "sizehumanreadablemtime";

    /// <inheritdoc />
    public string Title(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.RelativePath;
    }

    /// <inheritdoc />
    public string Detail(FsNode node, FileMetadata metadata, in LinemodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Status is not { } status)
        {
            return LinemodeText.Unknown;
        }

        // The time is right-padded so the times line up down the column even as sizes vary.
        string time = HumanReadable.FormatTime(status.ModifyTime.ToLocalTime(), context.Now);
        return $"{LinemodeText.Size(node, context.BinaryPrefix, context.CountFiles, context.ExactBytes)} {time,11}";
    }
}

/// <summary>Describes a file the way <c>file(1)</c> does, without blocking the caller.</summary>
public interface IFileDescriber
{
    /// <summary>
    /// The description of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>
    /// The description, or an empty string when it is not known yet. An unknown answer is
    /// expected to be computed in the background and to be available on a later call.
    /// </returns>
    string Describe(string path);
}
