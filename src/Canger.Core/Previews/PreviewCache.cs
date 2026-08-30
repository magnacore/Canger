// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Previews;

/// <summary>
/// Remembers previews so they are not regenerated needlessly.
/// </summary>
/// <remarks>
/// <para>
/// Generating a preview means running a script, which may in turn run a syntax highlighter or a
/// document converter. Doing that again every time the cursor passes over a file, or every time
/// the terminal is resized by a column, would make the browser unusable.
/// </para>
/// <para>
/// Entries are keyed by size as well as by path, but a preview that told us it does not depend
/// on a dimension is stored under a wildcard for that dimension and matches any value of it.
/// That is what makes a resize cheap: a syntax-highlighted file is regenerated only when its
/// width changes, and a metadata listing never.
/// </para>
/// <para>
/// Every entry also carries the file's modification time and size as they were when it was
/// generated, and an entry whose file no longer matches is thrown away rather than returned.
/// Without that, editing a file and coming back showed its old contents until the whole cache
/// was reset by hand — the preview of a file being, after all, a picture of the file at a moment,
/// and a cache of pictures without their moment cannot tell a current one from a stale one.
/// </para>
/// <para>
/// Ranger reaches the same place from the other direction: it clears a file's preview whenever
/// that file is re-examined (<c>container/fsobject.py:291</c> calling <c>update_preview</c>), so
/// the refresh that notices the change is also what forgets the picture. Keeping the stamp here
/// does not depend on a refresh having happened, which matters because a preview can be asked for
/// on a file the listing has not looked at since.
/// </para>
/// </remarks>
public sealed class PreviewCache(int capacity = 200)
{
    /// <summary>Stands for "any value" in a cache key.</summary>
    public const int AnyDimension = -1;

    private readonly Dictionary<PreviewKey, Entry> _entries = [];
    private long _clock;

    /// <summary>How many previews are held.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Looks for a preview that will serve at a given size.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="width">Columns available.</param>
    /// <param name="height">Rows available.</param>
    /// <param name="stamp">
    /// The file as it is now. An entry taken of a different version of the file is discarded
    /// rather than returned, so a preview cannot outlive the thing it is a picture of.
    /// </param>
    /// <returns>The preview, or <see langword="null"/> when one must be generated.</returns>
    public PreviewResult? Find(string path, int width, int height, PreviewStamp stamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Most permissive first: a preview good at any size answers for every size.
        foreach (PreviewKey key in Candidates(path, width, height))
        {
            if (!_entries.TryGetValue(key, out Entry entry))
            {
                continue;
            }

            if (entry.Stamp != stamp)
            {
                // The file has changed since. Every remembered picture of it is of the old one,
                // whatever size it was taken at, so they all go rather than only this one.
                Invalidate(path);
                return null;
            }

            _entries[key] = entry with { LastUsed = ++_clock };
            return entry.Result;
        }

        return null;
    }

    /// <summary>
    /// Remembers a preview, under a key reflecting which dimensions it depends on.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="width">Columns it was generated for.</param>
    /// <param name="height">Rows it was generated for.</param>
    /// <param name="result">What was produced.</param>
    /// <param name="stamp">
    /// The file as it was when the preview was made. Taken before generating rather than after:
    /// a file edited while its preview was being produced then fails to match on the next
    /// lookup, and is generated again — which errs towards doing the work twice rather than
    /// towards showing something that was never true.
    /// </param>
    public void Store(string path, int width, int height, PreviewResult result,
                      PreviewStamp stamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        PreviewKey key = new(
            path,
            result.Fit.HasFlag(PreviewFit.AnyWidth) ? AnyDimension : width,
            result.Fit.HasFlag(PreviewFit.AnyHeight) ? AnyDimension : height);

        _entries[key] = new Entry(result, ++_clock, stamp);

        Trim();
    }

    /// <summary>Forgets every preview of a file, after it has changed.</summary>
    /// <param name="path">The file.</param>
    /// <returns>How many entries were dropped.</returns>
    public int Invalidate(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        List<PreviewKey> stale =
        [
            .. _entries.Keys.Where(k => string.Equals(k.Path, path, StringComparison.Ordinal)),
        ];

        foreach (PreviewKey key in stale)
        {
            _entries.Remove(key);
        }

        return stale.Count;
    }

    /// <summary>Forgets everything.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>The keys that could answer for a size, most permissive first.</summary>
    private static IEnumerable<PreviewKey> Candidates(string path, int width, int height)
    {
        yield return new PreviewKey(path, AnyDimension, AnyDimension);
        yield return new PreviewKey(path, AnyDimension, height);
        yield return new PreviewKey(path, width, AnyDimension);
        yield return new PreviewKey(path, width, height);
    }

    /// <summary>Drops the least recently used entries once the cache is full.</summary>
    private void Trim()
    {
        if (_entries.Count <= capacity)
        {
            return;
        }

        foreach (PreviewKey key in _entries
                     .OrderBy(e => e.Value.LastUsed)
                     .Take(_entries.Count - capacity)
                     .Select(e => e.Key)
                     .ToList())
        {
            _entries.Remove(key);
        }
    }

    /// <summary>What a preview was generated for.</summary>
    private readonly record struct PreviewKey(string Path, int Width, int Height);

    /// <summary>A remembered preview, when it was last wanted, and what it is a picture of.</summary>
    private readonly record struct Entry(PreviewResult Result, long LastUsed, PreviewStamp Stamp);
}
