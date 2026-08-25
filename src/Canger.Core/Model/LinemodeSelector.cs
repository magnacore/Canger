// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.State;

namespace Canger.Core.Model;

/// <summary>How a <see cref="LinemodeRule"/> decides whether it applies.</summary>
public enum LinemodeScope
{
    /// <summary>Applies to everything.</summary>
    Always,

    /// <summary>Applies where a regular expression matches the entry's path.</summary>
    Path,

    /// <summary>Applies where the entry carries one of a set of tags.</summary>
    Tag,
}

/// <summary>
/// One <c>default_linemode</c> directive.
/// </summary>
/// <param name="Scope">What the rule tests.</param>
/// <param name="Pattern">The path pattern, for <see cref="LinemodeScope.Path"/>.</param>
/// <param name="Tags">The tag characters, for <see cref="LinemodeScope.Tag"/>.</param>
/// <param name="Linemode">The mode to use where the rule applies.</param>
public sealed record LinemodeRule(
    LinemodeScope Scope,
    Regex? Pattern,
    string? Tags,
    string Linemode);

/// <summary>
/// Works out which linemode an entry is drawn with.
/// </summary>
/// <remarks>
/// Rules are consulted newest first, so a later <c>default_linemode</c> in the configuration wins
/// over an earlier one — which is what makes a general rule followed by a narrow one read the way
/// it looks. An explicit <see cref="FsNode.LinemodeOverride"/> beats every rule.
/// </remarks>
/// <param name="registry">Where mode names are resolved.</param>
/// <param name="tags">The tag store, consulted by <see cref="LinemodeScope.Tag"/> rules.</param>
/// <param name="metadata">
/// Where annotations come from, for the modes that render them. When absent those modes see
/// nothing recorded and so fall back to the default.
/// </param>
public sealed class LinemodeSelector(
    LinemodeRegistry registry,
    Tags? tags = null,
    MetadataManager? metadata = null)
{
    private readonly List<LinemodeRule> _rules = [];

    /// <summary>Where mode names are resolved, and where plugins register their own.</summary>
    public LinemodeRegistry Registry { get; } = registry;

    /// <summary>The tag store consulted by tag-scoped rules.</summary>
    public Tags? Tags { get; set; } = tags;

    /// <summary>Where annotations come from, for the modes that render them.</summary>
    public MetadataManager? Metadata { get; set; } = metadata;

    /// <summary>The rules, newest first.</summary>
    public IReadOnlyList<LinemodeRule> Rules => _rules;

    /// <summary>Whether sizes use binary prefixes, as <c>binary_size_prefix</c> asks.</summary>
    public bool BinaryPrefix { get; set; }

    /// <summary>
    /// Whether directories that have not been opened are read to count their entries, as
    /// <c>automatically_count_files</c> asks.
    /// </summary>
    public bool CountFiles { get; set; } = true;

    /// <summary>
    /// Adds a rule, which takes precedence over everything added before it.
    /// </summary>
    /// <param name="rule">The rule.</param>
    public void Add(LinemodeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Insert(0, rule);
    }

    /// <summary>Forgets every rule.</summary>
    public void Clear() => _rules.Clear();

    /// <summary>
    /// Chooses the linemode for an entry, and reads whatever annotations it needs.
    /// </summary>
    /// <param name="node">The entry.</param>
    /// <returns>The mode and the metadata to render it with.</returns>
    /// <remarks>
    /// A mode that needs annotations an entry does not have gives way to the default for that
    /// entry alone, so a half-annotated library still lists cleanly.
    /// </remarks>
    public (ILinemode Mode, FileMetadata Metadata) Resolve(FsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        ILinemode mode = Choose(node);

        if (!mode.UsesMetadata)
        {
            return (mode, FileMetadata.Empty);
        }

        FileMetadata annotations = Metadata?.Get(node.Path) ?? FileMetadata.Empty;

        return mode.RequiredMetadata.All(annotations.Has)
            ? (mode, annotations)
            : (Registry.Default, annotations);
    }

    /// <summary>Applies the override and then the rules, without regard to metadata.</summary>
    private ILinemode Choose(FsNode node)
    {
        if (Registry.Find(node.LinemodeOverride) is { } overridden)
        {
            return overridden;
        }

        foreach (LinemodeRule rule in _rules)
        {
            if (Registry.Find(rule.Linemode) is { } mode && Applies(rule, node))
            {
                return mode;
            }
        }

        return Registry.Default;
    }

    /// <summary>Builds the context a linemode is rendered with.</summary>
    /// <param name="now">The moment "recent" is judged against.</param>
    /// <returns>The context.</returns>
    public LinemodeContext ContextAt(DateTimeOffset now) =>
        new(now, BinaryPrefix, CountFiles);

    private bool Applies(LinemodeRule rule, FsNode node) => rule.Scope switch
    {
        LinemodeScope.Always => true,
        LinemodeScope.Path => rule.Pattern?.IsMatch(node.Path) == true,
        LinemodeScope.Tag => rule.Tags is { } wanted
                             && Tags?.TagOf(node.RealPath) is { } tag
                             && wanted.Contains(tag, StringComparison.Ordinal),
        _ => false,
    };
}
