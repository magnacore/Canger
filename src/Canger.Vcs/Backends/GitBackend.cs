// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace Canger.Vcs.Backends;

/// <summary>
/// Git.
/// </summary>
/// <remarks>
/// Everything here goes through porcelain formats with explicit separators — <c>--porcelain</c>,
/// <c>-z</c>, and a NUL-delimited log format — so that filenames containing spaces, newlines or
/// quotes parse correctly. Git's human-readable output quotes such names, which would then need
/// unquoting; asking for machine-readable output avoids the problem rather than solving it.
/// </remarks>
public sealed partial class GitBackend : IVcsBackend
{
    /// <summary>
    /// How git's two-letter status codes map onto statuses.
    /// </summary>
    /// <remarks>
    /// The first letter is the index and the second the working tree, so <c>M </c> is staged and
    /// <c> M</c> is merely changed. Order matters: the conflict patterns must be tried before
    /// the plainer ones they overlap with.
    /// </remarks>
    private static readonly (string Index, string Tree, VcsStatus Status)[] Translations =
    [
        ("MADRC", " ", VcsStatus.Staged),
        (" MADRC", "M", VcsStatus.Changed),
        (" MARC", "D", VcsStatus.Deleted),

        ("D", "DU", VcsStatus.Conflict),
        ("A", "AU", VcsStatus.Conflict),
        ("U", "ADU", VcsStatus.Conflict),

        ("?", "?", VcsStatus.Untracked),
        ("!", "!", VcsStatus.Ignored),
    ];

    /// <inheritdoc />
    public string Name => "git";

    /// <inheritdoc />
    public string MarkerDirectory => ".git";

    /// <inheritdoc />
    public string Program => "git";

    /// <inheritdoc />
    public VcsStatus RootStatus(string root)
    {
        HashSet<VcsStatus> statuses = [];

        foreach (string entry in StatusEntries(root, includeIgnored: false))
        {
            statuses.Add(Translate(entry));
        }

        return statuses.Count == 0 ? VcsStatus.Sync : VcsStatuses.Combine(statuses);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, VcsStatus> SubpathStatuses(string root)
    {
        Dictionary<string, VcsStatus> statuses = new(StringComparer.Ordinal);

        // Ignored and empty directories are reported by ls-files rather than status, because
        // status describes files and these are directories with nothing in them to describe.
        foreach (string path in Directories(root, ignored: true))
        {
            statuses[Normalise(path)] = VcsStatus.Ignored;
        }

        foreach (string path in Directories(root, ignored: false))
        {
            statuses[Normalise(path)] = VcsStatus.None;
        }

        foreach (string entry in StatusEntries(root, includeIgnored: true))
        {
            // The path starts at column three: two status letters and a space.
            if (entry.Length > 3)
            {
                statuses[Normalise(entry[3..])] = Translate(entry);
            }
        }

        return statuses;
    }

    /// <inheritdoc />
    public VcsRemoteStatus RemoteStatus(string root)
    {
        string? head = HeadReference(root);
        string? remote = head is null ? null : RemoteReference(root, head);

        if (head is null || remote is null)
        {
            return VcsRemoteStatus.None;
        }

        string output;

        try
        {
            // --left-right marks each commit with which side it is on, so one command answers
            // both halves of the question.
            output = VcsProcess.Run(Program, root, "rev-list", "--left-right",
                                    $"{remote}...{head}");
        }
        catch (VcsException)
        {
            return VcsRemoteStatus.None;
        }

        bool ahead = AheadMarker().IsMatch(output);
        bool behind = BehindMarker().IsMatch(output);

        return (ahead, behind) switch
        {
            (true, true) => VcsRemoteStatus.Diverged,
            (true, false) => VcsRemoteStatus.Ahead,
            (false, true) => VcsRemoteStatus.Behind,
            _ => VcsRemoteStatus.Sync,
        };
    }

    /// <inheritdoc />
    public string? Branch(string root)
    {
        string? head = HeadReference(root);

        // No symbolic ref means the head is a bare commit, which is what a detached head is.
        if (head is null)
        {
            return "detached";
        }

        Match match = BranchReference().Match(head);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <inheritdoc />
    public VcsCommit? Head(string root)
    {
        string output;

        try
        {
            // NUL-separated fields, so a commit message containing anything at all still parses.
            output = VcsProcess.Run(
                Program, root, "--no-pager", "log", "-1",
                "--pretty=%h%x00%H%x00%an <%ae>%x00%ct%x00%s");
        }
        catch (VcsException)
        {
            // A repository with no commits yet is not an error; it simply has no head.
            return null;
        }

        string[] fields = output.Split('\0');

        if (fields.Length < 5 ||
            !long.TryParse(fields[3], CultureInfo.InvariantCulture, out long seconds))
        {
            return null;
        }

        return new VcsCommit(
            fields[0],
            fields[1],
            Clean(fields[2]),
            DateTimeOffset.FromUnixTimeSeconds(seconds),
            Clean(fields[4]));
    }

    /// <inheritdoc />
    public void Add(string root, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        VcsProcess.RunSilently(Program, root, [.. Arguments("add", "--all", paths)]);
    }

    /// <inheritdoc />
    public void Reset(string root, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        VcsProcess.RunSilently(Program, root, [.. Arguments("reset", null, paths)]);
    }

    /// <summary>Builds an argument list, adding the path separator only when there are paths.</summary>
    private static IEnumerable<string> Arguments(string verb, string? flag,
                                                 IReadOnlyList<string> paths)
    {
        yield return verb;

        if (flag is not null)
        {
            yield return flag;
        }

        if (paths.Count == 0)
        {
            yield break;
        }

        // Everything after -- is a path, so a file named like an option is still a file.
        yield return "--";

        foreach (string path in paths)
        {
            yield return path;
        }
    }

    /// <summary>Reads the status entries, skipping the second half of every rename.</summary>
    /// <remarks>
    /// A rename is reported as two NUL-separated records — the new name then the old — but only
    /// the first carries a status. Consuming the second as though it were an entry of its own
    /// would produce a garbage status for a path that does not exist.
    /// </remarks>
    private static IEnumerable<string> StatusEntries(string root, bool includeIgnored)
    {
        string[] arguments = includeIgnored
            ? ["status", "--porcelain", "-z", "--ignored"]
            : ["status", "--porcelain", "-z"];

        string output = VcsProcess.Run("git", root, arguments);
        bool skip = false;

        foreach (string record in VcsProcess.SplitNul(output))
        {
            if (skip)
            {
                skip = false;
                continue;
            }

            skip = record.StartsWith('R');
            yield return record;
        }
    }

    /// <summary>Lists directories git would otherwise say nothing about.</summary>
    private static IEnumerable<string> Directories(string root, bool ignored)
    {
        string[] arguments = ignored
            ? ["ls-files", "-z", "--others", "--directory", "--ignored", "--exclude-standard"]
            : ["ls-files", "-z", "--others", "--directory", "--exclude-standard"];

        string output;

        try
        {
            output = VcsProcess.Run("git", root, arguments);
        }
        catch (VcsException)
        {
            yield break;
        }

        foreach (string path in VcsProcess.SplitNul(output))
        {
            // Only directories, which git marks with a trailing separator.
            if (path.EndsWith('/'))
            {
                yield return path;
            }
        }
    }

    private static string? HeadReference(string root)
    {
        try
        {
            string reference = VcsProcess.Run("git", root, "symbolic-ref", "HEAD");
            return reference.Length > 0 ? reference : null;
        }
        catch (VcsException)
        {
            return null;
        }
    }

    private static string? RemoteReference(string root, string reference)
    {
        try
        {
            string remote = VcsProcess.Run("git", root, "for-each-ref", "--format=%(upstream)",
                                           reference);

            return remote.Length > 0 ? remote : null;
        }
        catch (VcsException)
        {
            return null;
        }
    }

    /// <summary>Maps a two-letter status code onto a status.</summary>
    private static VcsStatus Translate(string entry)
    {
        if (entry.Length < 2)
        {
            return VcsStatus.Unknown;
        }

        foreach ((string index, string tree, VcsStatus status) in Translations)
        {
            if (index.Contains(entry[0], StringComparison.Ordinal) &&
                tree.Contains(entry[1], StringComparison.Ordinal))
            {
                return status;
            }
        }

        return VcsStatus.Unknown;
    }

    /// <summary>Removes the trailing separator git puts on directories, and normalises the path.</summary>
    private static string Normalise(string path) => path.TrimEnd('/');

    /// <summary>
    /// Replaces control characters, which would otherwise move the cursor when drawn.
    /// </summary>
    /// <remarks>
    /// A commit message can contain anything at all, and it is shown on the status line. An
    /// escape sequence in an author name should not be able to repaint the screen.
    /// </remarks>
    private static string Clean(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? ' ' : source[i];
            }
        });

    [GeneratedRegex("^>", RegexOptions.Multiline)]
    private static partial Regex AheadMarker();

    [GeneratedRegex("^<", RegexOptions.Multiline)]
    private static partial Regex BehindMarker();

    [GeneratedRegex("^refs/heads/(.+)$")]
    private static partial Regex BranchReference();
}
