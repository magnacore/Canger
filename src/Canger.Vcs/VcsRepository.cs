// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Vcs;

/// <summary>
/// A repository, and the last thing it was known to look like.
/// </summary>
/// <remarks>
/// Ranger keeps this state on an object whose class it swaps at runtime. Here the state is the
/// object and the backend is a strategy it holds, which means a repository can be handed between
/// threads and inspected without wondering what type it currently is.
/// </remarks>
/// <param name="root">The repository's top directory.</param>
/// <param name="backend">Which system it belongs to.</param>
public sealed class VcsRepository(string root, IVcsBackend backend)
{
    private readonly Lock _gate = new();
    private Dictionary<string, VcsStatus> _subpaths = new(StringComparer.Ordinal);

    /// <summary>The repository's top directory.</summary>
    public string Root { get; } = root;

    /// <summary>Which system it belongs to.</summary>
    public IVcsBackend Backend { get; } = backend;

    /// <summary>The status of the repository as a whole.</summary>
    public VcsStatus Status { get; private set; } = VcsStatus.Unknown;

    /// <summary>How it stands relative to the remote it tracks.</summary>
    public VcsRemoteStatus RemoteStatus { get; private set; } = VcsRemoteStatus.None;

    /// <summary>The current branch, where the backend has such a notion.</summary>
    public string? Branch { get; private set; }

    /// <summary>The most recent revision.</summary>
    public VcsCommit? Head { get; private set; }

    /// <summary>When it was last read.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>What went wrong the last time it was read, if anything.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether anything has been read yet.</summary>
    public bool IsLoaded => UpdatedAt is not null;

    /// <summary>
    /// Re-reads everything from the repository.
    /// </summary>
    /// <returns><see langword="true"/> when it was read successfully.</returns>
    /// <remarks>
    /// Called from a background thread. Each answer is gathered before anything is published, so
    /// a caller never sees a branch from one refresh beside a status from another.
    /// </remarks>
    public bool Refresh()
    {
        try
        {
            IReadOnlyDictionary<string, VcsStatus> subpaths = Backend.SubpathStatuses(Root);
            VcsRemoteStatus remote = Backend.RemoteStatus(Root);
            string? branch = Backend.Branch(Root);
            VcsCommit? head = Backend.Head(Root);

            lock (_gate)
            {
                _subpaths = new Dictionary<string, VcsStatus>(subpaths, StringComparer.Ordinal);
                Status = VcsStatuses.Combine(subpaths.Values);
                RemoteStatus = remote;
                Branch = branch;
                Head = head;
                Error = null;
                UpdatedAt = DateTimeOffset.UtcNow;
            }

            return true;
        }
        catch (VcsException e)
        {
            lock (_gate)
            {
                Status = VcsStatus.Unknown;
                Error = e.Message;
                UpdatedAt = DateTimeOffset.UtcNow;
            }

            return false;
        }
    }

    /// <summary>
    /// The status of one path inside the repository.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <param name="isDirectory">Whether it is a directory.</param>
    /// <returns>Its status.</returns>
    /// <remarks>
    /// A directory takes the most important status among everything under it, which is what makes
    /// a collapsed tree still show that something inside needs attention. A file not mentioned at
    /// all is in sync — only what differs is recorded.
    /// </remarks>
    public VcsStatus StatusOf(string path, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string relative = Relative(path);

        if (relative.Length == 0)
        {
            return Status;
        }

        lock (_gate)
        {
            if (_subpaths.TryGetValue(relative, out VcsStatus status))
            {
                return status;
            }

            if (!isDirectory)
            {
                return VcsStatus.Sync;
            }

            // Nothing recorded for the directory itself, so it takes whatever is beneath it.
            string prefix = relative + '/';
            List<VcsStatus> inside = [];

            foreach ((string candidate, VcsStatus beneath) in _subpaths)
            {
                if (candidate.StartsWith(prefix, StringComparison.Ordinal))
                {
                    inside.Add(beneath);
                }
            }

            return inside.Count == 0 ? VcsStatus.Sync : VcsStatuses.Combine(inside);
        }
    }

    /// <summary>Turns an absolute path into one relative to the root.</summary>
    private string Relative(string path)
    {
        if (string.Equals(path, Root, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string prefix = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.Ordinal)
            ? path[prefix.Length..]
            : string.Empty;
    }
}
