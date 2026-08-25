// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Vcs;

/// <summary>
/// What a version control system makes of a file or directory.
/// </summary>
/// <remarks>
/// The order matters: a directory takes the most important status among everything under it, and
/// "most important" means "earliest here". A folder holding one conflicted file and a hundred
/// clean ones should read as conflicted, because that is the thing needing attention.
/// </remarks>
public enum VcsStatus
{
    /// <summary>Nothing is known, usually because the repository could not be read.</summary>
    Unknown = 0,

    /// <summary>A merge left this needing a decision.</summary>
    Conflict,

    /// <summary>Not under version control.</summary>
    Untracked,

    /// <summary>Removed since the last commit.</summary>
    Deleted,

    /// <summary>Edited since the last commit.</summary>
    Changed,

    /// <summary>Edited and added to the index, ready to commit.</summary>
    Staged,

    /// <summary>Deliberately excluded.</summary>
    Ignored,

    /// <summary>The same as the last commit.</summary>
    Sync,

    /// <summary>Under a repository but carrying no status of its own.</summary>
    None,
}

/// <summary>
/// How a repository stands relative to the remote it tracks.
/// </summary>
public enum VcsRemoteStatus
{
    /// <summary>No remote, or none that could be determined.</summary>
    None = 0,

    /// <summary>The same as the remote.</summary>
    Sync,

    /// <summary>Has commits the remote does not.</summary>
    Ahead,

    /// <summary>The remote has commits this does not.</summary>
    Behind,

    /// <summary>Each has commits the other does not.</summary>
    Diverged,
}

/// <summary>
/// One revision, as much of it as is worth showing on a status line.
/// </summary>
/// <param name="ShortId">The abbreviated identifier.</param>
/// <param name="Id">The full identifier.</param>
/// <param name="Author">Who made it.</param>
/// <param name="Date">When it was made.</param>
/// <param name="Summary">Its first line.</param>
public sealed record VcsCommit(
    string ShortId,
    string Id,
    string Author,
    DateTimeOffset Date,
    string Summary);

/// <summary>Ordering helpers for statuses.</summary>
public static class VcsStatuses
{
    /// <summary>
    /// Statuses in the order a directory inherits them, most important first.
    /// </summary>
    /// <remarks>
    /// <c>Ignored</c> and <c>None</c> are deliberately absent: ranger excludes them so that a
    /// directory is not reported as ignored merely because something inside it is.
    /// </remarks>
    public static IReadOnlyList<VcsStatus> DirectoryPrecedence { get; } =
    [
        VcsStatus.Conflict,
        VcsStatus.Untracked,
        VcsStatus.Deleted,
        VcsStatus.Changed,
        VcsStatus.Staged,
        VcsStatus.Sync,
        VcsStatus.Unknown,
    ];

    /// <summary>
    /// Picks the status a directory should show, given what is inside it.
    /// </summary>
    /// <param name="statuses">The statuses found underneath.</param>
    /// <returns>The most important one, or <see cref="VcsStatus.Sync"/> when none applies.</returns>
    public static VcsStatus Combine(IEnumerable<VcsStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        HashSet<VcsStatus> present = [.. statuses];

        foreach (VcsStatus candidate in DirectoryPrecedence)
        {
            if (present.Contains(candidate))
            {
                return candidate;
            }
        }

        return VcsStatus.Sync;
    }
}
