// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Model;

/// <summary>
/// Measures how much a directory tree holds.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the browser because it is the one operation here whose cost is unbounded: a
/// directory may hold a million files, and the only way to know its size is to look at all of
/// them. Nothing calls this except <c>:get_cumulative_size</c>, which is the user asking for it
/// explicitly.
/// </para>
/// <para>
/// The walk runs in parallel across subdirectories, which is where the time goes — the work is
/// almost entirely waiting on <c>stat</c>, so several outstanding at once is a real gain on any
/// tree deep enough for the measurement to be worth taking.
/// </para>
/// </remarks>
public static class DirectorySize
{
    /// <summary>How many directories are walked at once.</summary>
    /// <remarks>
    /// Bounded so measuring a huge tree cannot swamp a machine that is also drawing an
    /// interface. Beyond a handful the disk, not the scheduler, is the limit anyway.
    /// </remarks>
    private static readonly int Parallelism = Math.Min(Environment.ProcessorCount, 8);

    /// <summary>Adds up the sizes of every file beneath a directory.</summary>
    /// <param name="fileSystem">What to read through.</param>
    /// <param name="path">The directory to measure.</param>
    /// <param name="cancellationToken">Abandons the walk.</param>
    /// <returns>The total in bytes; unreadable subdirectories contribute nothing.</returns>
    public static long Measure(IFileSystem fileSystem, string path,
                               CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(path);

        long total = 0;
        List<string> directories = [];

        foreach (DirectoryEntry entry in Read(fileSystem, path, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A link to a directory is not descended into — that is what keeps the walk finite,
            // since a link pointing at an ancestor is enough to make the tree not a tree. Ranger
            // gets the same result from `os.walk`, which does not follow them either.
            if (entry.LinkStatus.IsSymbolicLink && entry.EffectiveStatus.IsDirectory)
            {
                continue;
            }

            if (entry.EffectiveStatus.IsDirectory)
            {
                directories.Add(Path.Join(path, entry.Name));
                continue;
            }

            // A link to a file contributes its target's size, because ranger stats through the
            // link (container/directory.py:570-579). A broken one contributes nothing, which is
            // what ranger's `except OSError: continue` amounts to.
            if (entry.LinkStatus.IsSymbolicLink && entry.TargetStatus is null)
            {
                continue;
            }

            total += entry.EffectiveStatus.Size;
        }

        if (directories.Count == 0)
        {
            return total;
        }

        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Parallelism,
            CancellationToken = cancellationToken,
        };

        Parallel.ForEach(directories, options, child =>
        {
            long size = Measure(fileSystem, child, cancellationToken);
            Interlocked.Add(ref total, size);
        });

        return total;
    }

    /// <summary>Reads a directory, treating one that cannot be read as empty.</summary>
    private static IReadOnlyList<DirectoryEntry> Read(IFileSystem fileSystem, string path,
                                                     CancellationToken cancellationToken)
    {
        try
        {
            return fileSystem.ListDirectory(path, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A directory the user cannot enter contributes nothing, which is more useful than
            // abandoning the whole measurement.
            return [];
        }
    }
}
