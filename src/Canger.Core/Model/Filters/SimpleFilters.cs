// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.FileSystem;

namespace Canger.Core.Model.Filters;

/// <summary>Shows only nodes whose displayed name matches a regular expression.</summary>
/// <param name="pattern">The pattern, as the user typed it.</param>
/// <param name="ignoreCase">Whether matching ignores case.</param>
public sealed class NameFilter(string pattern, bool ignoreCase = true) : IFileFilter
{
    private readonly Regex _regex = new(
        pattern,
        (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);

    /// <summary>The pattern as the user typed it.</summary>
    public string Pattern { get; } = pattern;

    /// <inheritdoc />
    public bool Accepts(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _regex.IsMatch(node.RelativePath);
    }

    /// <inheritdoc />
    public string Description => $"name {Pattern}";
}

/// <summary>
/// Shows only nodes of particular kinds, selected by the same letters the <c>filter_inode_type</c>
/// command uses: <c>d</c> for directories, <c>f</c> for files and <c>l</c> for links.
/// </summary>
/// <param name="types">Any combination of <c>d</c>, <c>f</c> and <c>l</c>.</param>
public sealed class InodeTypeFilter(string types) : IFileFilter
{
    /// <summary>The letters this filter accepts.</summary>
    public string Types { get; } = types;

    /// <inheritdoc />
    public bool Accepts(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (char type in Types)
        {
            bool matches = type switch
            {
                'd' => node.IsDirectory,
                // A symbolic link is not counted as a file, matching ranger, so that "f" and "l"
                // partition rather than overlap.
                'f' => node.Status is { Kind: FileKind.Regular } && !node.IsSymbolicLink,
                'l' => node.IsSymbolicLink,
                _ => false,
            };

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string Description => $"type {Types}";
}

/// <summary>Shows only the nodes named when the filter was created.</summary>
/// <remarks>
/// This is what <c>:narrow</c> produces: it freezes the current selection into a filter, so the
/// listing shows those entries and nothing else.
/// </remarks>
/// <param name="names">The displayed names to keep.</param>
public sealed class NarrowFilter(IEnumerable<string> names) : IFileFilter
{
    private readonly HashSet<string> _names = [.. names];

    /// <inheritdoc />
    public bool Accepts(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _names.Contains(node.RelativePath);
    }

    /// <inheritdoc />
    public string Description => $"narrow {_names.Count} entries";
}

/// <summary>Shows only nodes that pass a supplied test.</summary>
/// <param name="predicate">The test.</param>
/// <param name="description">How the filter describes itself.</param>
public sealed class PredicateFilter(Func<FsNode, bool> predicate, string description) : IFileFilter
{
    /// <inheritdoc />
    public bool Accepts(FsNode node) => predicate(node);

    /// <inheritdoc />
    public string Description => description;
}
