// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model.Filters;

/// <summary>
/// Decides whether a node is shown.
/// </summary>
/// <remarks>
/// Filters are composable: several apply at once and all must accept, and the combinators in this
/// namespace build compound filters out of simpler ones. Every filter can describe itself, which
/// is what <c>:filter_stack show</c> displays and what <c>decompose</c> takes apart.
/// </remarks>
public interface IFileFilter
{
    /// <summary>Whether a node passes this filter.</summary>
    /// <param name="node">The node to test.</param>
    /// <returns><see langword="true"/> when the node should be shown.</returns>
    bool Accepts(FsNode node);

    /// <summary>How this filter is written when the stack is displayed.</summary>
    string Description { get; }

    /// <summary>
    /// The filters this one was built from, for <c>:filter_stack decompose</c>. Empty for a
    /// filter that is not a combinator.
    /// </summary>
    IReadOnlyList<IFileFilter> Operands => [];
}
