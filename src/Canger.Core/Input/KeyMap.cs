// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>
/// The key bindings for one context, held as a trie of key codes.
/// </summary>
/// <remarks>
/// Canger keeps four contexts — browser, console, pager and task view — each an independent
/// map, matching ranger's <c>map</c>, <c>cmap</c>, <c>pmap</c> and <c>tmap</c> directives.
/// </remarks>
public sealed class KeyMap
{
    /// <summary>The root of the trie.</summary>
    public KeyMapNode Root { get; private set; } = KeyMapNode.Branch();

    /// <summary>
    /// Binds a key sequence to a command, replacing anything already bound at that path or
    /// below it.
    /// </summary>
    /// <param name="keys">The key codes, from <see cref="KeyBindingParser.Parse"/>.</param>
    /// <param name="command">The command to run.</param>
    public void Bind(IReadOnlyList<int> keys, string command)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(command);

        if (keys.Count == 0)
        {
            return;
        }

        KeyMapNode node = Root;

        for (int i = 0; i < keys.Count - 1; i++)
        {
            KeyMapNode? child = node.Child(keys[i]);

            // A command bound at a prefix is replaced by the branch that now runs through it.
            // This is why order in a configuration file matters.
            if (child is null || child.IsLeaf)
            {
                child = KeyMapNode.Branch();
                node.Attach(keys[i], child);
            }

            node = child;
        }

        node.Attach(keys[^1], KeyMapNode.Leaf(command));
    }

    /// <summary>Binds a key sequence written in configuration syntax.</summary>
    /// <param name="binding">The binding as written, for example <c>"&lt;C-a&gt;"</c>.</param>
    /// <param name="command">The command to run.</param>
    public void Bind(string binding, string command) =>
        Bind(KeyBindingParser.Parse(binding), command);

    /// <summary>Finds the node at a key path, or <see langword="null"/> when nothing is there.</summary>
    /// <param name="keys">The key codes to follow.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public KeyMapNode? Find(IReadOnlyList<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        KeyMapNode? node = Root;
        foreach (int key in keys)
        {
            node = node?.Child(key);
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>
    /// Duplicates a binding, or a whole branch of bindings, under another key sequence.
    /// </summary>
    /// <remarks>
    /// Copying a branch is what makes <c>copymap m&lt;bg&gt; um&lt;bg&gt;</c> useful: it moves a
    /// prefix and everything under it in one line. The copy is taken at the moment this runs, so
    /// a later change to either side leaves the other alone.
    /// </remarks>
    /// <param name="source">The key codes to copy from.</param>
    /// <param name="target">The key codes to copy to.</param>
    /// <exception cref="KeyBindingException">Nothing is bound at the source.</exception>
    public void Copy(IReadOnlyList<int> source, IReadOnlyList<int> target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        KeyMapNode node = Find(source) ?? throw new KeyBindingException(
            $"Tried to copy the binding '{KeyCodes.ToDisplayString(source)}', " +
            "but nothing is bound there.");

        if (target.Count == 0)
        {
            return;
        }

        KeyMapNode copy = node.DeepCopy();
        KeyMapNode parent = Root;

        for (int i = 0; i < target.Count - 1; i++)
        {
            KeyMapNode? child = parent.Child(target[i]);
            if (child is null || child.IsLeaf)
            {
                child = KeyMapNode.Branch();
                parent.Attach(target[i], child);
            }

            parent = child;
        }

        parent.Attach(target[^1], copy);
    }

    /// <summary>Duplicates a binding written in configuration syntax.</summary>
    /// <param name="source">The binding to copy from.</param>
    /// <param name="target">The binding to copy to.</param>
    public void Copy(string source, string target) =>
        Copy(KeyBindingParser.Parse(source), KeyBindingParser.Parse(target));

    /// <summary>
    /// Removes a binding, and any branches left empty by its removal.
    /// </summary>
    /// <param name="keys">The key codes to unbind.</param>
    /// <returns><see langword="true"/> when something was removed.</returns>
    public bool Unbind(IReadOnlyList<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Count != 0 && Remove(Root, keys, 0);
    }

    /// <summary>Removes a binding written in configuration syntax.</summary>
    /// <param name="binding">The binding to remove.</param>
    /// <returns><see langword="true"/> when something was removed.</returns>
    public bool Unbind(string binding) => Unbind(KeyBindingParser.Parse(binding));

    /// <summary>Discards every binding in this context.</summary>
    public void Clear() => Root = KeyMapNode.Branch();

    /// <summary>
    /// Enumerates every bound sequence and its command, in key order, for the hint list and for
    /// the <c>:dump_keybindings</c> command.
    /// </summary>
    /// <returns>Each binding as its key codes and command.</returns>
    public IEnumerable<(IReadOnlyList<int> Keys, string Command)> Enumerate()
    {
        List<int> prefix = [];
        return Walk(Root, prefix);

        static IEnumerable<(IReadOnlyList<int>, string)> Walk(KeyMapNode node, List<int> prefix)
        {
            foreach ((int key, KeyMapNode child) in node.Children.OrderBy(c => c.Key))
            {
                prefix.Add(key);

                if (child.IsLeaf)
                {
                    yield return (prefix.ToArray(), child.Command!);
                }
                else
                {
                    foreach (var binding in Walk(child, prefix))
                    {
                        yield return binding;
                    }
                }

                prefix.RemoveAt(prefix.Count - 1);
            }
        }
    }

    /// <summary>Recursively removes a path, pruning branches that become empty.</summary>
    private static bool Remove(KeyMapNode node, IReadOnlyList<int> keys, int depth)
    {
        int key = keys[depth];

        if (depth == keys.Count - 1)
        {
            return node.Detach(key);
        }

        KeyMapNode? child = node.Child(key);
        if (child is null || child.IsLeaf || !Remove(child, keys, depth + 1))
        {
            return false;
        }

        if (child.IsEmptyBranch)
        {
            node.Detach(key);
        }

        return true;
    }
}
