// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.FileSystem;

/// <summary>
/// The filesystem operations the Canger domain model depends on.
/// </summary>
/// <remarks>
/// <para>
/// Every part of the model reaches the disk through this interface rather than through
/// <see cref="System.IO"/> directly. That keeps the domain testable: sorting, filtering, flat
/// mode and cursor behaviour can all be exercised against an in-memory tree with no temporary
/// directories, no cleanup, and no dependence on the host's filesystem semantics.
/// </para>
/// <para>
/// Implementations must be safe to call from multiple threads at once, because directory
/// scanning and preview generation run in parallel.
/// </para>
/// </remarks>
public interface IFileSystem
{
    /// <summary>
    /// Lists the entries of a directory, gathering each entry's metadata as it goes.
    /// </summary>
    /// <param name="path">Absolute path of the directory to scan.</param>
    /// <param name="cancellationToken">Cancels a scan of a large or slow directory.</param>
    /// <returns>
    /// The entries, in whatever order the filesystem reports them. Callers are responsible for
    /// sorting. Entries whose metadata could not be read are still returned, with
    /// <see cref="FileKind.Unknown"/>, so that unreadable files remain visible to the user.
    /// </returns>
    /// <exception cref="IOException">The directory itself could not be opened.</exception>
    IReadOnlyList<DirectoryEntry> ListDirectory(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the entries of a directory without reading any of their metadata.
    /// </summary>
    /// <param name="path">Absolute path of the directory to count.</param>
    /// <returns>The number of entries, or <see langword="null"/> when it could not be read.</returns>
    /// <remarks>
    /// <para>
    /// <c>automatically_count_files</c> puts an entry count beside every visible directory, and
    /// the count is the only thing it wants. Getting it from <see cref="ListDirectory"/> meant one
    /// <c>statx</c> per entry of every one of those directories, and then discarding all of it —
    /// measured at 70ms of a 420ms settle on a 20,546-entry listing, for forty integers.
    /// </para>
    /// <para>
    /// Separate from <see cref="ListDirectory"/> rather than a flag on it, because the walkers
    /// that genuinely need every entry's metadata — the cumulative-size job and the copy
    /// engine — must keep getting it.
    /// </para>
    /// </remarks>
    int? CountEntries(string path);

    /// <summary>
    /// Reads the metadata of a single path.
    /// </summary>
    /// <param name="path">Absolute path to query.</param>
    /// <param name="followSymbolicLinks">
    /// When <see langword="true"/>, reports the target of a symbolic link; when
    /// <see langword="false"/>, reports the link itself.
    /// </param>
    /// <returns>The status, or <see langword="null"/> when the path does not exist or is unreadable.</returns>
    FileStatus? GetStatus(string path, bool followSymbolicLinks = false);

    /// <summary>Whether a path exists as a directory, following symbolic links.</summary>
    /// <param name="path">Absolute path to test.</param>
    /// <returns><see langword="true"/> when the path resolves to a directory.</returns>
    bool DirectoryExists(string path);

    /// <summary>Whether a path exists at all, without following a final symbolic link.</summary>
    /// <param name="path">Absolute path to test.</param>
    /// <returns><see langword="true"/> when something exists at the path.</returns>
    bool Exists(string path);

    /// <summary>Reads the raw target of a symbolic link, without resolving it.</summary>
    /// <param name="path">Absolute path of the link.</param>
    /// <returns>The link target as stored, or <see langword="null"/> when not a link.</returns>
    string? ReadSymbolicLinkTarget(string path);

    /// <summary>Resolves a path to its canonical form, following every symbolic link.</summary>
    /// <param name="path">Absolute path to resolve.</param>
    /// <returns>The resolved path, or <paramref name="path"/> when it cannot be resolved.</returns>
    string ResolvePath(string path);

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <returns>A readable stream the caller owns and must dispose.</returns>
    /// <exception cref="IOException">The file could not be opened.</exception>
    Stream OpenRead(string path);

    /// <summary>
    /// Opens a file for writing, replacing anything already there.
    /// </summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <returns>A writable stream the caller owns and must dispose.</returns>
    /// <exception cref="IOException">The file could not be opened.</exception>
    Stream OpenWrite(string path);

    /// <summary>
    /// Reads up to <paramref name="maximumBytes"/> from the start of a file.
    /// </summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <param name="maximumBytes">Upper bound on how much to read.</param>
    /// <returns>
    /// The bytes read, which may be shorter than requested, or an empty span when the file
    /// could not be read. Used for binary sniffing and short previews.
    /// </returns>
    byte[] ReadFilePrefix(string path, int maximumBytes);

    /// <summary>Creates a directory and any missing parents.</summary>
    /// <param name="path">Absolute path of the directory to create.</param>
    void CreateDirectory(string path);

    /// <summary>
    /// Creates an empty file, or updates the modification time of an existing one.
    /// </summary>
    /// <param name="path">Absolute path of the file.</param>
    void Touch(string path);

    /// <summary>Deletes a file, symbolic link or empty directory.</summary>
    /// <param name="path">Absolute path to delete.</param>
    void Delete(string path);

    /// <summary>Deletes a directory and everything beneath it.</summary>
    /// <param name="path">Absolute path of the directory tree to delete.</param>
    void DeleteRecursive(string path);

    /// <summary>
    /// Renames a path. Succeeds only within one filesystem; callers fall back to copy-then-delete
    /// across filesystems.
    /// </summary>
    /// <param name="sourcePath">Existing absolute path.</param>
    /// <param name="destinationPath">Absolute path to move it to.</param>
    void Rename(string sourcePath, string destinationPath);

    /// <summary>
    /// Moves a file over another, replacing it.
    /// </summary>
    /// <param name="sourcePath">The file to move.</param>
    /// <param name="destinationPath">What it replaces.</param>
    /// <remarks>
    /// Deliberately separate from <see cref="Rename"/>, which refuses an existing destination and
    /// is what stops <c>:rename</c> and <c>:bulkrename</c> destroying a file. This one exists for
    /// the last step of a write-beside-then-replace, where overwriting is the entire point and
    /// the thing being replaced is a file Canger wrote a moment ago. Naming them apart is what
    /// keeps a future caller from reaching for the destructive one by accident.
    /// </remarks>
    void Replace(string sourcePath, string destinationPath);

    /// <summary>Creates a symbolic link.</summary>
    /// <param name="linkPath">Absolute path of the link to create.</param>
    /// <param name="target">The target to record in the link.</param>
    void CreateSymbolicLink(string linkPath, string target);

    /// <summary>Creates a hard link.</summary>
    /// <param name="linkPath">Absolute path of the link to create.</param>
    /// <param name="existingPath">Absolute path of the existing file.</param>
    void CreateHardLink(string linkPath, string existingPath);

    /// <summary>
    /// Reports free and total space for the filesystem containing a path, for the status bar.
    /// </summary>
    /// <param name="path">Any absolute path on the filesystem of interest.</param>
    /// <returns>
    /// Free and total bytes, or <see langword="null"/> when the information is unavailable.
    /// </returns>
    (long FreeBytes, long TotalBytes)? GetDiskUsage(string path);
}
