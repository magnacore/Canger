// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Tasks;

/// <summary>
/// Counts the entries an archiver names as it works, against what each of them is worth.
/// </summary>
/// <remarks>
/// <para>
/// Info-ZIP reports nothing a byte at a time, and its output file cannot be watched growing
/// because <c>zip</c> writes to a temporary file and renames it at the very end — measured: the
/// temporary grew to 74 MB while the named archive did not yet exist, so the count only ever
/// appeared once the job was over. What both halves of Info-ZIP do say is which entry they are on:
/// <c>  adding: data/f3.bin (deflated 24%)</c> and <c>  inflating: dest/data/f3.bin</c>. Given a
/// manifest of what each entry weighs, that is a true byte count.
/// </para>
/// <para>
/// Not a percentage scraped from the program's own display, which differs between versions and
/// would be a parser to keep working per tool. The only thing read here is a path, and a path is
/// the one thing an archiver cannot rephrase.
/// </para>
/// <para>
/// The granularity is one entry: an archive of five large files moves in five steps, while one of
/// many small files is smooth. That is a true count either way, which is the difference between
/// coarse and wrong.
/// </para>
/// </remarks>
public sealed class ManifestProgress : IMeasurableProgress
{
    /// <summary>What each entry is worth, by its name as the manifest knows it.</summary>
    private readonly Dictionary<string, long> _sizes = new(StringComparer.Ordinal);

    /// <summary>Entries sharing a last segment, which is what a printed path is matched by.</summary>
    private readonly Dictionary<string, List<string>> _byLastSegment =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> _counted = new(StringComparer.Ordinal);
    private readonly IFileSystem? _fileSystem;
    private readonly IReadOnlyList<string> _paths;

    private long _completed;
    private long _total;

    /// <summary>A manifest already known, as an archive's own index gives it.</summary>
    /// <param name="entries">Each entry's name and the size it will occupy once written.</param>
    public ManifestProgress(IReadOnlyList<(string Name, long Size)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _paths = [];
        _fileSystem = null;

        foreach ((string name, long size) in entries)
        {
            Include(name, size);
        }

        IsMeasured = true;
    }

    /// <summary>A manifest to be built by walking what the command was given.</summary>
    /// <param name="fileSystem">Used to walk the tree.</param>
    /// <param name="paths">What the command was given to store.</param>
    /// <remarks>
    /// Walked by the queue in slices, exactly as a copy's measuring is, because a directory may
    /// hold a million files and looking at all of them on the spot would stop the interface.
    /// </remarks>
    public ManifestProgress(IFileSystem fileSystem, IReadOnlyList<string> paths)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public bool IsMeasured { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Withheld until the manifest is complete, so the percentage cannot fall as the walk catches
    /// up. A bar that goes backwards reads as a fault.
    /// </remarks>
    public long? Total => IsMeasured && _total > 0 ? _total : null;

    /// <inheritdoc />
    public long? Completed => _counted.Count > 0 ? _completed : null;

    /// <inheritdoc />
    /// <remarks>
    /// Both Info-ZIP programs announce entries on standard output, so a source reading only
    /// standard error would hear nothing at all from either of them.
    /// </remarks>
    public bool ReadsStandardOutput => true;

    /// <inheritdoc />
    public IEnumerator<Unit> Measure()
    {
        if (_fileSystem is not null)
        {
            foreach (string path in _paths)
            {
                foreach ((string found, long size) in TreeSize.Entries(_fileSystem, path))
                {
                    Include(found, size);
                    yield return Unit.Value;
                }
            }
        }

        IsMeasured = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole stream each time, and every entry counted at most once, so re-reading what has
    /// already been seen adds nothing. That is what lets this hold no position of its own.
    /// </remarks>
    public void Update(string reported)
    {
        ArgumentNullException.ThrowIfNull(reported);

        foreach (string line in reported.Split('\n'))
        {
            if (Announced(line) is { } path && Match(path) is { } entry &&
                _counted.Add(entry))
            {
                _completed += _sizes[entry];
            }
        }
    }

    /// <summary>The path an output line announces, if it announces one.</summary>
    /// <param name="line">One line of the command's output.</param>
    /// <returns>The path, or <see langword="null"/> when the line is something else.</returns>
    /// <remarks>
    /// The shape both programs share is <c>verb: path</c>, with <c>zip</c> adding a parenthesised
    /// note about how well it compressed. Taking the last colon rather than the first is what
    /// keeps a Windows-style path or a drive letter from splitting in the wrong place.
    /// </remarks>
    private static string? Announced(string line)
    {
        int colon = line.LastIndexOf(": ", StringComparison.Ordinal);

        if (colon < 0)
        {
            return null;
        }

        string path = line[(colon + 2)..].Trim();

        // `  adding: data/f3.bin (deflated 24%)` — the note is the program's own display, and
        // the only part of it read here is that it ends the name.
        int note = path.LastIndexOf(" (", StringComparison.Ordinal);

        if (note > 0 && path.EndsWith(')'))
        {
            path = path[..note];
        }

        return path.Length > 0 ? path.TrimEnd('/') : null;
    }

    /// <summary>Which manifest entry a printed path refers to.</summary>
    /// <param name="path">The path the command printed.</param>
    /// <returns>The entry's manifest name, or <see langword="null"/> when none matches.</returns>
    /// <remarks>
    /// Matched on trailing segments in whichever direction fits, because the two ends disagree in
    /// opposite ways: storing, the manifest holds absolute paths while <c>zip</c> prints the
    /// relative names it was given; extracting, the manifest holds names inside the archive while
    /// <c>unzip</c> prints where it put them. Comparing from the last segment backwards settles
    /// both, and the longest agreement wins so that two files sharing a name are told apart.
    /// </remarks>
    private string? Match(string path)
    {
        string last = LastSegment(path);

        if (!_byLastSegment.TryGetValue(last, out List<string>? candidates))
        {
            return null;
        }

        string? best = null;
        int longest = 0;

        foreach (string candidate in candidates)
        {
            int agreed = TrailingAgreement(candidate, path);

            if (agreed > longest)
            {
                longest = agreed;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>How many trailing path segments two paths share.</summary>
    private static int TrailingAgreement(string left, string right)
    {
        string[] one = left.TrimEnd('/').Split('/');
        string[] other = right.TrimEnd('/').Split('/');
        int shared = 0;

        while (shared < one.Length && shared < other.Length &&
               string.Equals(one[^(shared + 1)], other[^(shared + 1)], StringComparison.Ordinal))
        {
            shared++;
        }

        return shared;
    }

    /// <summary>The last segment of a path, which is what candidates are indexed by.</summary>
    private static string LastSegment(string path)
    {
        string trimmed = path.TrimEnd('/');
        int separator = trimmed.LastIndexOf('/');

        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }

    /// <summary>Adds one entry to the manifest.</summary>
    private void Include(string name, long size)
    {
        string key = name.TrimEnd('/');

        if (key.Length == 0 || !_sizes.TryAdd(key, size))
        {
            return;
        }

        _total += size;

        string last = LastSegment(key);

        if (!_byLastSegment.TryGetValue(last, out List<string>? candidates))
        {
            candidates = [];
            _byLastSegment[last] = candidates;
        }

        candidates.Add(key);
    }
}
