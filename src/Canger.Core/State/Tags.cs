// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.State;

/// <summary>
/// Marks on files that survive between sessions.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the marks used for a single bulk operation, a tag stays until it is removed, so a file
/// can be flagged now and acted on next week. Each tag is one character, which is what lets a
/// user keep several independent categories at once.
/// </para>
/// <para>
/// The file is re-read before every change and written back after. That costs a little on each
/// operation and means two running instances do not overwrite one another's tags, which matters
/// more.
/// </para>
/// </remarks>
public sealed class Tags(string path)
{
    /// <summary>The tag used when none is named.</summary>
    public const char DefaultTag = '*';

    private readonly Dictionary<string, char> _tags = new(StringComparer.Ordinal);

    /// <summary>Where the tags are kept.</summary>
    public string Path { get; } = path;

    /// <summary>The tagged paths and their tags.</summary>
    public IReadOnlyDictionary<string, char> Entries => _tags;

    /// <summary>How many files are tagged.</summary>
    public int Count => _tags.Count;

    /// <summary>Whether a character may be used as a tag.</summary>
    /// <param name="tag">The character.</param>
    /// <returns><see langword="true"/> when it is printable and not a space.</returns>
    public static bool IsValidTag(char tag) => !char.IsControl(tag) && tag != ' ';

    /// <summary>Whether a file is tagged at all.</summary>
    /// <param name="filePath">The file.</param>
    /// <returns><see langword="true"/> when it carries any tag.</returns>
    public bool Contains(string filePath) => _tags.ContainsKey(filePath);

    /// <summary>The tag on a file, if any.</summary>
    /// <param name="filePath">The file.</param>
    /// <returns>The tag, or <see langword="null"/> when the file is not tagged.</returns>
    public char? TagOf(string filePath) =>
        _tags.TryGetValue(filePath, out char tag) ? tag : null;

    /// <summary>Tags files.</summary>
    /// <param name="paths">The files.</param>
    /// <param name="tag">The tag to apply.</param>
    public void Add(IEnumerable<string> paths, char tag = DefaultTag)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!IsValidTag(tag))
        {
            return;
        }

        Reload();

        foreach (string filePath in paths)
        {
            _tags[filePath] = tag;
        }

        Save();
    }

    /// <summary>Removes tags from files.</summary>
    /// <param name="paths">The files.</param>
    public void Remove(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Reload();

        foreach (string filePath in paths)
        {
            _tags.Remove(filePath);
        }

        Save();
    }

    /// <summary>
    /// Adds a tag where it is missing and removes it where it is present.
    /// </summary>
    /// <remarks>
    /// Toggling with the default tag clears whatever tag a file has, rather than only the
    /// default one, so one key always means "untag this" whatever was applied.
    /// </remarks>
    /// <param name="paths">The files.</param>
    /// <param name="tag">The tag to toggle.</param>
    public void Toggle(IEnumerable<string> paths, char tag = DefaultTag)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!IsValidTag(tag))
        {
            return;
        }

        Reload();

        foreach (string filePath in paths)
        {
            if (_tags.TryGetValue(filePath, out char existing))
            {
                if (tag == DefaultTag || existing == tag)
                {
                    _tags.Remove(filePath);
                    continue;
                }
            }

            _tags[filePath] = tag;
        }

        Save();
    }

    /// <summary>The files carrying any of the given tags.</summary>
    /// <param name="tags">The tags to look for, or empty for any tag at all.</param>
    /// <returns>The paths.</returns>
    public IReadOnlyList<string> Tagged(IReadOnlyCollection<char>? tags = null) =>
        tags is { Count: > 0 }
            ? [.. _tags.Where(e => tags.Contains(e.Value)).Select(e => e.Key)]
            : [.. _tags.Keys];

    /// <summary>
    /// Moves tags with a file that has been renamed or moved.
    /// </summary>
    /// <remarks>
    /// Applies to everything beneath a directory too, so tagging files and then moving the
    /// directory does not silently lose them.
    /// </remarks>
    /// <param name="oldPath">Where the file was.</param>
    /// <param name="newPath">Where it is now.</param>
    public void MovePath(string oldPath, string newPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldPath);
        ArgumentException.ThrowIfNullOrEmpty(newPath);

        Reload();

        foreach ((string filePath, char tag) in _tags.ToList())
        {
            if (string.Equals(filePath, oldPath, StringComparison.Ordinal))
            {
                _tags.Remove(filePath);
                _tags[newPath] = tag;
            }
            else if (filePath.StartsWith(oldPath + "/", StringComparison.Ordinal))
            {
                _tags.Remove(filePath);
                _tags[newPath + filePath[oldPath.Length..]] = tag;
            }
        }

        Save();
    }

    /// <summary>Re-reads the tags from disk.</summary>
    public void Reload()
    {
        _tags.Clear();

        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            foreach (string line in File.ReadLines(Path))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                // A path on its own carries the default tag; anything else is written as the
                // tag, a colon, then the path. This is ranger's format, so existing tag files
                // are read correctly.
                if (line.Length >= 3 && line[1] == ':' && IsValidTag(line[0]) && line[0] != '/')
                {
                    _tags[line[2..]] = line[0];
                }
                else
                {
                    _tags[line] = DefaultTag;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable tag file should not stop Canger starting.
        }
    }

    /// <summary>Writes the tags to disk.</summary>
    public void Save()
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = Path + ".new";

            File.WriteAllLines(
                temporary,
                _tags.OrderBy(e => e.Key, StringComparer.Ordinal)
                     .Select(e => e.Value == DefaultTag ? e.Key : $"{e.Value}:{e.Key}"));

            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing tags is unfortunate; failing because of it would be worse.
        }
    }
}
