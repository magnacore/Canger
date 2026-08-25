// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>
/// One node in a key map: either a command to run, or a branch waiting for the next key.
/// </summary>
/// <remarks>
/// <para>
/// A node is never both. That is not a simplification but the actual behaviour being reproduced:
/// ranger stores leaves and branches in the same structure, so binding <c>gg</c> after <c>g</c>
/// replaces the <c>g</c> command with a branch and the <c>g</c> binding is gone, while binding
/// <c>g</c> after <c>gg</c> replaces the whole branch with a command. Configuration files are
/// therefore order-sensitive, and Canger applies them strictly top to bottom.
/// </para>
/// <para>
/// The one thing that looks like an exception is <c>&lt;bg&gt;</c>: a branch may carry a
/// <see cref="KeyCodes.PassiveAction"/> child whose command fires on arrival at the branch while
/// the branch keeps waiting for more keys. That is an ordinary child, not a second role for the
/// node.
/// </para>
/// </remarks>
public sealed class KeyMapNode
{
    private readonly Dictionary<int, KeyMapNode>? _children;

    private KeyMapNode(string command) => Command = command;

    private KeyMapNode(Dictionary<int, KeyMapNode> children) => _children = children;

    /// <summary>The command to run, when this node is a leaf.</summary>
    public string? Command { get; }

    /// <summary>Whether this node is a command rather than a branch.</summary>
    public bool IsLeaf => Command is not null;

    /// <summary>The keys this branch accepts. Empty for a leaf.</summary>
    public IReadOnlyCollection<int> Keys =>
        _children?.Keys ?? (IReadOnlyCollection<int>)[];

    /// <summary>
    /// The command that fires on arrival at this branch without ending the sequence, when one is
    /// bound with <c>&lt;bg&gt;</c>.
    /// </summary>
    public string? PassiveCommand =>
        _children?.GetValueOrDefault(KeyCodes.PassiveAction)?.Command;

    /// <summary>Creates a leaf node carrying a command.</summary>
    /// <param name="command">The command to run.</param>
    /// <returns>The node.</returns>
    public static KeyMapNode Leaf(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new KeyMapNode(command);
    }

    /// <summary>Creates an empty branch node.</summary>
    /// <returns>The node.</returns>
    public static KeyMapNode Branch() => new([]);

    /// <summary>Follows one key out of this branch.</summary>
    /// <param name="key">The key code.</param>
    /// <returns>The child node, or <see langword="null"/> when the key is not bound here.</returns>
    public KeyMapNode? Child(int key) => _children?.GetValueOrDefault(key);

    /// <summary>Whether this branch has a child for a key.</summary>
    /// <param name="key">The key code.</param>
    /// <returns><see langword="true"/> when the key is bound here.</returns>
    public bool Has(int key) => _children?.ContainsKey(key) ?? false;

    /// <summary>Whether this branch has no children left.</summary>
    public bool IsEmptyBranch => !IsLeaf && _children!.Count == 0;

    /// <summary>Every key and child of this branch, for hints and for copying.</summary>
    public IEnumerable<KeyValuePair<int, KeyMapNode>> Children =>
        _children ?? Enumerable.Empty<KeyValuePair<int, KeyMapNode>>();

    /// <summary>Attaches a child, replacing whatever was bound to that key.</summary>
    /// <param name="key">The key code.</param>
    /// <param name="node">The child node.</param>
    /// <exception cref="InvalidOperationException">This node is a leaf.</exception>
    internal void Attach(int key, KeyMapNode node)
    {
        if (_children is null)
        {
            throw new InvalidOperationException("Cannot attach a child to a leaf node.");
        }

        _children[key] = node;
    }

    /// <summary>Removes a child.</summary>
    /// <param name="key">The key code.</param>
    /// <returns><see langword="true"/> when a child was removed.</returns>
    internal bool Detach(int key) => _children?.Remove(key) ?? false;

    /// <summary>
    /// Produces an independent copy of this node and everything below it.
    /// </summary>
    /// <remarks>
    /// <c>copymap</c> duplicates whatever sits at the source path at the moment it runs, so the
    /// copy must not alias the original — later edits to one must not disturb the other.
    /// </remarks>
    /// <returns>The copy.</returns>
    internal KeyMapNode DeepCopy()
    {
        if (IsLeaf)
        {
            return Leaf(Command!);
        }

        KeyMapNode copy = Branch();
        foreach ((int key, KeyMapNode child) in _children!)
        {
            copy.Attach(key, child.DeepCopy());
        }

        return copy;
    }
}
