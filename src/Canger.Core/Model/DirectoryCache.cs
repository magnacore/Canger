// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Model;

/// <summary>
/// Hands out directory objects, returning the same instance for the same path.
/// </summary>
/// <remarks>
/// <para>
/// This single-instance-per-path rule is load-bearing rather than an optimisation. The main
/// column, the parent column beside it, and every tab that has the directory open all hold the
/// same object, so moving the cursor in one place is reflected everywhere without any
/// synchronisation, and switching tabs costs nothing because the listing is already there.
/// </para>
/// <para>
/// Ranger relies on exactly the same interning (<c>core/fm.py:424-432</c>). Files are not
/// interned: a scan builds them afresh each time, and they compare equal by path, which is what
/// lets marks and the copy buffer survive a reload.
/// </para>
/// </remarks>
public sealed class DirectoryCache(IFileSystem fileSystem)
{
    /// <summary>Whether listings are held as they are rather than re-read.</summary>
    /// <remarks>
    /// The <c>freeze_files</c> setting. It lives here rather than on each node because
    /// <see cref="DirectoryNode.Load"/> is reached from a dozen places — entering, the outdated
    /// check, a finished copy, a preview column — and none of them should have to remember to
    /// ask. Ranger puts the same test at the top of <c>load_content</c> and of
    /// <c>FileSystemObject.load</c> (<c>container/directory.py:500</c>,
    /// <c>container/fsobject.py:288</c>).
    ///
    /// It is for looking at something that is changing underneath you — a directory being
    /// written to — without the listing moving while you read it.
    /// </remarks>
    public bool Frozen { get; set; }

    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly Dictionary<string, DirectoryNode> _directories = new(StringComparer.Ordinal);

    /// <summary>
    /// The listing settings every directory is born with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A directory used to be created with the defaults and given the real settings a frame later,
    /// once something walked the visible columns. That is a frame too late: the first load has
    /// already ordered the entries by name and put the cursor on the first of them, and when the
    /// real order arrives the cursor follows that entry to wherever it now belongs. Opening a
    /// folder under <c>sort=mtime</c> therefore landed on the alphabetically first name, halfway
    /// down the list, instead of on the first row.
    /// </para>
    /// <para>
    /// Applied whenever a directory is handed out, not only when one is made. Stamping at
    /// creation alone fixed nothing: a subdirectory's node is created while its *parent* is being
    /// listed, which is long before the user changes the sort, so by the time the folder is opened
    /// the node already exists and was skipped. Every setter re-derives only on a change, so
    /// applying them on the way past costs nothing.
    /// </para>
    /// <para>
    /// Ranger has no such gap because a directory binds itself to these settings in its
    /// constructor (<c>container/directory.py:140-148</c>). This is that, in the one place every
    /// directory is fetched.
    /// </para>
    /// </remarks>
    public DirectorySettings Settings { get; set; } = DirectorySettings.Default;

    /// <summary>How many directories are currently held.</summary>
    public int Count => _directories.Count;

    /// <summary>The paths currently held, for diagnostics and tests.</summary>
    public IReadOnlyCollection<string> Paths => _directories.Keys;

    /// <summary>
    /// Returns the directory for a path, creating it the first time and returning the same
    /// instance thereafter.
    /// </summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>The directory.</returns>
    public DirectoryNode Get(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string key = Normalize(path);

        if (_directories.TryGetValue(key, out DirectoryNode? existing))
        {
            Settings.ApplyTo(existing);
            return existing;
        }

        DirectoryNode directory = new(
            _fileSystem, key, _fileSystem.GetStatus(key, followSymbolicLinks: true),
            _fileSystem.GetStatus(key), relativeToPath: null, cache: this);

        Settings.ApplyTo(directory);
        _directories[key] = directory;
        return directory;
    }

    /// <summary>
    /// The shared node for a directory a scan has just described.
    /// </summary>
    /// <param name="path">Absolute path of the directory.</param>
    /// <param name="status">Its metadata, following links, as the scan read it.</param>
    /// <param name="linkStatus">Its own metadata, as the scan read it.</param>
    /// <param name="relativeToPath">What the display path is measured from, for flat mode.</param>
    /// <returns>The one node that stands for this directory.</returns>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="Get"/> because the caller already has the metadata: a scan has
    /// just stat'd the entry, and re-reading it here would double the syscalls of every listing.
    /// </para>
    /// <para>
    /// This is what makes a directory *one object* however it is reached. Ranger interns the same
    /// way from inside its scan (<c>container/directory.py:428</c>), and it is the reason a
    /// measured size, a cursor position or a mark set on a directory is still there when it is
    /// reached from somewhere else.
    /// </para>
    /// </remarks>
    public DirectoryNode Intern(string path, FileStatus? status, FileStatus? linkStatus,
                                string? relativeToPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string key = Normalize(path);

        if (_directories.TryGetValue(key, out DirectoryNode? existing))
        {
            // The metadata is newer than what the node was holding, and the display path belongs
            // to the listing being built — a flattened listing measures it from further up.
            existing.UpdateStatus(status, linkStatus);
            existing.RebaseRelativePath(relativeToPath);
            Settings.ApplyTo(existing);

            return existing;
        }

        DirectoryNode directory = new(_fileSystem, key, status, linkStatus, relativeToPath, this);

        Settings.ApplyTo(directory);
        _directories[key] = directory;

        return directory;
    }

    /// <summary>
    /// Returns the directory for a path, loading it if it has not been scanned yet.
    /// </summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="cancellationToken">Abandons the scan.</param>
    /// <returns>The loaded directory.</returns>
    public DirectoryNode GetLoaded(string path, CancellationToken cancellationToken = default)
    {
        DirectoryNode directory = Get(path);
        if (!directory.IsLoaded)
        {
            directory.Load(cancellationToken);
        }

        return directory;
    }

    /// <summary>
    /// The chain of directories from the filesystem root down to a path, which is what the
    /// breadcrumb columns to the left of the current directory display.
    /// </summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>The directories, root first, ending with <paramref name="path"/> itself.</returns>
    public IReadOnlyList<DirectoryNode> PathwayTo(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        List<DirectoryNode> pathway = [];
        string current = Normalize(path);

        while (true)
        {
            pathway.Add(Get(current));

            if (current == "/")
            {
                break;
            }

            current = ParentOf(current);
        }

        pathway.Reverse();
        return pathway;
    }

    /// <summary>Forgets a directory, so the next request rebuilds it.</summary>
    /// <param name="path">Absolute path.</param>
    /// <returns><see langword="true"/> when a directory was held for that path.</returns>
    public bool Evict(string path) => _directories.Remove(Normalize(path));

    /// <summary>Drops the listings of directories nobody is sitting on and nobody has touched.</summary>
    /// <param name="keep">Directories to leave alone, whatever their age.</param>
    /// <param name="olderThan">Only unload listings last scanned before this moment.</param>
    /// <returns>How many listings were dropped.</returns>
    /// <remarks>
    /// <para>
    /// The cache never forgets a directory, because forgetting one would break the single-object
    /// rule <see cref="Intern"/> rests on — the next lookup would mint a second node for the same
    /// path, and its cursor row, its marks and any measured size would quietly be someone else's.
    /// What it can do is let go of the listings, which is where the memory is: around a kilobyte
    /// an entry, against a node of a dozen small fields. A directory returned to pays for one
    /// scan and remembers everything else. See <see cref="DirectoryNode.Unload"/>.
    /// </para>
    /// <para>
    /// Ranger has the same collector and the same two conditions — old enough, and not under any
    /// tab (<c>core/fm.py:472-486</c>) — and its periodic call is commented out in its own main
    /// loop (<c>core/fm.py:531-534</c>). That is why Canger inherited an unbounded cache: the
    /// port was faithful, including the part that never runs.
    /// </para>
    /// </remarks>
    public int UnloadIdle(IReadOnlySet<DirectoryNode> keep, DateTimeOffset olderThan)
    {
        ArgumentNullException.ThrowIfNull(keep);

        int unloaded = 0;

        // Only the nodes are touched, never the dictionary, so this can iterate it directly.
        foreach (DirectoryNode directory in _directories.Values)
        {
            if (keep.Contains(directory) || directory.LastLoaded >= olderThan)
            {
                continue;
            }

            if (directory.Unload())
            {
                unloaded++;
            }
        }

        return unloaded;
    }

    /// <summary>Forgets every directory.</summary>
    public void Clear() => _directories.Clear();

    private static string Normalize(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string trimmed = full.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }
}
