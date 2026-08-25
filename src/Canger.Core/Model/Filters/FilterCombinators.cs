// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model.Filters;

/// <summary>Shows nodes that pass every operand.</summary>
/// <param name="left">The first operand.</param>
/// <param name="right">The second operand.</param>
public sealed class AndFilter(IFileFilter left, IFileFilter right) : IFileFilter
{
    /// <inheritdoc />
    public bool Accepts(FsNode node) => left.Accepts(node) && right.Accepts(node);

    /// <inheritdoc />
    public string Description => $"({left.Description} AND {right.Description})";

    /// <inheritdoc />
    public IReadOnlyList<IFileFilter> Operands => [left, right];
}

/// <summary>Shows nodes that pass either operand.</summary>
/// <param name="left">The first operand.</param>
/// <param name="right">The second operand.</param>
public sealed class OrFilter(IFileFilter left, IFileFilter right) : IFileFilter
{
    /// <inheritdoc />
    public bool Accepts(FsNode node) => left.Accepts(node) || right.Accepts(node);

    /// <inheritdoc />
    public string Description => $"({left.Description} OR {right.Description})";

    /// <inheritdoc />
    public IReadOnlyList<IFileFilter> Operands => [left, right];
}

/// <summary>Shows nodes the operand rejects.</summary>
/// <param name="operand">The filter to invert.</param>
public sealed class NotFilter(IFileFilter operand) : IFileFilter
{
    /// <inheritdoc />
    public bool Accepts(FsNode node) => !operand.Accepts(node);

    /// <inheritdoc />
    public string Description => $"NOT {operand.Description}";

    /// <inheritdoc />
    public IReadOnlyList<IFileFilter> Operands => [operand];
}
