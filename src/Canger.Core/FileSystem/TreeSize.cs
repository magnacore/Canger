// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.FileSystem;

/// <summary>
/// Adds up what a tree holds, a file at a time.
/// </summary>
/// <remarks>
/// <para>
/// Yields one size per file so the caller can hand control back between them. That is what lets
/// the task queue measure a directory in three-millisecond slices while something else is running,
/// instead of stopping the interface for as long as the walk takes — a directory may hold a
/// million files, and the only way to know its size is to look at all of them.
/// </para>
/// <para>
/// Only metadata is read, so a measurement can run alongside a transfer without the two competing
/// for the disk. A symbolic link counts as its own small size rather than as whatever it points
/// at, which is what both copying and archiving do with one.
/// </para>
/// <para>
/// Grown out of <c>CopyJob</c>, which had it privately and is still its first caller. Compressing
/// needs the same answer for the same reason, and two walks that could disagree about what a tree
/// holds would be worse than one.
/// </para>
/// </remarks>
public static class TreeSize
{
    /// <summary>Every file's size beneath a path, including the path itself when it is a file.</summary>
    /// <param name="fileSystem">Where to look.</param>
    /// <param name="path">The file or directory to measure.</param>
    /// <param name="cancellationToken">Stops a walk that is no longer wanted.</param>
    /// <returns>The sizes, one at a time.</returns>
    /// <remarks>
    /// A path that cannot be read contributes nothing rather than throwing: a directory the user
    /// cannot enter is a gap in the total, and refusing to measure anything because of one is
    /// worse than measuring the rest.
    /// </remarks>
    public static IEnumerable<long> Sizes(IFileSystem fileSystem, string path,
                                          CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(path);

        cancellationToken.ThrowIfCancellationRequested();

        FileStatus? status = fileSystem.GetStatus(path, followSymbolicLinks: false);

        if (status is null)
        {
            yield break;
        }

        if (!status.Value.IsDirectory || status.Value.IsSymbolicLink)
        {
            yield return status.Value.Size;
            yield break;
        }

        IReadOnlyList<DirectoryEntry> entries;

        try
        {
            entries = fileSystem.ListDirectory(path, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (DirectoryEntry entry in entries)
        {
            foreach (long measured in Sizes(fileSystem, Join(path, entry.Name), cancellationToken))
            {
                yield return measured;
            }
        }
    }

    /// <summary>Joins a directory and a name without doubling the separator at the root.</summary>
    private static string Join(string directory, string name) =>
        directory == "/" ? "/" + name : directory + "/" + name;
}
