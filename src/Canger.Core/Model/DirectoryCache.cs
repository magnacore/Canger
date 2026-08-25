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
    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly Dictionary<string, DirectoryNode> _directories = new(StringComparer.Ordinal);

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
            return existing;
        }

        DirectoryNode directory = new(
            _fileSystem, key, _fileSystem.GetStatus(key, followSymbolicLinks: true),
            _fileSystem.GetStatus(key), relativeToPath: null, cache: this);

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

            return existing;
        }

        DirectoryNode directory = new(_fileSystem, key, status, linkStatus, relativeToPath, this);
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

    /// <summary>
    /// Forgets directories that nothing is using, to bound how much a long session accumulates.
    /// </summary>
    /// <param name="keep">Paths that must be retained, such as those open in a tab.</param>
    /// <param name="olderThan">Only drop directories last scanned before this time.</param>
    /// <returns>How many were dropped.</returns>
    public int Trim(IReadOnlySet<string> keep, DateTimeOffset olderThan)
    {
        ArgumentNullException.ThrowIfNull(keep);

        List<string> stale =
        [
            .. _directories
                .Where(entry => !keep.Contains(entry.Key) && entry.Value.LastLoaded < olderThan)
                .Select(entry => entry.Key),
        ];

        foreach (string path in stale)
        {
            _directories.Remove(path);
        }

        return stale.Count;
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
