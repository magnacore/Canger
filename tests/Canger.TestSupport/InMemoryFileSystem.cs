// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.FileSystem;

namespace Canger.TestSupport;

/// <summary>
/// An <see cref="IFileSystem"/> held entirely in memory.
/// </summary>
/// <remarks>
/// <para>
/// Sorting, filtering, flat mode, marks and cursor behaviour are all pure logic over a directory
/// listing, so testing them against a real disk would only add temporary directories, cleanup and
/// a dependence on the host's filesystem semantics. This double lets those tests state exactly
/// the tree they need and nothing else.
/// </para>
/// <para>
/// It models the things the domain actually distinguishes: file kinds, sizes, timestamps,
/// ownership, permissions, symbolic links including broken ones, and which device a path is on,
/// so that same-filesystem checks can be exercised.
/// </para>
/// </remarks>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private ulong _nextInode = 1;

    /// <summary>Creates a filesystem containing only the root directory.</summary>
    public InMemoryFileSystem() => AddDirectory("/");

    /// <summary>Free bytes reported by <see cref="GetDiskUsage"/>.</summary>
    public long FreeBytes { get; set; } = 500L * 1024 * 1024 * 1024;

    /// <summary>Total bytes reported by <see cref="GetDiskUsage"/>.</summary>
    public long TotalBytes { get; set; } = 1000L * 1024 * 1024 * 1024;

    /// <summary>How many times a directory has been listed, to detect redundant scans.</summary>
    public int ListCount { get; private set; }

    /// <summary>How many times entries were counted without their metadata being read.</summary>
    /// <remarks>
    /// Counted separately from <see cref="ListCount"/> so a test can assert that showing a
    /// directory's entry count does not stat every entry inside it.
    /// </remarks>
    public int CountEntriesCount { get; private set; }

    /// <summary>Adds a directory and every parent it needs.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="device">Which filesystem it sits on.</param>
    /// <param name="modified">Modification time.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public InMemoryFileSystem AddDirectory(string path, ulong device = 1,
                                           DateTimeOffset modified = default)
    {
        path = Normalize(path);

        if (path != "/")
        {
            AddDirectory(ParentOf(path), device, modified);
        }

        _nodes[path] = new Node(FileKind.Directory, [], null, device, modified,
                                Mode: 0x41ED /* 040755 octal */, Inode: _nextInode++);
        return this;
    }

    /// <summary>Adds a regular file, creating any parent directories.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="content">File content.</param>
    /// <param name="device">Which filesystem it sits on.</param>
    /// <param name="modified">Modification time.</param>
    /// <param name="mode">Raw mode bits, defaulting to a regular file with 0644.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public InMemoryFileSystem AddFile(string path, string content = "", ulong device = 1,
                                      DateTimeOffset modified = default, uint mode = 0x81A4)
    {
        path = Normalize(path);
        AddDirectory(ParentOf(path), device, modified);
        _nodes[path] = new Node(FileKind.Regular, Encoding.UTF8.GetBytes(content), null, device,
                                modified, mode, _nextInode++);
        return this;
    }

    /// <summary>Adds a file of a given size without allocating its content.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="size">Apparent size in bytes.</param>
    /// <param name="modified">Modification time.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public InMemoryFileSystem AddFileOfSize(string path, long size, DateTimeOffset modified = default)
    {
        path = Normalize(path);
        AddDirectory(ParentOf(path), 1, modified);
        _nodes[path] = new Node(FileKind.Regular, [], null, 1, modified, 0x81A4, _nextInode++)
        {
            ApparentSize = size,
        };
        return this;
    }

    /// <summary>
    /// Adds a symbolic link. A target that does not exist makes the link broken, which is a case
    /// the model has to render differently.
    /// </summary>
    /// <param name="path">Absolute path of the link.</param>
    /// <param name="target">The target to record in the link.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public InMemoryFileSystem AddSymbolicLink(string path, string target)
    {
        path = Normalize(path);
        AddDirectory(ParentOf(path));
        _nodes[path] = new Node(FileKind.SymbolicLink, [], target, 1, default, 0xA1FF,
                                _nextInode++);
        return this;
    }

    /// <summary>Adds a device node, socket or FIFO.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="kind">Which kind of special file.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public InMemoryFileSystem AddSpecial(string path, FileKind kind)
    {
        path = Normalize(path);
        AddDirectory(ParentOf(path));
        uint mode = kind switch
        {
            FileKind.Fifo => 0x1000,
            FileKind.Socket => 0xC000,
            FileKind.CharacterDevice => 0x2000,
            FileKind.BlockDevice => 0x6000,
            _ => 0x8000,
        };
        _nodes[path] = new Node(kind, [], null, 1, default, mode, _nextInode++);
        return this;
    }

    /// <summary>Makes a path unreadable, so listing or statting it fails.</summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public InMemoryFileSystem MakeInaccessible(string path)
    {
        path = Normalize(path);
        if (_nodes.TryGetValue(path, out Node? node))
        {
            _nodes[path] = node with { IsAccessible = false };
        }

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<Canger.Core.FileSystem.DirectoryEntry> ListDirectory(
        string path, CancellationToken cancellationToken = default)
    {
        path = Normalize(path);
        ListCount++;

        // A link to a directory lists what it points at, because `opendir(3)` follows links.
        // Without this the double could not represent a linked folder at all: listing one threw,
        // so its entry count came back empty and any test about one quietly measured nothing.
        path = FollowLinks(path);

        if (!_nodes.TryGetValue(path, out Node? directory) || directory.Kind != FileKind.Directory)
        {
            throw new DirectoryNotFoundException($"No such directory: {path}");
        }

        if (!directory.IsAccessible)
        {
            throw new UnauthorizedAccessException($"Permission denied: {path}");
        }

        string prefix = path == "/" ? "/" : path + "/";
        List<Canger.Core.FileSystem.DirectoryEntry> entries = [];

        foreach ((string childPath, Node child) in _nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Direct children only: the remainder after the prefix must contain no separator.
            if (!childPath.StartsWith(prefix, StringComparison.Ordinal) || childPath == path)
            {
                continue;
            }

            string name = childPath[prefix.Length..];
            if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            FileStatus linkStatus = StatusOf(child);
            FileStatus? targetStatus = child.Kind == FileKind.SymbolicLink
                ? Resolve(child.LinkTarget!) is { } resolved ? StatusOf(resolved) : null
                : null;

            entries.Add(new Canger.Core.FileSystem.DirectoryEntry(name, linkStatus, targetStatus));
        }

        return entries;
    }

    /// <summary>
    /// Changes a path's modification time, as an outside program would.
    /// </summary>
    /// <param name="path">The path to touch.</param>
    /// <param name="modified">The new modification time.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    /// <remarks>
    /// A directory's own mtime is how a listing notices a change it did not make itself, so a test
    /// for that needs to be able to move it the way <c>cp</c> or another pane would.
    /// </remarks>
    public InMemoryFileSystem Touch(string path, DateTimeOffset modified)
    {
        path = Normalize(path);

        if (_nodes.TryGetValue(path, out Node? node))
        {
            _nodes[path] = node with { Modified = modified };
        }

        return this;
    }

    /// <inheritdoc />
    public int? CountEntries(string path)
    {
        CountEntriesCount++;

        // Following links here for the same reason ListDirectory does: counting a linked folder
        // counts what it points at, and answering null instead made a linked folder show no
        // count at all.
        path = FollowLinks(Normalize(path));

        if (!_nodes.TryGetValue(path, out Node? directory) || directory.Kind != FileKind.Directory
            || !directory.IsAccessible)
        {
            return null;
        }

        string prefix = path == "/" ? "/" : path + "/";
        int count = 0;

        foreach (string candidate in _nodes.Keys)
        {
            if (candidate.Length > prefix.Length && candidate.StartsWith(prefix, StringComparison.Ordinal)
                && candidate.IndexOf('/', prefix.Length) < 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <inheritdoc />
    public FileStatus? GetStatus(string path, bool followSymbolicLinks = false)
    {
        path = Normalize(path);
        if (!_nodes.TryGetValue(path, out Node? node) || !node.IsAccessible)
        {
            return null;
        }

        if (followSymbolicLinks && node.Kind == FileKind.SymbolicLink)
        {
            Node? target = Resolve(node.LinkTarget!);
            return target is null ? null : StatusOf(target);
        }

        return StatusOf(node);
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        FileStatus? status = GetStatus(path, followSymbolicLinks: true);
        return status is { IsDirectory: true };
    }

    /// <inheritdoc />
    public bool Exists(string path) => _nodes.ContainsKey(Normalize(path));

    /// <inheritdoc />
    public string? ReadSymbolicLinkTarget(string path) =>
        _nodes.TryGetValue(Normalize(path), out Node? node) &&
        node.Kind == FileKind.SymbolicLink
            ? node.LinkTarget
            : null;

    /// <inheritdoc />
    public string ResolvePath(string path)
    {
        path = Normalize(path);
        return _nodes.TryGetValue(path, out Node? node) && node.Kind == FileKind.SymbolicLink
            ? Normalize(node.LinkTarget!)
            : path;
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        path = Normalize(path);
        return _nodes.TryGetValue(path, out Node? node) && node.IsAccessible
            ? new MemoryStream(node.Content, writable: false)
            : throw new FileNotFoundException($"No such file: {path}", path);
    }

    /// <summary>Paths whose writes fail, and what to say when they do.</summary>
    private readonly Dictionary<string, string> _writeFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes writes to a path fail, the way a real filesystem refuses one.
    /// </summary>
    /// <param name="path">The destination that cannot be written.</param>
    /// <param name="because">
    /// Why, in the words the filesystem would use — "No space left on device", "File name too
    /// long", "Invalid argument" for a character exFAT will not take, "File too large" past
    /// FAT32's four gigabytes. The reason does not change the behaviour; it makes the test say
    /// which real situation it stands for.
    /// </param>
    /// <returns>This filesystem, so setup can be chained.</returns>
    public InMemoryFileSystem FailWritesTo(string path, string because = "No space left on device")
    {
        _writeFailures[Normalize(path)] = because;
        return this;
    }

    /// <inheritdoc />
    public Stream OpenWrite(string path)
    {
        path = Normalize(path);

        if (_writeFailures.TryGetValue(path, out string? because))
        {
            throw new IOException(because);
        }

        AddDirectory(ParentOf(path));
        return new WritebackStream(this, path);
    }

    /// <summary>Writes back into the store when the caller is finished with it.</summary>
    private sealed class WritebackStream(InMemoryFileSystem fileSystem, string path) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fileSystem._nodes[path] = new Node(
                    FileKind.Regular, ToArray(), null, 1, default, 0x81A4,
                    fileSystem._nextInode++);
            }

            base.Dispose(disposing);
        }
    }

    /// <inheritdoc />
    public byte[] ReadFilePrefix(string path, int maximumBytes)
    {
        path = Normalize(path);
        if (!_nodes.TryGetValue(path, out Node? node) || !node.IsAccessible)
        {
            return [];
        }

        return node.Content.Length <= maximumBytes ? node.Content : node.Content[..maximumBytes];
    }

    /// <inheritdoc />
    public void CreateDirectory(string path) => AddDirectory(path);

    /// <inheritdoc />
    public void Touch(string path)
    {
        path = Normalize(path);
        if (!_nodes.ContainsKey(path))
        {
            AddFile(path);
        }
    }

    /// <inheritdoc />
    public void Delete(string path)
    {
        Refuse(path);
        _nodes.Remove(Normalize(path));
    }

    private readonly HashSet<string> _undeletable = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes a path refuse to be deleted, as a read-only mount or a lack of permission would.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>This filesystem, for chaining.</returns>
    /// <remarks>
    /// Deletion here otherwise always succeeds, so a test about what happens when it does not
    /// could not be written at all — it would quietly assert the successful case instead.
    /// </remarks>
    public InMemoryFileSystem FailToDelete(string path)
    {
        _undeletable.Add(Normalize(path));
        return this;
    }

    /// <summary>Throws if the path has been marked undeletable.</summary>
    private void Refuse(string path)
    {
        if (_undeletable.Contains(Normalize(path)))
        {
            throw new IOException($"Permission denied: {path}");
        }
    }

    /// <inheritdoc />
    public void DeleteRecursive(string path)
    {
        Refuse(path);

        path = Normalize(path);
        string prefix = path + "/";

        foreach (string key in _nodes.Keys
                     .Where(k => k == path || k.StartsWith(prefix, StringComparison.Ordinal))
                     .ToList())
        {
            _nodes.Remove(key);
        }
    }

    /// <inheritdoc />
    public void Rename(string sourcePath, string destinationPath)
    {
        sourcePath = Normalize(sourcePath);
        destinationPath = Normalize(destinationPath);

        // A destination the filesystem will not accept refuses a rename onto it just as it
        // refuses a write to it — an illegal character, a full disk, a name too long. Without
        // this the fake would quietly rename where a real filesystem would fail, and a test that
        // means to exercise a failed move would exercise a successful one.
        if (_writeFailures.TryGetValue(destinationPath, out string? because))
        {
            throw new IOException(because);
        }

        foreach (string key in _nodes.Keys
                     .Where(k => k == sourcePath ||
                                 k.StartsWith(sourcePath + "/", StringComparison.Ordinal))
                     .ToList())
        {
            Node node = _nodes[key];
            _nodes.Remove(key);
            _nodes[destinationPath + key[sourcePath.Length..]] = node;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The store holds a link as a node in its own right, so a name is taken whether or not what
    /// it points at exists — which is the distinction this asks about.
    /// </remarks>
    public bool ExistsNoFollow(string path) => _nodes.ContainsKey(Normalize(path));

    /// <inheritdoc />
    public void Replace(string sourcePath, string destinationPath)
    {
        _nodes.Remove(Normalize(destinationPath));
        Rename(sourcePath, destinationPath);
    }

    /// <inheritdoc />
    public void CreateSymbolicLink(string linkPath, string target) =>
        AddSymbolicLink(linkPath, target);

    /// <inheritdoc />
    public void CreateHardLink(string linkPath, string existingPath)
    {
        existingPath = Normalize(existingPath);
        if (_nodes.TryGetValue(existingPath, out Node? node))
        {
            _nodes[Normalize(linkPath)] = node;
        }
    }

    /// <inheritdoc />
    public (long FreeBytes, long TotalBytes)? GetDiskUsage(string path) => (FreeBytes, TotalBytes);

    /// <summary>
    /// Follows a chain of links to whatever it ends at.
    /// </summary>
    /// <param name="path">The path to follow.</param>
    /// <returns>The path of the final target, or the path itself when it is not a link.</returns>
    /// <remarks>
    /// Bounded, so a link pointing at itself is a wrong answer rather than a hung test. The real
    /// kernel gives up after forty and reports ELOOP.
    /// </remarks>
    private string FollowLinks(string path)
    {
        for (int hop = 0; hop < 40; hop++)
        {
            if (!_nodes.TryGetValue(path, out Node? node) || node.Kind != FileKind.SymbolicLink)
            {
                return path;
            }

            path = Normalize(node.LinkTarget!);
        }

        return path;
    }

    private Node? Resolve(string target) =>
        _nodes.TryGetValue(Normalize(target), out Node? node) ? node : null;

    private static FileStatus StatusOf(Node node) => new(
        node.Kind,
        node.Mode,
        node.ApparentSize ?? node.Content.Length,
        HardLinkCount: 1,
        Uid: 1000,
        Gid: 1000,
        node.Inode,
        node.Device,
        node.Modified,
        node.Modified,
        node.Modified);

    /// <summary>Collapses a path to the canonical form the store is keyed by.</summary>
    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    private sealed record Node(
        FileKind Kind,
        byte[] Content,
        string? LinkTarget,
        ulong Device,
        DateTimeOffset Modified,
        uint Mode,
        ulong Inode)
    {
        internal bool IsAccessible { get; init; } = true;

        internal long? ApparentSize { get; init; }
    }
}
