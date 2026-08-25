// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Frozen;

namespace Canger.Core.Model;

/// <summary>
/// What kind of thing a file is, judged from its name.
/// </summary>
/// <remarks>
/// Flags rather than an enumeration of one choice, because the categories genuinely overlap: an
/// <c>.svg</c> is both an image and a document, and ranger's own tests are independent for the
/// same reason.
/// </remarks>
[Flags]
public enum FileCategory
{
    /// <summary>Nothing recognised.</summary>
    None = 0,

    /// <summary>An image.</summary>
    Image = 1,

    /// <summary>A video.</summary>
    Video = 2,

    /// <summary>Audio.</summary>
    Audio = 4,

    /// <summary>An archive.</summary>
    Container = 8,

    /// <summary>A document.</summary>
    Document = 16,

    /// <summary>Any of the three kinds of media, which is what the colourscheme asks about.</summary>
    Media = Image | Video | Audio,
}

/// <summary>
/// The extension lists that decide the categories a media type cannot.
/// </summary>
/// <remarks>
/// Taken verbatim from <c>container/fsobject.py:31-40</c>. They are ranger's judgement rather
/// than anything canonical — <c>.gz</c> counts as an archive and <c>.md</c> as a document — and
/// matching them exactly is the point, since the whole purpose here is that a listing looks the
/// same as it does in ranger.
/// </remarks>
public static class FileCategories
{
    /// <summary>Extensions ranger treats as archives.</summary>
    public static readonly FrozenSet<string> ContainerExtensions =
        new[]
        {
            "7z", "ace", "ar", "arc", "bz", "bz2", "cab", "cpio", "cpt", "deb", "dgc", "dmg",
            "gz", "iso", "jar", "msi", "pkg", "rar", "shar", "tar", "tbz", "tgz", "txz", "xar",
            "xpi", "xz", "zip",
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Extensions ranger treats as documents.</summary>
    public static readonly FrozenSet<string> DocumentExtensions =
        new[]
        {
            "cbr", "cbz", "cfg", "css", "cvs", "djvu", "doc", "docx", "gnm", "gnumeric", "htm",
            "html", "md", "odf", "odg", "odp", "ods", "odt", "pdf", "pod", "ps", "rtf", "sxc",
            "txt", "xls", "xlw", "xml", "xslx",
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whole filenames ranger treats as documents, whatever their extension.</summary>
    public static readonly FrozenSet<string> DocumentBasenames =
        new[]
        {
            "bugs", "changelog", "copying", "credits", "hacking", "help", "install", "license",
            "readme", "todo",
        }.ToFrozenSet(StringComparer.Ordinal);
}
