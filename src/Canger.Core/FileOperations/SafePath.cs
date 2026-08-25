// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.FileSystem;

namespace Canger.Core.FileOperations;

/// <summary>
/// Finds a destination name that does not already exist.
/// </summary>
/// <remarks>
/// Copying a file into a directory that already has one of that name should not destroy it
/// silently. Renaming the incoming file is less surprising than either overwriting or refusing,
/// and it is what lets a copy of a whole directory proceed without stopping at the first clash.
/// </remarks>
public static class SafePath
{
    /// <summary>
    /// Appends underscores and then numbers until the name is free.
    /// </summary>
    /// <remarks>
    /// The first attempt is a bare underscore, so a single clash produces <c>notes_</c> rather
    /// than <c>notes_0</c>, which reads better and matches ranger.
    /// </remarks>
    /// <param name="fileSystem">Used to test what exists.</param>
    /// <param name="path">The desired path.</param>
    /// <returns>A path nothing occupies.</returns>
    public static string MakeUnique(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!fileSystem.Exists(path))
        {
            return path;
        }

        string candidate = path + "_";
        if (!fileSystem.Exists(candidate))
        {
            return candidate;
        }

        for (int index = 0; index < int.MaxValue; index++)
        {
            candidate = path + "_" + index.ToString(CultureInfo.InvariantCulture);
            if (!fileSystem.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"could not find a free name near {path}");
    }

    /// <summary>
    /// Makes a name unique while keeping the extension on the end.
    /// </summary>
    /// <remarks>
    /// <c>report_.pdf</c> rather than <c>report.pdf_</c>, so the copy still opens in the right
    /// program and still sorts with its siblings. This is what <c>:paste_ext</c> uses.
    /// </remarks>
    /// <param name="fileSystem">Used to test what exists.</param>
    /// <param name="path">The desired path.</param>
    /// <returns>A path nothing occupies.</returns>
    public static string MakeUniqueKeepingExtension(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!fileSystem.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileName(path);

        // A leading dot does not begin an extension, so a dotfile keeps its whole name.
        int dot = name.LastIndexOf('.');
        string stem = dot <= 0 ? name : name[..dot];
        string extension = dot <= 0 ? string.Empty : name[dot..];

        string candidate = Path.Join(directory, stem + "_" + extension);
        if (!fileSystem.Exists(candidate))
        {
            return candidate;
        }

        for (int index = 0; index < int.MaxValue; index++)
        {
            candidate = Path.Join(
                directory,
                stem + "_" + index.ToString(CultureInfo.InvariantCulture) + extension);

            if (!fileSystem.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"could not find a free name near {path}");
    }
}
