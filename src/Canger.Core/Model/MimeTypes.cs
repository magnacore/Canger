// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Frozen;

namespace Canger.Core.Model;

/// <summary>
/// Maps a filename extension to a media type.
/// </summary>
/// <remarks>
/// <para>
/// The extension is consulted before <c>file(1)</c>, which is the opposite of what seems sensible
/// and is what ranger does for a good reason: <c>file</c> inspects content, and content is often
/// ambiguous. An MP3 whose first bytes are an unusual tag reads as
/// <c>application/octet-stream</c>, which matches no audio rule, so the file falls through to
/// whatever the desktop considers a default handler. The name says <c>.mp3</c>; that is better
/// evidence than a header guess.
/// </para>
/// <para>
/// The table is read from the system's own <c>mime.types</c> files, so it agrees with everything
/// else on the machine, with a small built-in list behind it for systems that ship none.
/// </para>
/// </remarks>
public static class MimeTypes
{
    /// <summary>
    /// Where the system keeps its table, most specific first.
    /// </summary>
    /// <remarks>
    /// The same list Python's <c>mimetypes</c> module consults, which is what ranger ends up
    /// using, plus the user's own file.
    /// </remarks>
    private static readonly string[] KnownFiles =
    [
        "/etc/mime.types",
        "/etc/httpd/mime.types",
        "/etc/httpd/conf/mime.types",
        "/etc/apache/mime.types",
        "/etc/apache2/mime.types",
        "/usr/local/etc/httpd/conf/mime.types",
        "/usr/local/lib/netscape/mime.types",
    ];

    /// <summary>
    /// Enough of a table to be useful where the system ships none.
    /// </summary>
    /// <remarks>
    /// Deliberately short: it covers the kinds a file manager is asked to open, which is where
    /// getting this wrong is most visible.
    /// </remarks>
    private static readonly (string Extension, string Type)[] BuiltIn =
    [
        ("mp3", "audio/mpeg"), ("flac", "audio/flac"), ("wav", "audio/x-wav"),
        ("ogg", "audio/ogg"), ("oga", "audio/ogg"), ("opus", "audio/ogg"),
        ("m4a", "audio/mp4"), ("aac", "audio/aac"), ("wma", "audio/x-ms-wma"),

        ("mp4", "video/mp4"), ("mkv", "video/x-matroska"), ("avi", "video/x-msvideo"),
        ("mov", "video/quicktime"), ("webm", "video/webm"), ("wmv", "video/x-ms-wmv"),
        ("flv", "video/x-flv"), ("m4v", "video/mp4"), ("mpg", "video/mpeg"),
        ("mpeg", "video/mpeg"),

        ("jpg", "image/jpeg"), ("jpeg", "image/jpeg"), ("png", "image/png"),
        ("gif", "image/gif"), ("bmp", "image/bmp"), ("svg", "image/svg+xml"),
        ("webp", "image/webp"), ("tif", "image/tiff"), ("tiff", "image/tiff"),

        ("pdf", "application/pdf"), ("epub", "application/epub+zip"),
        ("zip", "application/zip"), ("gz", "application/gzip"),
        ("tar", "application/x-tar"), ("7z", "application/x-7z-compressed"),
        ("rar", "application/vnd.rar"),

        ("txt", "text/plain"), ("md", "text/markdown"), ("html", "text/html"),
        ("htm", "text/html"), ("css", "text/css"), ("csv", "text/csv"),
        ("json", "application/json"), ("xml", "text/xml"),
    ];

    private static readonly Lazy<FrozenDictionary<string, string>> Table = new(Load);

    /// <summary>
    /// The media type a filename implies.
    /// </summary>
    /// <param name="path">The file's name or path.</param>
    /// <returns>The type, or <see langword="null"/> when the extension is unknown.</returns>
    public static string? FromExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string extension = Path.GetExtension(path);

        if (extension.Length < 2)
        {
            return null;
        }

        return Table.Value.TryGetValue(extension[1..].ToLowerInvariant(), out string? type)
            ? type
            : null;
    }

    /// <summary>Reads the system tables, falling back to the built-in list.</summary>
    private static FrozenDictionary<string, string> Load()
    {
        Dictionary<string, string> table = new(StringComparer.Ordinal);

        // The built-in list goes in first so a system file can override it.
        foreach ((string extension, string type) in BuiltIn)
        {
            table[extension] = type;
        }

        foreach (string file in Files())
        {
            Read(file, table);
        }

        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Files()
    {
        foreach (string file in KnownFiles)
        {
            yield return file;
        }

        // The user's own file last, so it has the final say.
        yield return Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mime.types");
    }

    /// <summary>
    /// Reads one table.
    /// </summary>
    /// <remarks>
    /// The format is a media type followed by the extensions that mean it, separated by
    /// whitespace, with <c>#</c> starting a comment. Lines naming no extension are common and
    /// carry nothing.
    /// </remarks>
    private static void Read(string file, Dictionary<string, string> table)
    {
        try
        {
            if (!File.Exists(file))
            {
                return;
            }

            foreach (string line in File.ReadLines(file))
            {
                int comment = line.IndexOf('#', StringComparison.Ordinal);
                string text = comment < 0 ? line : line[..comment];

                string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 1; i < parts.Length; i++)
                {
                    table[parts[i].ToLowerInvariant()] = parts[0];
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable table simply contributes nothing.
        }
    }
}
