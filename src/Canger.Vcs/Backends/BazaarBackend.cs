// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Vcs.Backends;

/// <summary>
/// GNU Bazaar.
/// </summary>
/// <remarks>
/// Bazaar's short status uses two columns much as git does, but with its own letters, and its
/// head revision is <c>last:1</c> rather than <c>HEAD</c>.
/// </remarks>
public sealed class BazaarBackend : IVcsBackend
{
    /// <summary>How Bazaar's two-column status codes map onto statuses.</summary>
    private static readonly (string First, string Second, VcsStatus Status)[] Translations =
    [
        ("+ -R", "K NM", VcsStatus.Staged),
        (" -", "D", VcsStatus.Deleted),
        ("?", " ", VcsStatus.Untracked),
    ];

    /// <summary>Where the path starts in a short status line.</summary>
    private const int PathColumn = 4;

    /// <inheritdoc />
    public string Name => "bzr";

    /// <inheritdoc />
    public string MarkerDirectory => ".bzr";

    /// <inheritdoc />
    public string Program => "bzr";

    /// <inheritdoc />
    public VcsStatus RootStatus(string root)
    {
        HashSet<VcsStatus> statuses = [];

        foreach (string line in StatusLines(root))
        {
            statuses.Add(Translate(line));
        }

        if (ConflictedPaths(root).Any())
        {
            statuses.Add(VcsStatus.Conflict);
        }

        return statuses.Count == 0 ? VcsStatus.Sync : VcsStatuses.Combine(statuses);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, VcsStatus> SubpathStatuses(string root)
    {
        Dictionary<string, VcsStatus> statuses = new(StringComparer.Ordinal);

        // Ignored files come from a separate listing, since the status output omits them.
        try
        {
            foreach (string path in VcsProcess.SplitNul(
                         VcsProcess.Run(Program, root, "ls", "--null", "--ignored")))
            {
                statuses[path.TrimEnd('/')] = VcsStatus.Ignored;
            }
        }
        catch (VcsException)
        {
            // No ignore rules, or an old Bazaar; the rest of the status is still worth having.
        }

        foreach (string line in StatusLines(root))
        {
            if (line.Length > PathColumn)
            {
                statuses[line[PathColumn..].Trim().TrimEnd('/')] = Translate(line);
            }
        }

        // Last, so it outranks whatever the status line said: a conflicted file also shows as
        // modified there, and a conflict is the more urgent of the two.
        foreach (string path in ConflictedPaths(root))
        {
            statuses[path] = VcsStatus.Conflict;
        }

        return statuses;
    }

    /// <inheritdoc />
    /// <remarks>
    /// As with Mercurial, comparing against the parent branch means contacting it, which is too
    /// slow to do while drawing. Having a parent at all is as much as is reported.
    /// </remarks>
    public VcsRemoteStatus RemoteStatus(string root) =>
        Configured(root, "parent_location") is null ? VcsRemoteStatus.None : VcsRemoteStatus.Sync;

    /// <inheritdoc />
    public string? Branch(string root)
    {
        try
        {
            string nickname = VcsProcess.Run(Program, root, "nick");
            return nickname.Length > 0 ? nickname : null;
        }
        catch (VcsException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public VcsCommit? Head(string root)
    {
        string output;

        try
        {
            // A one-line-per-field format, which is far easier to parse reliably than the long
            // format ranger reads.
            output = VcsProcess.Run(
                Program, root, "log", "--limit", "1", "--revision", "last:1",
                "--log-format", "line");
        }
        catch (VcsException)
        {
            return null;
        }

        if (output.Length == 0)
        {
            return null;
        }

        // "3: Author 2024-01-02 Summary here"
        string[] parts = output.Split(' ', 4);

        if (parts.Length < 4)
        {
            return null;
        }

        string revision = parts[0].TrimEnd(':');

        return new VcsCommit(
            revision,
            revision,
            parts[1],
            DateTimeOffset.TryParse(parts[2], out DateTimeOffset date)
                ? date
                : DateTimeOffset.MinValue,
            parts[3]);
    }

    /// <inheritdoc />
    public void Add(string root, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[] arguments = paths.Count == 0 ? ["add"] : ["add", "--", .. paths];
        VcsProcess.RunSilently(Program, root, arguments);
    }

    /// <inheritdoc />
    public void Reset(string root, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // --keep leaves the files on disk; only the record of them being added is undone.
        string[] arguments = paths.Count == 0
            ? ["remove", "--keep", "--new"]
            : ["remove", "--keep", "--new", "--", .. paths];

        VcsProcess.RunSilently(Program, root, arguments);
    }

    private IEnumerable<string> StatusLines(string root)
    {
        string output;

        try
        {
            output = VcsProcess.Run(Program, root, "status", "--short", "--no-classify");
        }
        catch (VcsException)
        {
            yield break;
        }

        foreach (string line in output.Split('\n'))
        {
            if (line.Length >= 2 && !DescribesSomethingOtherThanAPath(line[0]))
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// Whether a short-status line carries prose rather than a path.
    /// </summary>
    /// <param name="code">The line's first column.</param>
    /// <returns><see langword="true"/> when nothing after column four is a filename.</returns>
    /// <remarks>
    /// <para>
    /// Bazaar's first column is documented as the versioning state, and two of its values do not
    /// introduce a path at all:
    /// </para>
    /// <code>
    ///  M  f.txt
    /// C   Text conflict in f.txt
    /// P   T 2026-09-01 theirs
    /// </code>
    /// <para>
    /// <c>C</c> is followed by a human-readable description of the conflict and <c>P</c> by a
    /// pending merge's revision line. Read as records they become the subpaths
    /// <c>Text conflict in f.txt</c> and <c>T 2026-09-01 theirs</c>, each with status
    /// <c>unknown</c> because neither code appears in the translation table. Ranger has the same
    /// defect (<c>ext/vcs/bzr.py:110-125</c>) and Canger reproduced it.
    /// </para>
    /// <para>
    /// Skipping <c>C</c> here would lose the conflict, so it is read back from
    /// <see cref="ConflictedPaths"/>, which names the files instead of describing them.
    /// </para>
    /// </remarks>
    private static bool DescribesSomethingOtherThanAPath(char code) => code is 'C' or 'P';

    /// <summary>The files Bazaar considers conflicted.</summary>
    /// <param name="root">The working tree.</param>
    /// <returns>Paths relative to the tree.</returns>
    /// <remarks>
    /// <c>bzr conflicts</c> prints descriptions; <c>--text</c> prints the paths instead, which is
    /// the only machine-readable form it offers. It covers text conflicts, which is what a merge
    /// leaves behind in almost every case. Other kinds are simply not marked — the same as before,
    /// and better than inventing a subpath that cannot exist.
    /// </remarks>
    private IEnumerable<string> ConflictedPaths(string root)
    {
        string output;

        try
        {
            output = VcsProcess.Run(Program, root, "conflicts", "--text");
        }
        catch (VcsException)
        {
            yield break;
        }

        foreach (string line in output.Split('\n'))
        {
            string path = line.Trim();

            if (path.Length > 0)
            {
                yield return path.TrimEnd('/');
            }
        }
    }

    private string? Configured(string root, string key)
    {
        try
        {
            string value = VcsProcess.Run(Program, root, "config", key);
            return value.Length > 0 ? value : null;
        }
        catch (VcsException)
        {
            return null;
        }
    }

    private static VcsStatus Translate(string line)
    {
        if (line.Length < 2)
        {
            return VcsStatus.Unknown;
        }

        foreach ((string first, string second, VcsStatus status) in Translations)
        {
            if (first.Contains(line[0], StringComparison.Ordinal) &&
                second.Contains(line[1], StringComparison.Ordinal))
            {
                return status;
            }
        }

        return VcsStatus.Unknown;
    }
}
