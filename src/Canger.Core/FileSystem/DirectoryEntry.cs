// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.FileSystem;

/// <summary>
/// One entry produced by a directory scan, carrying the metadata gathered during the scan so
/// that the model layer does not have to stat the file a second time.
/// </summary>
/// <remarks>
/// Ranger calls this a "preload" and consumes it exactly once
/// (<c>ranger/container/fsobject.py:299</c>). Canger keeps the same idea for the same reason:
/// stat is the dominant cost of listing a large directory, and doing it twice halves the
/// achievable throughput.
/// </remarks>
/// <param name="Name">The entry's name, without any directory component.</param>
/// <param name="LinkStatus">
/// Status of the entry itself, without following a final symbolic link — the <c>lstat</c> view.
/// </param>
/// <param name="TargetStatus">
/// Status of the symlink's target for symbolic links that resolve, otherwise
/// <see langword="null"/>. A symlink with a <see langword="null"/> target is broken.
/// </param>
public readonly record struct DirectoryEntry(
    string Name,
    FileStatus LinkStatus,
    FileStatus? TargetStatus)
{
    /// <summary>
    /// The status that describes what the entry behaves like: the symlink target where one
    /// resolves, otherwise the entry itself.
    /// </summary>
    public FileStatus EffectiveStatus => TargetStatus ?? LinkStatus;

    /// <summary>Whether this entry is a symbolic link, resolving or not.</summary>
    public bool IsSymbolicLink => LinkStatus.IsSymbolicLink;

    /// <summary>Whether this entry is a symbolic link whose target could not be resolved.</summary>
    public bool IsBrokenSymbolicLink => LinkStatus.IsSymbolicLink && TargetStatus is null;

    /// <summary>Whether this entry behaves as a directory, following symbolic links.</summary>
    public bool IsDirectory => EffectiveStatus.IsDirectory;
}
