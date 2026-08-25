// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.State;

/// <summary>
/// Directories the user can jump to by pressing a single key.
/// </summary>
/// <remarks>
/// <para>
/// Two keys are special. <c>'</c> always holds the directory you were in before this one, so
/// pressing it twice returns you to where you started; the backtick is an alias for it, because
/// both keys are conventionally used for the same thing and which one is easier to reach depends
/// on the keyboard.
/// </para>
/// <para>
/// Saving merges rather than overwrites. Two Canger windows open at once would otherwise mean
/// whichever exited last silently discarded the other's bookmarks.
/// </para>
/// </remarks>
public sealed class Bookmarks(IFileSystem fileSystem, string path)
{
    /// <summary>The key holding the previous directory.</summary>
    public const char PreviousDirectory = '\'';

    /// <summary>An alias for <see cref="PreviousDirectory"/>.</summary>
    public const char PreviousDirectoryAlias = '`';

    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly Dictionary<char, string> _entries = [];

    /// <summary>What was on disk when this was last read, for the three-way merge.</summary>
    private Dictionary<char, string> _asLoaded = [];

    /// <summary>Where the bookmarks are kept.</summary>
    public string Path { get; } = path;

    /// <summary>Whether saving happens automatically after every change.</summary>
    public bool AutoSave { get; set; } = true;

    /// <summary>
    /// Whether the previous-directory bookmark is written to disk.
    /// </summary>
    /// <remarks>
    /// It changes on every navigation, so persisting it means rewriting the file constantly for
    /// a value that means nothing in the next session.
    /// </remarks>
    public bool SaveBacktickBookmark { get; set; } = true;

    /// <summary>The bookmarks, by key.</summary>
    public IReadOnlyDictionary<char, string> Entries => _entries;

    /// <summary>Whether a key may hold a bookmark.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it is a letter, a digit, or one of the two aliases.</returns>
    public static bool IsValidKey(char key) =>
        char.IsAsciiLetterOrDigit(key) || key is PreviousDirectory or PreviousDirectoryAlias;

    /// <summary>Resolves the backtick alias, so both keys reach the same bookmark.</summary>
    /// <param name="key">The key as pressed.</param>
    /// <returns>The key the bookmark is stored under.</returns>
    public static char Normalize(char key) =>
        key == PreviousDirectoryAlias ? PreviousDirectory : key;

    /// <summary>Looks up where a key points.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The directory, or <see langword="null"/> when the key holds nothing.</returns>
    public string? Get(char key) =>
        _entries.TryGetValue(Normalize(key), out string? value) ? value : null;

    /// <summary>Points a key at a directory.</summary>
    /// <param name="key">The key.</param>
    /// <param name="directory">Where it should lead.</param>
    public void Set(char key, string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (!IsValidKey(key))
        {
            return;
        }

        _entries[Normalize(key)] = directory;

        if (AutoSave)
        {
            Save();
        }
    }

    /// <summary>Clears a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it held something.</returns>
    public bool Remove(char key)
    {
        bool removed = _entries.Remove(Normalize(key));

        if (removed && AutoSave)
        {
            Save();
        }

        return removed;
    }

    /// <summary>
    /// Records where the user just came from, under the previous-directory key.
    /// </summary>
    /// <param name="directory">The directory being left.</param>
    public void RememberPrevious(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _entries[PreviousDirectory] = directory;

        // Deliberately not saved here: this changes on every navigation, and writing the file
        // each time would be constant churn for a value the next session will not want.
    }

    /// <summary>Drops bookmarks whose directory no longer exists.</summary>
    /// <returns>How many were dropped.</returns>
    public int RemoveStale()
    {
        List<char> stale =
        [
            .. _entries.Where(e => !_fileSystem.DirectoryExists(e.Value)).Select(e => e.Key),
        ];

        foreach (char key in stale)
        {
            _entries.Remove(key);
        }

        return stale.Count;
    }

    /// <summary>Reads the bookmarks from disk, replacing whatever is held.</summary>
    public void Load()
    {
        // A read that failed leaves nothing loaded, which is safe here: `Save` reads again and
        // refuses when that read fails too, so an unreadable file is never written over. Should
        // it become readable in between, the merge sees the real contents and this instance —
        // having recorded no bookmarks as loaded — deletes none of them.
        Dictionary<char, string> read = ReadFile() ?? [];

        _entries.Clear();
        _asLoaded = read;

        foreach ((char key, string value) in read)
        {
            _entries[key] = value;
        }
    }

    /// <summary>
    /// Writes the bookmarks, keeping changes another instance made in the meantime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is re-read and compared three ways: what is on disk now, what was there when this
    /// instance read it, and what this instance holds. A key this instance did not touch keeps
    /// whatever is on disk; a key it did touch wins.
    /// </para>
    /// <para>
    /// Without this, running two Canger windows would mean the second to exit silently discarded
    /// every bookmark the first had added.
    /// </para>
    /// </remarks>
    public void Save()
    {
        // A read that failed is not an empty file, and the difference decides whether the user
        // keeps their bookmarks. The merge below re-adds only the keys *this instance changed*,
        // so merging onto an empty dictionary silently drops every bookmark the user did not
        // happen to touch this session — and then writes the result. Ranger guards the same way:
        // `_load_dict` returns None on failure and `update()` returns without writing
        // (`container/bookmarks.py:222-245`).
        if (ReadFile() is not { } onDisk)
        {
            return;
        }

        Dictionary<char, string> merged = new(onDisk);

        // Keys this instance changed or added.
        foreach ((char key, string value) in _entries)
        {
            if (!_asLoaded.TryGetValue(key, out string? original) ||
                !string.Equals(original, value, StringComparison.Ordinal))
            {
                merged[key] = value;
            }
        }

        // Keys this instance deleted.
        foreach (char key in _asLoaded.Keys.Where(k => !_entries.ContainsKey(k)))
        {
            merged.Remove(key);
        }

        if (!SaveBacktickBookmark)
        {
            merged.Remove(PreviousDirectory);
        }

        WriteFile(merged);
        _asLoaded = merged;
    }

    /// <summary>Reads the file, or reports that it could not be read.</summary>
    /// <returns>
    /// The bookmarks on disk, or <see langword="null"/> when the file exists and could not be
    /// read — which is not the same as its being empty, and must not be treated as if it were.
    /// </returns>
    private Dictionary<char, string>? ReadFile()
    {
        Dictionary<char, string> entries = [];

        try
        {
            if (!File.Exists(Path))
            {
                return entries;
            }

            foreach (string line in File.ReadLines(Path))
            {
                // One key, a colon, then the path. A path may contain colons, so only the first
                // one separates.
                if (line.Length >= 3 && line[1] == ':' && IsValidKey(line[0]))
                {
                    entries[Normalize(line[0])] = line[2..];
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable bookmarks file should not stop Canger starting — but it must stop
            // Canger saving, or the next save is what destroys it.
            return null;
        }

        return entries;
    }

    /// <summary>
    /// Writes the file through a temporary copy, so an interrupted write cannot destroy it.
    /// </summary>
    private void WriteFile(Dictionary<char, string> entries)
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
                entries.OrderBy(e => e.Key).Select(e => $"{e.Key}:{e.Value}"));

            File.Move(temporary, RealPath(Path), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing bookmarks is unfortunate; failing to exit because of it would be worse.
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

}
