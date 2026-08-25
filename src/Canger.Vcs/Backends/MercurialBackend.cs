// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;

namespace Canger.Vcs.Backends;

/// <summary>
/// Mercurial.
/// </summary>
/// <remarks>
/// Mercurial can emit JSON for both status and log, so this backend parses structured output
/// rather than columns. That removes the whole class of problem git's <c>-z</c> flags exist to
/// avoid: a filename containing a newline is simply a JSON string.
/// </remarks>
public sealed class MercurialBackend : IVcsBackend
{
    /// <summary>How Mercurial's one-letter status codes map onto statuses.</summary>
    private static readonly (string Codes, VcsStatus Status)[] Translations =
    [
        ("AR", VcsStatus.Staged),
        ("M", VcsStatus.Changed),
        ("!", VcsStatus.Deleted),
        ("?", VcsStatus.Untracked),
        ("I", VcsStatus.Ignored),
    ];

    /// <inheritdoc />
    public string Name => "hg";

    /// <inheritdoc />
    public string MarkerDirectory => ".hg";

    /// <inheritdoc />
    /// <remarks>
    /// <c>chg</c> is Mercurial's command server client: it keeps a warm process around instead of
    /// starting Python afresh, which for a status call on every directory change is the
    /// difference between usable and not. Ranger reaches for it too.
    /// </remarks>
    public string Program => HasChg.Value ? "chg" : "hg";

    private static readonly Lazy<bool> HasChg = new(() => Exists("chg"));

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
    /// Mercurial cannot say how it stands relative to a remote without contacting it, which is
    /// far too slow to do while drawing. Having a remote at all is as much as can be reported.
    /// </remarks>
    public VcsRemoteStatus RemoteStatus(string root) =>
        RemoteUrl(root) is null ? VcsRemoteStatus.None : VcsRemoteStatus.Sync;

    /// <inheritdoc />
    public string? Branch(string root)
    {
        try
        {
            string branch = VcsProcess.Run(Program, root, "branch");
            return branch.Length > 0 ? branch : null;
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
            output = VcsProcess.Run(Program, root, "log", "--limit", "1", "--template", "json");
        }
        catch (VcsException)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(output);

            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement entry = document.RootElement[0];

            // Mercurial reports the date as [seconds, offset]; only the first is wanted.
            double seconds = entry.TryGetProperty("date", out JsonElement date)
                             && date.ValueKind == JsonValueKind.Array
                             && date.GetArrayLength() > 0
                ? date[0].GetDouble()
                : 0;

            return new VcsCommit(
                Text(entry, "rev"),
                Text(entry, "node"),
                Text(entry, "user"),
                DateTimeOffset.FromUnixTimeSeconds((long)seconds),
                Text(entry, "desc").Split('\n')[0]);
        }
        catch (JsonException)
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

        // With nothing named, everything that has a status is forgotten — which is what "reset
        // the index" means where there is no index as such.
        IReadOnlyList<string> targets = paths.Count > 0 ? paths : [.. SubpathStatuses(root).Keys];

        if (targets.Count == 0)
        {
            return;
        }

        VcsProcess.RunSilently(Program, root, ["forget", "--", .. targets]);
    }

    /// <summary>Reads the status of everything that has one.</summary>
    private IEnumerable<(string Path, VcsStatus Status)> Entries(string root)
    {
        string output;

        try
        {
            output = VcsProcess.Run(Program, root, "status", "--all", "--template", "json");
        }
        catch (VcsException)
        {
            yield break;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (JsonElement entry in document.RootElement.EnumerateArray())
            {
                string code = Text(entry, "status");

                // "C" is clean, which is the overwhelming majority and says nothing.
                if (code is "C" or "")
                {
                    continue;
                }

                yield return (Text(entry, "path").TrimEnd('/'), Translate(code));
            }
        }
    }

    private string? RemoteUrl(string root)
    {
        try
        {
            string url = VcsProcess.Run(Program, root, "showconfig", "paths.default");
            return url.Length > 0 ? url : null;
        }
        catch (VcsException)
        {
            return null;
        }
    }

    private static VcsStatus Translate(string code)
    {
        foreach ((string codes, VcsStatus status) in Translations)
        {
            if (code.Length > 0 && codes.Contains(code[0], StringComparison.Ordinal))
            {
                return status;
            }
        }

        return VcsStatus.Unknown;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString()
            : string.Empty;

    /// <summary>Whether a program is on the path.</summary>
    private static bool Exists(string program)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");

        return path is not null &&
               path.Split(Path.PathSeparator)
                   .Any(d => d.Length > 0 && File.Exists(Path.Join(d, program)));
    }
}
