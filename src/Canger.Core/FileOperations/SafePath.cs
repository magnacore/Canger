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
    /// Appends <c>_0</c>, <c>_1</c> and so on until the name is free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ranger tries a bare underscore first and only then starts counting
    /// (<c>ext/safe_path.py:13-21</c>), so four copies of <c>notes.md</c> come out as
    /// <c>notes.md_</c>, <c>notes.md_0</c>, <c>notes.md_1</c>, <c>notes.md_2</c>. The first one
    /// reads well on its own and the rest do not: three of the four are numbered, the odd one
    /// out is the oldest, and nothing about the set says which came first.
    /// </para>
    /// <para>
    /// Counting from the start instead makes the rule sayable in one sentence and the copies
    /// sortable by the number that names them. A deliberate divergence, and the only one in
    /// this file beyond keeping extensions.
    /// </para>
    /// </remarks>
    /// <param name="fileSystem">Used to test what exists.</param>
    /// <param name="path">The desired path.</param>
    /// <returns>A path nothing occupies.</returns>
    public static string MakeUnique(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!fileSystem.ExistsNoFollow(path))
        {
            return path;
        }

        for (int index = 0; index < int.MaxValue; index++)
        {
            string candidate = path + "_" + index.ToString(CultureInfo.InvariantCulture);
            if (!fileSystem.ExistsNoFollow(candidate))
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
    /// <c>report_0.pdf</c> rather than <c>report.pdf_</c>, so the copy still opens in the right
    /// program and still sorts with its siblings. This is what <c>:paste_ext</c> uses, and it
    /// counts from zero for the reason given on <see cref="MakeUnique"/>.
    /// </remarks>
    /// <param name="fileSystem">Used to test what exists.</param>
    /// <param name="path">The desired path.</param>
    /// <returns>A path nothing occupies.</returns>
    public static string MakeUniqueKeepingExtension(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!fileSystem.ExistsNoFollow(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileName(path);

        // A leading dot does not begin an extension, so a dotfile keeps its whole name.
        int dot = name.LastIndexOf('.');
        string stem = dot <= 0 ? name : name[..dot];
        string extension = dot <= 0 ? string.Empty : name[dot..];

        for (int index = 0; index < int.MaxValue; index++)
        {
            string candidate = Path.Join(
                directory,
                stem + "_" + index.ToString(CultureInfo.InvariantCulture) + extension);

            if (!fileSystem.ExistsNoFollow(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"could not find a free name near {path}");
    }
}
