// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model;

/// <summary>Which property a directory listing is ordered by.</summary>
public enum SortKey
{
    /// <summary>By name, with embedded numbers ordered by value. The default.</summary>
    Natural,

    /// <summary>By name, character by character.</summary>
    Basename,

    /// <summary>Largest first.</summary>
    Size,

    /// <summary>Most recently modified first.</summary>
    ModificationTime,

    /// <summary>Most recently changed first.</summary>
    ChangeTime,

    /// <summary>Most recently accessed first.</summary>
    AccessTime,

    /// <summary>By file type.</summary>
    Type,

    /// <summary>By extension.</summary>
    Extension,

    /// <summary>Shuffled.</summary>
    Random,
}

/// <summary>
/// How a directory listing should be ordered.
/// </summary>
/// <param name="Key">Which property to order by.</param>
/// <param name="Reverse">Whether to invert the order.</param>
/// <param name="DirectoriesFirst">Whether directories are grouped ahead of files.</param>
/// <param name="CaseInsensitive">Whether name ordering ignores case.</param>
/// <param name="UseUnicodeCollation">
/// Whether names are ordered by the current culture's collation rather than by code point.
/// </param>
/// <remarks>
/// Deliberately a record class rather than a record struct. A struct's parameterless
/// construction zero-initialises and does <em>not</em> apply a primary constructor's default
/// values, so <c>new SortOrder()</c> would silently mean "files before directories, case
/// sensitive" — the opposite of the intended defaults, and invisible until a listing came back
/// in the wrong order.
/// </remarks>
public sealed record SortOrder(
    SortKey Key = SortKey.Natural,
    bool Reverse = false,
    bool DirectoriesFirst = true,
    bool CaseInsensitive = true,
    bool UseUnicodeCollation = false)
{
    /// <summary>Canger's default ordering.</summary>
    public static SortOrder Default => new();

    /// <summary>Parses the name used by the <c>sort</c> setting.</summary>
    /// <param name="name">The setting value, for example <c>natural</c> or <c>mtime</c>.</param>
    /// <returns>The key, or <see cref="SortKey.Natural"/> when the name is unrecognised.</returns>
    public static SortKey ParseKey(string name) => name switch
    {
        "basename" => SortKey.Basename,
        "size" => SortKey.Size,
        "mtime" => SortKey.ModificationTime,
        "ctime" => SortKey.ChangeTime,
        "atime" => SortKey.AccessTime,
        "type" => SortKey.Type,
        "extension" => SortKey.Extension,
        "random" => SortKey.Random,
        _ => SortKey.Natural,
    };
}
