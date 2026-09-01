// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Model;

/// <summary>
/// A file or directory as the browser sees it: its identity, its metadata, and the display state
/// that goes with it.
/// </summary>
/// <remarks>
/// <para>
/// Metadata that costs a system call or a subprocess is computed on first use and then kept, so
/// that listing a large directory does not pay for information no one looks at. Nothing here is
/// eager beyond what the directory scan already gathered.
/// </para>
/// <para>
/// Equality is by path. Directory scans build new instances each time, and marks, the copy buffer
/// and tag lookups all have to keep working across a reload, which they do because a rebuilt node
/// compares equal to the one it replaced.
/// </para>
/// </remarks>
public abstract class FsNode : IEquatable<FsNode>
{
    private string? _extension;
    private string? _basenameWithoutExtension;
    private NaturalSortKey? _naturalSortKey;

    /// <summary>Creates a node.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="status">Metadata gathered during the scan, if any.</param>
    /// <param name="linkStatus">
    /// Metadata of the link itself when this node is reached through a symbolic link.
    /// </param>
    /// <param name="relativeToPath">
    /// The directory that <see cref="RelativePath"/> is measured from. Normally the containing
    /// directory, but in flat mode it is the root of the flattened tree, which is what makes
    /// nested entries display as <c>sub/dir/file</c>.
    /// </param>
    protected FsNode(string path, FileStatus? status, FileStatus? linkStatus = null,
                     string? relativeToPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Path = path;
        Status = status;
        LinkStatus = linkStatus;
        Basename = System.IO.Path.GetFileName(path) is { Length: > 0 } name ? name : path;

        RelativePath = relativeToPath is null
            ? Basename
            : System.IO.Path.GetRelativePath(relativeToPath, path);
    }

    /// <summary>Absolute path.</summary>
    public string Path { get; }

    /// <summary>The final path component.</summary>
    public string Basename { get; }

    /// <summary>
    /// How this node is displayed: normally the basename, but a path relative to the flat root
    /// when the containing directory is flattened.
    /// </summary>
    public string RelativePath { get; private set; }

    /// <summary>
    /// Metadata describing what this node behaves like, following symbolic links.
    /// <see langword="null"/> when it could not be read.
    /// </summary>
    public FileStatus? Status { get; private set; }

    /// <summary>
    /// Metadata of the link itself, when this node was reached through a symbolic link.
    /// </summary>
    public FileStatus? LinkStatus { get; private set; }

    /// <summary>
    /// A linemode chosen for this entry specifically, overriding whatever the configured rules
    /// would select. <see langword="null"/> when the rules decide.
    /// </summary>
    /// <remarks>
    /// This is what <c>:linemode</c> sets. It lives on the entry rather than the directory
    /// because a plugin may well want to render one kind of file differently from its neighbours.
    /// Like ranger's, it does not survive a reload, since the entries are rebuilt by the scan.
    /// </remarks>
    public string? LinemodeOverride { get; set; }

    /// <summary>Whether this node is a directory, following symbolic links.</summary>
    public bool IsDirectory => Status is { IsDirectory: true };

    /// <summary>Whether this node is a symbolic link.</summary>
    public bool IsSymbolicLink => LinkStatus is { IsSymbolicLink: true };

    /// <summary>
    /// The path with symbolic links resolved: what a tag is recorded against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tags are keyed on the resolved path (<c>core/actions.py:880</c>) so that tagging a link
    /// and tagging its target are the same act — otherwise a tag applied through a link would be
    /// invisible when the real file is reached by its own name, and vice versa.
    /// </para>
    /// <para>
    /// Resolved once and remembered, because the renderer asks for it on every visible row of
    /// every frame and a link is the rare case. For anything that is not a link this is simply
    /// <see cref="Path"/>, with no syscall at all.
    /// </para>
    /// </remarks>
    public string RealPath => _realPath ??= ResolvePath();

    private string? _realPath;

    /// <summary>Follows the link chain, falling back to the path when it cannot be followed.</summary>
    private string ResolvePath()
    {
        if (!IsSymbolicLink)
        {
            return Path;
        }

        try
        {
            // ResolveLinkTarget(returnFinalTarget: true) walks the whole chain, matching
            // realpath(3) — including for a broken link, where it names the target that does not
            // exist rather than giving up. That is what ranger's os.path.realpath does too, so a
            // tag on a broken link survives whatever it pointed at coming back. Null means the
            // path is not a link at all, which only happens in a race with something deleting it.
            return (System.IO.File.ResolveLinkTarget(Path, returnFinalTarget: true)
                    ?? System.IO.Directory.ResolveLinkTarget(Path, returnFinalTarget: true))
                   ?.FullName ?? Path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Path;
        }
    }

    /// <summary>
    /// Where the link points, written the way the link itself writes it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="RealPath"/>, which walks the whole chain to an absolute path. This is what
    /// <c>readlink(2)</c> returns and what <c>ls -l</c> prints — so a link written as
    /// <c>../shared</c> shows as <c>../shared</c> rather than as wherever that resolves to.
    /// Ranger's status bar shows the same thing (<c>gui/widgets/statusbar.py:183</c>).
    /// </remarks>
    public string? LinkTarget
    {
        get
        {
            if (!IsSymbolicLink)
            {
                return null;
            }

            if (!_readLinkTarget)
            {
                _linkTarget = ReadLinkTarget();
                _readLinkTarget = true;
            }

            return _linkTarget;
        }
    }

    private string? _linkTarget;
    private bool _readLinkTarget;

    /// <summary>Reads the link's own contents, without following it.</summary>
    private string? ReadLinkTarget()
    {
        try
        {
            // Whichever of the two describes it: a link to a directory answers on DirectoryInfo
            // and a link to a file on FileInfo, and a broken one may answer on either.
            return new System.IO.FileInfo(Path).LinkTarget
                   ?? new System.IO.DirectoryInfo(Path).LinkTarget;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether this node is a symbolic link whose target could not be resolved.</summary>
    public bool IsBrokenSymbolicLink => IsSymbolicLink && Status is null;

    /// <summary>Whether the node's metadata could be read at all.</summary>
    public bool IsAccessible => Status is not null;

    /// <summary>Size in bytes, or <see langword="null"/> when it is not known.</summary>
    public virtual long? Size => Status?.Size;

    /// <summary>Whether the owner may execute this node.</summary>
    public bool IsExecutable => Status is { IsExecutableByOwner: true, Kind: FileKind.Regular };

    /// <summary>
    /// Whether the user has marked this node for a bulk operation.
    /// </summary>
    /// <remarks>
    /// Marks belong to the node rather than to the directory so that they survive re-sorting and
    /// re-filtering for free. A directory restores them by path after a rescan, since a scan
    /// builds fresh instances.
    /// </remarks>
    public bool IsMarked { get; set; }

    /// <summary>Version-control status, when the containing repository has been examined.</summary>
    public string? VcsStatus { get; set; }

    /// <summary>
    /// The measured size of everything beneath this node, once someone has asked.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> until <c>:get_cumulative_size</c> has run on it, because measuring
    /// means walking the tree and nothing should pay for that unasked. Once set it stays until
    /// the directory is rescanned, which is when it might be wrong.
    /// </remarks>
    public long? CumulativeSize { get; set; }

    /// <summary>
    /// Whether a measured size may no longer be right.
    /// </summary>
    /// <remarks>
    /// Set when the directory is re-read after having been measured. Ranger's reasoning is worth
    /// quoting, because it explains why this is a "may" and not a "has"
    /// (<c>container/directory.py:379-381</c>): <em>"this is not the first time loading. So I
    /// can't really be sure if the size has changed and I'll add a '?'."</em> Measuring again to
    /// find out would cost another walk of the whole tree, which is the thing the user asked for
    /// explicitly in the first place.
    /// </remarks>
    public bool CumulativeSizeStale { get; set; }

    /// <summary>
    /// The extension, lowercased and without the dot, or an empty string when there is none.
    /// </summary>
    /// <remarks>
    /// A leading dot does not begin an extension, so <c>.bashrc</c> has none. This matches what
    /// rifle does when matching an <c>ext</c> condition.
    /// </remarks>
    public string Extension => _extension ??= ComputeExtension();

    /// <summary>The basename with its extension removed.</summary>
    public string BasenameWithoutExtension =>
        _basenameWithoutExtension ??= Extension.Length == 0
            ? Basename
            : Basename[..(Basename.Length - Extension.Length - 1)];

    /// <summary>
    /// The key used when sorting naturally, so that <c>file2</c> sorts before <c>file10</c>.
    /// </summary>
    public NaturalSortKey NaturalSortKey => _naturalSortKey ??= new NaturalSortKey(RelativePath);

    /// <summary>
    /// What kind of thing this is, judged from its name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The colourscheme asks for this on every visible row of every frame, so it is worked out
    /// once per node and remembered. Everything it needs comes from the extension, with no
    /// syscall and no reading of the file — which is what makes it cheap enough to colour a
    /// listing by.
    /// </para>
    /// <para>
    /// The classification is ranger's (<c>container/fsobject.py:219-237</c>): the media type's
    /// first word decides image, video or audio, and two fixed extension lists decide archives
    /// and documents. Deliberately *not* <c>file(1)</c>: content is often ambiguous and this is
    /// only used to choose a colour.
    /// </para>
    /// </remarks>
    public FileCategory Category => _category ??= Classify();

    /// <summary>Whether this is an image, by name.</summary>
    public bool IsImage => Category.HasFlag(FileCategory.Image);

    /// <summary>Whether this is a video, by name.</summary>
    public bool IsVideo => Category.HasFlag(FileCategory.Video);

    /// <summary>Whether this is audio, by name.</summary>
    public bool IsAudio => Category.HasFlag(FileCategory.Audio);

    /// <summary>Whether this is an image, a video or audio.</summary>
    public bool IsMedia => (Category & FileCategory.Media) != 0;

    /// <summary>Whether this is an archive.</summary>
    public bool IsContainer => Category.HasFlag(FileCategory.Container);

    /// <summary>Whether this is a document.</summary>
    public bool IsDocument => Category.HasFlag(FileCategory.Document);

    private FileCategory? _category;

    /// <summary>Works out the category from the name.</summary>
    private FileCategory Classify()
    {
        // A partial download is whatever it will be once finished: ranger drops a `.part`
        // extension before classifying so a half-downloaded film still looks like a film.
        string name = Extension == "part" && Basename.Length > 5
            ? Basename[..^5]
            : Basename;

        string extension = System.IO.Path.GetExtension(name) is { Length: > 1 } dotted
            ? dotted[1..].ToLowerInvariant()
            : string.Empty;

        FileCategory category = FileCategory.None;

        // The media type's first word is the whole of what ranger looks at.
        if (MimeTypes.FromExtension(name) is { } mime)
        {
            if (mime.StartsWith("image/", StringComparison.Ordinal))
            {
                category |= FileCategory.Image;
            }
            else if (mime.StartsWith("video/", StringComparison.Ordinal))
            {
                category |= FileCategory.Video;
            }
            else if (mime.StartsWith("audio/", StringComparison.Ordinal))
            {
                category |= FileCategory.Audio;
            }

            if (mime.StartsWith("text/", StringComparison.Ordinal))
            {
                category |= FileCategory.Document;
            }
        }

        if (FileCategories.DocumentExtensions.Contains(extension) ||
            FileCategories.DocumentBasenames.Contains(name.ToLowerInvariant()))
        {
            category |= FileCategory.Document;
        }

        if (FileCategories.ContainerExtensions.Contains(extension))
        {
            category |= FileCategory.Container;
        }

        return category;
    }

    /// <summary>
    /// Replaces the metadata after a reload, discarding anything derived from the old values.
    /// </summary>
    /// <param name="status">The new metadata, following symbolic links.</param>
    /// <param name="linkStatus">The new metadata of the link itself.</param>
    internal void UpdateStatus(FileStatus? status, FileStatus? linkStatus)
    {
        Status = status;
        LinkStatus = linkStatus;
    }

    /// <summary>
    /// Rebases how this node is displayed, used when a directory is flattened or unflattened.
    /// </summary>
    /// <param name="relativeToPath">The directory to measure from, or <see langword="null"/>.</param>
    internal void RebaseRelativePath(string? relativeToPath)
    {
        RelativePath = relativeToPath is null
            ? Basename
            : System.IO.Path.GetRelativePath(relativeToPath, Path);

        // The natural sort key is derived from the displayed path, so it is no longer valid.
        _naturalSortKey = null;
    }

    private string ComputeExtension()
    {
        // Ranger ignores a leading dot, so a dotfile with no other dot has no extension.
        int dot = Basename.LastIndexOf('.');
        return dot <= 0 || dot == Basename.Length - 1
            ? string.Empty
            : Basename[(dot + 1)..].ToLowerInvariant();
    }

    /// <inheritdoc />
    public bool Equals(FsNode? other) =>
        other is not null && string.Equals(Path, other.Path, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FsNode);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path);

    /// <inheritdoc />
    public override string ToString() => Path;
}
