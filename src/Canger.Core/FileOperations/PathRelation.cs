// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.FileOperations;

/// <summary>
/// Whether one path lies within another.
/// </summary>
/// <remarks>
/// The question every transfer has to ask before it starts: putting a directory inside itself
/// walks a tree that is growing as it is read, and for a move it renames the source beneath a
/// path that is about to stop existing. Ranger asks it too, as <c>shutil</c>'s <c>_destinsrc</c>.
///
/// It lived inside <see cref="CopyJob"/>, so the three linking pastes — which recurse just as
/// happily — had no guard at all. Shared here so there is one answer rather than one per caller.
/// </remarks>
public static class PathRelation
{
    /// <summary>Whether a destination is the source itself, or somewhere beneath it.</summary>
    /// <param name="fileSystem">Used to resolve symbolic links.</param>
    /// <param name="destination">Where something is being put.</param>
    /// <param name="source">What is being put there.</param>
    /// <returns><see langword="true"/> when the transfer would consume its own output.</returns>
    /// <remarks>
    /// Links are resolved on both sides first. Comparing the paths as typed misses the case where
    /// the destination reaches the source by another name — <c>~/work</c> pointing at
    /// <c>/mnt/data/work</c> is an ordinary arrangement, and the string comparison sees two
    /// unrelated paths.
    /// </remarks>
    public static bool IsSameOrInside(IFileSystem fileSystem, string destination, string source)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentException.ThrowIfNullOrEmpty(source);

        string to = fileSystem.ResolvePath(destination);
        string from = fileSystem.ResolvePath(source);

        return string.Equals(to, from, StringComparison.Ordinal) || IsInside(to, from);
    }

    /// <summary>Whether one resolved path lies strictly within another.</summary>
    /// <param name="candidate">The path that might be inside.</param>
    /// <param name="directory">The directory it might be inside of.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    /// <remarks>
    /// The separator matters: without it <c>/a/bc</c> would count as inside <c>/a/b</c>.
    /// </remarks>
    public static bool IsInside(string candidate, string directory)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }
}
