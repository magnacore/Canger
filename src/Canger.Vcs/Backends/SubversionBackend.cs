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
            // A space in the first column means the file itself is unchanged; the columns after
            // it describe properties and locks, which are not shown.
            if (line.Length <= PathColumn || line[0] == ' ')
            {
                continue;
            }

            yield return (line[PathColumn..].Trim().TrimEnd('/'), Translate(line[0]));
        }
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
