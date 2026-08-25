// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;

namespace Canger.Core.Model.Filters;

/// <summary>
/// A stack of filters that combine to decide what a directory shows.
/// </summary>
/// <remarks>
/// <para>
/// Filters accumulate, and every one on the stack must accept a node for it to appear. The
/// combinators work in reverse Polish order: pushing <c>and</c> pops the two filters below it and
/// pushes the combination in their place. That is what lets the user build
/// "documents that are not backups" out of two simple filters and one combinator, entirely from
/// the keyboard, without any expression syntax.
/// </para>
/// <para>
/// The design follows ranger's <c>core/filter_stack.py</c>, including the way combinators consume
/// their operands rather than sitting alongside them.
/// </para>
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
                 Justification = "filter_stack is the user-facing command name, and the type " +
                                 "deliberately matches it. Renaming would make the code harder " +
                                 "to relate to the command it implements.")]
public sealed class FilterStack
{
    private readonly List<IFileFilter> _filters = [];

    /// <summary>The filters currently applied, oldest first.</summary>
    public IReadOnlyList<IFileFilter> Filters => _filters;

    /// <summary>Whether the stack rejects nothing.</summary>
    public bool IsEmpty => _filters.Count == 0;

    /// <summary>Adds a filter on top of the stack.</summary>
    /// <param name="filter">The filter to add.</param>
    public void Push(IFileFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filters.Add(filter);
    }

    /// <summary>Removes the most recently added filter.</summary>
    /// <returns>The removed filter, or <see langword="null"/> when the stack was empty.</returns>
    public IFileFilter? Pop()
    {
        if (_filters.Count == 0)
        {
            return null;
        }

        IFileFilter top = _filters[^1];
        _filters.RemoveAt(_filters.Count - 1);
        return top;
    }

    /// <summary>Removes every filter.</summary>
    public void Clear() => _filters.Clear();

    /// <summary>
    /// Combines the top two filters with a binary combinator, replacing them with the result.
    /// </summary>
    /// <param name="combine">Builds the combined filter.</param>
    /// <returns><see langword="true"/> when there were two filters to combine.</returns>
    public bool Combine(Func<IFileFilter, IFileFilter, IFileFilter> combine)
    {
        ArgumentNullException.ThrowIfNull(combine);

        if (_filters.Count < 2)
        {
            return false;
        }

        IFileFilter right = Pop()!;
        IFileFilter left = Pop()!;
        Push(combine(left, right));
        return true;
    }

    /// <summary>Replaces the top filter with its inverse.</summary>
    /// <returns><see langword="true"/> when there was a filter to invert.</returns>
    public bool Negate()
    {
        if (_filters.Count == 0)
        {
            return false;
        }

        Push(new NotFilter(Pop()!));
        return true;
    }

    /// <summary>
    /// Replaces the top filter with the filters it was built from, undoing one combination.
    /// </summary>
    /// <returns><see langword="true"/> when the top filter was a combination.</returns>
    public bool Decompose()
    {
        if (_filters.Count == 0 || _filters[^1].Operands.Count == 0)
        {
            return false;
        }

        IFileFilter top = Pop()!;
        foreach (IFileFilter operand in top.Operands)
        {
            Push(operand);
        }

        return true;
    }

    /// <summary>
    /// Moves filters around the stack, so a combinator can be applied to a different pair.
    /// </summary>
    /// <param name="count">How many places to rotate. Positive moves the top to the bottom.</param>
    public void Rotate(int count = 1)
    {
        if (_filters.Count < 2)
        {
            return;
        }

        int steps = ((count % _filters.Count) + _filters.Count) % _filters.Count;
        for (int i = 0; i < steps; i++)
        {
            IFileFilter top = Pop()!;
            _filters.Insert(0, top);
        }
    }

    /// <summary>Whether a node passes every filter on the stack.</summary>
    /// <param name="node">The node to test.</param>
    /// <returns><see langword="true"/> when nothing rejects it.</returns>
    public bool Accepts(FsNode node)
    {
        foreach (IFileFilter filter in _filters)
        {
            if (!filter.Accepts(node))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>How the stack reads when displayed, bottom first.</summary>
    /// <returns>One description per filter.</returns>
    public IReadOnlyList<string> Describe() => [.. _filters.Select(f => f.Description)];
}
