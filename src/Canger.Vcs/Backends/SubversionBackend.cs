// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;

namespace Canger.Vcs.Backends;

/// <summary>
/// Subversion.
/// </summary>
/// <remarks>
/// Subversion has no notion of a staging area or of local commits, so several of these answers
/// are necessarily thinner than git's: there is no branch to report, and how the working copy
/// stands relative to the server cannot be known without asking the server.
/// </remarks>
public sealed class SubversionBackend : IVcsBackend
{
    /// <summary>How Subversion's one-letter status codes map onto statuses.</summary>
    private static readonly (string Codes, VcsStatus Status)[] Translations =
    [
        ("ADR", VcsStatus.Staged),
        ("C", VcsStatus.Conflict),
        ("I", VcsStatus.Ignored),
        ("M~", VcsStatus.Changed),
        ("X", VcsStatus.None),
        ("?", VcsStatus.Untracked),
        ("!", VcsStatus.Deleted),
    ];

    /// <summary>Where the path starts in a status line, after the seven status columns.</summary>
    private const int PathColumn = 8;

    /// <inheritdoc />
    public string Name => "svn";

    /// <inheritdoc />
    public string MarkerDirectory => ".svn";

    /// <inheritdoc />
    public string Program => "svn";

    /// <inheritdoc />
    public VcsStatus RootStatus(string root)
    {
        HashSet<VcsStatus> statuses = [];

        foreach ((_, VcsStatus status) in Entries(root))
        {
            statuses.Add(status);
        }

        return statuses.Count == 0 ? VcsStatus.Sync : VcsStatuses.Combine(statuses);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, VcsStatus> SubpathStatuses(string root)
    {
        Dictionary<string, VcsStatus> statuses = new(StringComparer.Ordinal);

        foreach ((string path, VcsStatus status) in Entries(root))
        {
            statuses[path] = status;
        }

        return statuses;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A working copy served from the local filesystem has no meaningful remote, so it reports
    /// none rather than claiming to be in sync with itself.
    /// </remarks>
    public VcsRemoteStatus RemoteStatus(string root)
    {
        string? url = RemoteUrl(root);

        return url is null || url.StartsWith("file://", StringComparison.Ordinal)
            ? VcsRemoteStatus.None
            : VcsRemoteStatus.Sync;
    }

    /// <inheritdoc />
    /// <remarks>Subversion has no branches as such; they are directory conventions.</remarks>
    public string? Branch(string root) => null;

    /// <inheritdoc />
    public VcsCommit? Head(string root)
    {
        string output;

        try
        {
            output = VcsProcess.Run(Program, root, "log", "--xml", "--limit", "1");
        }
        catch (VcsException)
        {
            return null;
        }

        try
        {
            XElement? entry = XDocument.Parse(output).Root?.Element("logentry");

            if (entry is null)
            {
                return null;
            }

            string revision = entry.Attribute("revision")?.Value ?? string.Empty;
            string message = entry.Element("msg")?.Value ?? string.Empty;

            DateTimeOffset date = DateTimeOffset.TryParse(
                entry.Element("date")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
                    ? parsed
                    : DateTimeOffset.MinValue;

            return new VcsCommit(
                revision,
                revision,
                entry.Element("author")?.Value ?? string.Empty,
                date,
                message.Split('\n')[0]);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
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

        IReadOnlyList<string> targets = paths.Count > 0 ? paths : [.. SubpathStatuses(root).Keys];

        if (targets.Count == 0)
        {
            return;
        }

        VcsProcess.RunSilently(Program, root, ["revert", "--", .. targets]);
    }

    /// <summary>Reads the status of everything that has one.</summary>
    private IEnumerable<(string Path, VcsStatus Status)> Entries(string root)
    {
        string output;

        try
        {
            output = VcsProcess.Run(Program, root, "status");
        }
        catch (VcsException)
        {
            yield break;
        }

        foreach (string line in output.Split('\n'))
        {
            if (!IsStatusRecord(line))
            {
                continue;
            }

            // A space in the first column means the file itself is unchanged; the columns after
            // it describe properties and locks, which are not shown.
            if (line[0] == ' ')
            {
                continue;
            }

            yield return (line[PathColumn..].Trim().TrimEnd('/'), Translate(line[0]));
        }
    }

    /// <summary>What each of the seven status columns is allowed to contain.</summary>
    /// <remarks>
    /// From <c>svn help status</c>, in order: the item itself, its properties, whether the working
    /// copy is locked, whether a commit carries history with it, whether it is switched, the lock
    /// token, and whether it is a tree conflict. A space always means "nothing to say".
    /// </remarks>
    private static readonly string[] StatusColumns =
    [
        " ACDIMRX?!~", " CM", " L", " +", " S", " KOTB", " C",
    ];

    /// <summary>Whether a line of <c>svn status</c> output is a status record at all.</summary>
    /// <param name="line">One line, as printed.</param>
    /// <returns><see langword="true"/> when the seven status columns and the separator all fit.</returns>
    /// <remarks>
    /// <para>
    /// <c>svn status</c> does not print only records. When there are conflicts it ends with a
    /// summary block:
    /// </para>
    /// <code>
    /// ?       untracked.txt
    /// Summary of conflicts:
    ///   Text conflicts: 1
    /// </code>
    /// <para>
    /// Reading the first column of <c>Summary of conflicts:</c> gives <c>S</c>, which matches no
    /// rule and so becomes <c>unknown</c>, and column eight onwards gives the "path"
    /// <c>of conflicts:</c> — a subpath that cannot exist. Ranger has the same defect
    /// (<c>ext/vcs/svn.py:100-116</c>, which tests only <c>line[0] == ' '</c>) and Canger
    /// reproduced it exactly. It is inert today, because nothing looks that name up and the
    /// summary only appears alongside a conflict, which outranks <c>unknown</c> — but a wrong
    /// entry in a status table is a wrong entry, and this is a deliberate divergence from ranger
    /// rather than an oversight.
    /// </para>
    /// <para>
    /// Checking every column rather than just the first is what makes this robust: any prose line
    /// fails at column one, where <c>u</c> of <c>Summary</c> is not a property status. Testing
    /// column zero alone would let <c>Assertion …</c> or <c>Merge …</c> straight through.
    /// </para>
    /// </remarks>
    private static bool IsStatusRecord(string line)
    {
        // Seven columns, a separating space, and at least one character of path.
        if (line.Length <= PathColumn || line[PathColumn - 1] != ' ')
        {
            return false;
        }

        for (int column = 0; column < StatusColumns.Length; column++)
        {
            if (!StatusColumns[column].Contains(line[column], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private string? RemoteUrl(string root)
    {
        try
        {
            string output = VcsProcess.Run(Program, root, "info", "--xml");
            string? url = XDocument.Parse(output).Root?.Element("entry")?.Element("url")?.Value;

            return string.IsNullOrEmpty(url) ? null : url;
        }
        catch (Exception e) when (e is VcsException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static VcsStatus Translate(char code)
    {
        foreach ((string codes, VcsStatus status) in Translations)
        {
            if (codes.Contains(code, StringComparison.Ordinal))
            {
                return status;
            }
        }

        return VcsStatus.Unknown;
    }
}
