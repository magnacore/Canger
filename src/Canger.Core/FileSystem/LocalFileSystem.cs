// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Canger.Core.Native;

namespace Canger.Core.FileSystem;

/// <summary>
/// The real, on-disk implementation of <see cref="IFileSystem"/> for Linux.
/// </summary>
/// <remarks>
/// Metadata is read through <c>statx(2)</c> rather than <see cref="FileInfo"/> because Canger
/// needs owner, group, inode, link count and containing device, none of which the base class
/// library exposes. Everything else uses <see cref="System.IO"/>, which is both simpler and
/// better tested than a hand-rolled equivalent.
/// </remarks>
public sealed class LocalFileSystem : IFileSystem
{
    /// <summary>A shared instance. The type holds no mutable state, so one is enough.</summary>
    public static readonly LocalFileSystem Instance = new();

    /// <inheritdoc />
    public IReadOnlyList<DirectoryEntry> ListDirectory(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // EnumerateFileSystemEntries surfaces names cheaply; the per-entry stat below is the
        // expensive part, and is what a caller parallelises when a directory is large.
        var entries = new List<DirectoryEntry>();
        foreach (string entryPath in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(Describe(entryPath, Path.GetFileName(entryPath)));
        }

        return entries;
    }

    /// <inheritdoc />
    public int? CountEntries(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            // Enumeration alone, with no per-entry stat: `getdents` is one syscall for many
            // entries, where statting each of them is one syscall each. On a 20,546-entry
            // directory that is the difference between 7ms and 59ms, and the caller only wants
            // the total.
            int count = 0;

            foreach (string _ in Directory.EnumerateFileSystemEntries(path))
            {
                count++;
            }

            return count;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gathers the link status and, for symbolic links, the target status of one entry.
    /// </summary>
    private static DirectoryEntry Describe(string fullPath, string name)
    {
        FileStatus? linkStatus = Stat(fullPath, followSymbolicLinks: false);
        if (linkStatus is null)
        {
            // The entry exists — enumeration just returned it — but is unreadable. Surface it
            // anyway so the user can see that something is there.
            return new DirectoryEntry(name, UnknownStatus, TargetStatus: null);
        }

        FileStatus? targetStatus = linkStatus.Value.IsSymbolicLink
            ? Stat(fullPath, followSymbolicLinks: true)
            : null;

        return new DirectoryEntry(name, linkStatus.Value, targetStatus);
    }

    /// <summary>A placeholder for entries whose metadata could not be read.</summary>
    private static FileStatus UnknownStatus => new(
        FileKind.Unknown, Mode: 0, Size: 0, HardLinkCount: 0, Uid: 0, Gid: 0, Inode: 0, Device: 0,
        AccessTime: default, ModifyTime: default, ChangeTime: default);

    /// <inheritdoc />
    public FileStatus? GetStatus(string path, bool followSymbolicLinks = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Stat(path, followSymbolicLinks);
    }

    /// <summary>Calls <c>statx</c> and decodes the result.</summary>
    private static FileStatus? Stat(string path, bool followSymbolicLinks)
    {
        int flags = NativeConstants.AtNoAutomount;
        if (!followSymbolicLinks)
        {
            flags |= NativeConstants.AtSymlinkNoFollow;
        }

        int result;
        do
        {
            result = Libc.Statx(
                NativeConstants.AtFdCwd, path, flags, NativeConstants.StatxBasicStats,
                out StatxBuffer buffer);

            if (result == 0)
            {
                return FileStatus.FromStatx(buffer);
            }
        }
        while (Marshal.GetLastPInvokeError() == NativeConstants.Eintr);

        return null;
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool Exists(string path) => Path.Exists(path);

    /// <inheritdoc />
    public string? ReadSymbolicLinkTarget(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            // ResolveLinkTarget returns null when the path is not a link, in which case the
            // path is already canonical enough for our purposes.
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
                   ?? Path.GetFullPath(path);
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }

    /// <inheritdoc />
    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    /// <inheritdoc />
    public Stream OpenWrite(string path) =>
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

    /// <inheritdoc />
    public byte[] ReadFilePrefix(string path, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[maximumBytes];
            int read = stream.ReadAtLeast(buffer, maximumBytes, throwOnEndOfStream: false);
            return read == maximumBytes ? buffer : buffer[..read];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void Touch(string path)
    {
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return;
        }

        using FileStream _ = new(path, FileMode.CreateNew, FileAccess.Write);
    }

    /// <inheritdoc />
    public void Delete(string path)
    {
        // A symbolic link to a directory must be removed as a link, not followed and recursed
        // into, so the link status is what decides here.
        FileStatus? status = Stat(path, followSymbolicLinks: false);
        if (status is { IsDirectory: true })
        {
            Directory.Delete(path, recursive: false);
        }
        else
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public void DeleteRecursive(string path) => Directory.Delete(path, recursive: true);

    /// <inheritdoc />
    public void Rename(string sourcePath, string destinationPath)
    {
        FileStatus? status = Stat(sourcePath, followSymbolicLinks: false);
        if (status is { IsDirectory: true })
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath, overwrite: false);
        }
    }

    /// <inheritdoc />
    public bool ExistsNoFollow(string path) =>
        Stat(path, followSymbolicLinks: false) is not null;

    /// <inheritdoc />
    public void Replace(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: true);

    /// <inheritdoc />
    public void CreateSymbolicLink(string linkPath, string target) =>
        File.CreateSymbolicLink(linkPath, target);

    /// <inheritdoc />
    public void CreateHardLink(string linkPath, string existingPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(linkPath);
        ArgumentException.ThrowIfNullOrEmpty(existingPath);

        if (Libc.Link(existingPath, linkPath) != 0)
        {
            throw new IOException(
                $"Could not hard link '{linkPath}' to '{existingPath}'.",
                Marshal.GetLastPInvokeError());
        }
    }

    /// <inheritdoc />
    public (long FreeBytes, long TotalBytes)? GetDiskUsage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // statvfs reports on the filesystem containing the given path. DriveInfo would report on
        // a mount point, which for a nested mount is the wrong filesystem entirely.
        if (Libc.Statvfs(path, out StatvfsBuffer buffer) != 0)
        {
            return null;
        }

        ulong unit = buffer.FragmentSize != 0 ? buffer.FragmentSize : buffer.BlockSize;
        return ((long)(buffer.AvailableBlocks * unit), (long)(buffer.TotalBlocks * unit));
    }
}
