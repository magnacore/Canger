// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Canger.Core.FileSystem;

namespace Canger.Core.State;

/// <summary>
/// What a <c>.metadata.json</c> file records about one entry.
/// </summary>
/// <remarks>
/// The keys are free-form, so this is a dictionary rather than a fixed record; the named
/// properties are only the ones the shipped linemodes and commands read. A missing key reads as
/// an empty string, which is what makes <c>metatitle</c>'s fallback test a simple emptiness check.
/// </remarks>
public sealed class FileMetadata
{
    /// <summary>An entry with nothing recorded about it.</summary>
    public static FileMetadata Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly Dictionary<string, string> _fields;

    /// <summary>Creates metadata over a set of fields.</summary>
    /// <param name="fields">The fields, which are copied.</param>
    public FileMetadata(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<string, string>(fields, StringComparer.Ordinal);
    }

    /// <summary>Every recorded key, in order.</summary>
    public IEnumerable<string> Keys => _fields.Keys.Order(StringComparer.Ordinal);

    /// <summary>Whether nothing at all is recorded.</summary>
    public bool IsEmpty => _fields.Count == 0;

    /// <summary>Reads a field.</summary>
    /// <param name="key">The field name.</param>
    /// <returns>The value, or an empty string when it is not recorded.</returns>
    public string this[string key] =>
        _fields.TryGetValue(key, out string? value) ? value : string.Empty;

    /// <summary>Whether a field is recorded and not empty.</summary>
    /// <param name="key">The field name.</param>
    /// <returns><see langword="true"/> when there is something to show.</returns>
    public bool Has(string key) => this[key].Length > 0;

    /// <summary>The work's title.</summary>
    public string Title => this["title"];

    /// <summary>The year, as recorded.</summary>
    public string Year => this["year"];

    /// <summary>The authors, comma-separated as recorded.</summary>
    public string Authors => this["authors"];

    /// <summary>The fields, for writing back out.</summary>
    internal IReadOnlyDictionary<string, string> Fields => _fields;
}

/// <summary>
/// Reads and writes the <c>.metadata.json</c> files that describe entries.
/// </summary>
/// <remarks>
/// The database is per-directory by design: it travels with the files it describes, so a moved or
/// shared folder keeps its annotations. With <c>metadata_deep_search</c> the search continues up
/// the tree, which lets one file at the top of a library annotate everything beneath it.
/// </remarks>
/// <param name="fileSystem">Where the files are read and written.</param>
public sealed class MetadataManager(IFileSystem fileSystem)
{
    /// <summary>The name of the file holding the database.</summary>
    public const string FileName = ".metadata.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, FileMetadata> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>?> _files =
        new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    /// <summary>
    /// Whether to consult <c>.metadata.json</c> files in ancestor directories too.
    /// </summary>
    public bool DeepSearch { get; set; }

    /// <summary>Forgets everything read so far, so the next read sees the files afresh.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
            _files.Clear();
        }
    }

    /// <summary>
    /// Reads what is recorded about an entry.
    /// </summary>
    /// <param name="path">The entry's path.</param>
    /// <returns>The metadata, which is <see cref="FileMetadata.Empty"/> when there is none.</returns>
    public FileMetadata Get(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out FileMetadata? cached))
            {
                return cached;
            }

            FileMetadata found = Lookup(path) is { } fields ? new FileMetadata(fields) : FileMetadata.Empty;
            _entries[path] = found;
            return found;
        }
    }

    /// <summary>
    /// Records fields against an entry, writing them to the database beside it.
    /// </summary>
    /// <param name="path">The entry's path.</param>
    /// <param name="updates">
    /// The fields to change. An empty value removes the field, which is how <c>:meta</c> deletes.
    /// </param>
    public void Set(string path, IReadOnlyDictionary<string, string> updates)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(updates);

        lock (_gate)
        {
            string target = DeepSearch ? OwningFile(path) : MetadataFiles(path).First();

            // A database that could not be read is not an empty one. Treating it as empty and
            // then writing replaced a file of hundreds of annotations with a single entry — the
            // user's own notes, sitting among their own files. Ranger raises rather than
            // swallowing, so its write is never reached (`core/metadata.py:116-125`).
            if (Load(target) is not { } entries)
            {
                return;
            }

            // Existing fields are kept: an update names only what changes.
            string key = entries.ContainsKey(path) ? path : System.IO.Path.GetFileName(path);
            if (!entries.TryGetValue(key, out Dictionary<string, string>? entry))
            {
                key = path;
                entry = [];
                entries[key] = entry;
            }

            foreach ((string field, string value) in updates)
            {
                if (value.Length == 0)
                {
                    entry.Remove(field);
                }
                else
                {
                    entry[field] = value;
                }
            }

            Write(target, entries);

            // Both caches are now stale for this entry.
            _entries.Remove(path);
        }
    }

    /// <summary>Finds the fields recorded for a path in the first database that mentions it.</summary>
    private Dictionary<string, string>? Lookup(string path)
    {
        string name = System.IO.Path.GetFileName(path);

        foreach (string metadataFile in MetadataFiles(path))
        {
            // Reading only: a database that could not be read simply contributes nothing.
            if (Load(metadataFile) is not { } entries)
            {
                continue;
            }

            // A full path wins over a bare name, so a deep-search database can single out one
            // file among many that share a name.
            if (entries.TryGetValue(path, out Dictionary<string, string>? entry)
                || entries.TryGetValue(name, out entry))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Which database file already mentions an entry, or the nearest one if none does.</summary>
    private string OwningFile(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        string? first = null;

        foreach (string metadataFile in MetadataFiles(path))
        {
            first ??= metadataFile;

            if (Load(metadataFile) is not { } entries)
            {
                continue;
            }

            if (entries.ContainsKey(path) || entries.ContainsKey(name))
            {
                return metadataFile;
            }
        }

        return first ?? System.IO.Path.Join(System.IO.Path.GetDirectoryName(path), FileName);
    }

    /// <summary>
    /// The databases that could describe an entry: the one beside it, then each ancestor's when
    /// deep search is on, nearest first.
    /// </summary>
    private IEnumerable<string> MetadataFiles(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));

        if (directory is null)
        {
            yield break;
        }

        yield return System.IO.Path.Join(directory, FileName);

        if (!DeepSearch)
        {
            yield break;
        }

        DirectoryInfo? ancestor = new DirectoryInfo(directory).Parent;

        while (ancestor is not null)
        {
            yield return System.IO.Path.Join(ancestor.FullName, FileName);
            ancestor = ancestor.Parent;
        }
    }

    /// <summary>Reads a database, remembering it so a listing does not reread it per row.</summary>
    /// <returns>
    /// The database, or <see langword="null"/> when it could not be read. A caller that only
    /// looks things up can treat that as empty; a caller that intends to write must not.
    /// </returns>
    private Dictionary<string, Dictionary<string, string>>? Load(string metadataFile)
    {
        if (_files.TryGetValue(metadataFile, out Dictionary<string, Dictionary<string, string>>? cached))
        {
            return cached;
        }

        // A failed read is remembered as null, so a listing does not retry it for every row and
        // so `Set` can tell "nothing recorded" from "could not be read".
        Dictionary<string, Dictionary<string, string>>? entries = Read(metadataFile);
        _files[metadataFile] = entries;
        return entries;
    }

    /// <returns>
    /// The database, or <see langword="null"/> when the file exists and could not be read — which
    /// is not the same as its being absent.
    /// </returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "A malformed or unreadable database must not stop a listing "
                                   + "from being drawn; it contributes nothing and is not written.")]
    private Dictionary<string, Dictionary<string, string>>? Read(string metadataFile)
    {
        try
        {
            if (!fileSystem.Exists(metadataFile))
            {
                return [];
            }

            using Stream stream = fileSystem.OpenRead(metadataFile);

            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
                   ?? [];
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Replaces a database, without a moment where it is neither old nor new.</summary>
    /// <param name="metadataFile">The database to replace.</param>
    /// <param name="entries">What it should now contain.</param>
    /// <remarks>
    /// Written beside the real file and renamed over it, as <c>Tags</c>, <c>Bookmarks</c> and the
    /// console history all do. This one was left writing in place, which truncates at open — so a
    /// crash, a full disk, or a closed terminal between the truncate and the flush left a
    /// half-written or empty database. It sits among the user's own files rather than in Canger's
    /// state directory, which makes it the worst place to have kept that.
    /// </remarks>
    private void Write(string metadataFile, Dictionary<string, Dictionary<string, string>> entries)
    {
        string temporary = metadataFile + ".new";

        using (Stream stream = fileSystem.OpenWrite(temporary))
        {
            JsonSerializer.Serialize(stream, entries, WriteOptions);
        }

        fileSystem.Replace(temporary, metadataFile);
    }
}
