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
            if (!CanBeStored(filePath))
            {
                continue;
            }

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
    /// Removes the tags of things that have gone, and of everything that was inside them.
    /// </summary>
    /// <param name="paths">What has been removed.</param>
    /// <returns>How many tags were dropped.</returns>
    /// <remarks>
    /// <para>
    /// Deleting a tagged file left its tag behind, pointing at nothing; deleting a tagged folder
    /// left a tag for the folder and for every tagged file inside it. Ranger clears them as part
    /// of deleting (<c>core/actions.py:1687-1690</c>) and this is the same rule.
    /// </para>
    /// <para>
    /// Not the same comparison, though. Ranger asks whether the tag's path
    /// <c>startswith</c> the deleted one, which is a comparison of text rather than of paths:
    /// deleting <c>/a/b</c> there also untags <c>/a/bc</c>, a different file that merely begins
    /// the same way. This asks whether one path is really inside the other, which is what
    /// <c>startswith</c> was reaching for.
    /// </para>
    /// <para>
    /// Only in response to something being removed — never as a sweep over tags whose files are
    /// missing. A tag on a file on a drive that is not plugged in is not a stale tag, and a
    /// tidy-up that could not tell the difference would quietly empty the file every time
    /// somebody browsed without their external disc.
    /// </para>
    /// </remarks>
    public int RemoveUnder(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<string> gone = [.. paths.Where(p => p is { Length: > 0 })];

        if (gone.Count == 0)
        {
            return 0;
        }

        Reload();

        List<string> stale =
        [
            .. _tags.Keys.Where(tagged => gone.Exists(
                   path => string.Equals(tagged, path, StringComparison.Ordinal)
                           || FileOperations.PathRelation.IsInside(tagged, path))),
        ];

        foreach (string path in stale)
        {
            _tags.Remove(path);
        }

        if (stale.Count > 0)
        {
            Save();
        }

        return stale.Count;
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

            if (!CanBeStored(filePath))
            {
                continue;
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

    /// <summary>
    /// Whether tags are kept at all, as opposed to being held only for this session.
    /// </summary>
    /// <remarks>
    /// <c>--clean</c> passes <c>/dev/null</c> as the path, and the save is a rename over it — as
    /// an ordinary user that fails harmlessly, but as root it replaces the device node with a
    /// regular file and everything on the system redirecting to <c>/dev/null</c> starts filling
    /// a disk. Ranger avoids the question with a separate do-nothing class,
    /// <c>container/tags.py</c>'s <c>TagsDummy</c>.
    /// </remarks>
    public bool Persistent { get; init; } = true;

    /// <summary>
    /// Whether the last read of the tag file failed, so writing would destroy it.
    /// </summary>
    /// <remarks>
    /// Every change re-reads before writing, so that two running instances cannot clobber one
    /// another. That makes a failed read dangerous rather than merely unhelpful: the set would be
    /// empty, and saving it would write the emptiness over every tag the user has.
    /// </remarks>
    public bool CouldNotBeRead { get; private set; }

    /// <summary>Re-reads the tags from disk.</summary>
    public void Reload()
    {
        // Read into a local and assign only on success. Clearing first — which is what this did —
        // means a read failure leaves nothing behind and the next Save writes that nothing over
        // the file. Ranger keeps its existing dictionary and notifies (`container/tags.py:74-83`);
        // it empties only when the file genuinely does not exist.
        Dictionary<string, char> read = new(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(Path))
            {
                _tags.Clear();
                CouldNotBeRead = false;
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
                    read[line[2..]] = line[0];
                }
                else
                {
                    read[line] = DefaultTag;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable tag file should not stop Canger starting — but it must stop Canger
            // writing, or starting is what destroys it.
            CouldNotBeRead = true;
            return;
        }

        _tags.Clear();

        foreach ((string path, char tag) in read)
        {
            _tags[path] = tag;
        }

        CouldNotBeRead = false;
    }

    /// <summary>Writes the tags to disk.</summary>
    /// <remarks>
    /// Refuses when the file could not be read, since what is in memory is then not the user's
    /// tags but the absence of them.
    /// </remarks>
    public void Save()
    {
        if (CouldNotBeRead || !Persistent)
        {
            return;
        }

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

            File.Move(temporary, RealPath(Path), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing tags is unfortunate; failing because of it would be worse.
        }
    }
    /// <summary>Where a state file really lives, following a link if it is one.</summary>
    /// <param name="path">The configured path.</param>
    /// <returns>The path to rename over.</returns>
    /// <remarks>
    /// <c>rename(2)</c> replaces a symbolic link rather than what it points at, so replacing the
    /// file in place would break the link and leave later changes accumulating in an untracked
    /// regular file — until the next re-install of the dotfiles put the stale copy back and took
    /// everything since with it. Keeping this file as a link into a dotfiles repository is a
    /// common enough arrangement that ranger has the same branch
    /// (<c>container/bookmarks.py:200-204</c>).
    /// </remarks>
    private static string RealPath(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>Whether a path can be written to the tag file at all.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="false"/> when storing it would corrupt the file.</returns>
    /// <remarks>
    /// The format is one entry per line — ranger's, so a tag file can be shared between the two —
    /// and a path containing a newline therefore cannot be represented. Written out it becomes
    /// two lines, and comes back as two tags on two paths that do not exist; the next save makes
    /// that permanent, and the real tag is gone.
    ///
    /// Refusing keeps the format readable by ranger. Escaping would fix the round trip properly
    /// and break that, which is a poor trade for a case this rare.
    /// </remarks>
    private static bool CanBeStored(string path) =>
        !path.Contains('\n', StringComparison.Ordinal) &&
        !path.Contains('\r', StringComparison.Ordinal);

}
