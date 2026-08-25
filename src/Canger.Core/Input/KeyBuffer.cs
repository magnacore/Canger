// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>
/// Accumulates keys as they arrive and reports when they complete a binding.
/// </summary>
/// <remarks>
/// <para>
/// This is the state machine behind every keystroke. It handles the numeric prefix (typing
/// <c>3</c> before <c>dd</c> cuts three entries), walks the key map, captures the keys that
/// matched a <c>&lt;any&gt;</c> wildcard so the command can refer to them as <c>%any</c>, and
/// decides when a sequence has completed, is still in progress, or has gone nowhere.
/// </para>
/// <para>
/// Four rules carry real weight and are reproduced exactly from
/// <c>ranger/ext/keybinding_parser.py:255-284</c>:
/// </para>
/// <list type="number">
///   <item><description>
///     Leading digits accumulate into a quantifier until a non-digit arrives. A key map can opt
///     out with <c>&lt;allow_quantifiers&gt; false</c>, which the console does so that digits
///     type normally.
///   </description></item>
///   <item><description>
///     An exact binding always beats <c>&lt;any&gt;</c>.
///   </description></item>
///   <item><description>
///     Escape never matches <c>&lt;any&gt;</c>, so it can always abandon a partial sequence.
///   </description></item>
///   <item><description>
///     Reaching a branch that carries a <c>&lt;bg&gt;</c> command fires that command but does
///     <em>not</em> end the sequence, so the branch keeps waiting for the next key.
///   </description></item>
/// </list>
/// </remarks>
public sealed class KeyBuffer
{
    private readonly List<int> _keys = [];
    private readonly List<int> _wildcards = [];

    /// <summary>Creates a buffer reading from a key map.</summary>
    /// <param name="keyMap">The map to match against.</param>
    public KeyBuffer(KeyMap keyMap)
    {
        ArgumentNullException.ThrowIfNull(keyMap);
        KeyMap = keyMap;
        Clear();
    }

    /// <summary>The map being matched against.</summary>
    public KeyMap KeyMap { get; private set; }

    /// <summary>The keys typed so far, including any that formed the quantifier.</summary>
    public IReadOnlyList<int> Keys => _keys;

    /// <summary>
    /// The keys that matched a <c>&lt;any&gt;</c> wildcard, in order, which become the
    /// <c>%any0</c>, <c>%any1</c> and so on macros.
    /// </summary>
    public IReadOnlyList<int> Wildcards => _wildcards;

    /// <summary>The numeric prefix typed before the binding, when there was one.</summary>
    public int? Quantifier { get; private set; }

    /// <summary>The command to run, once a binding has completed or a passive action fired.</summary>
    public string? Result { get; private set; }

    /// <summary>Whether no further key can extend what has been typed.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Whether the keys typed went nowhere.</summary>
    public bool HasFailed { get; private set; }

    /// <summary>Whether the quantifier, if any, has been fully read.</summary>
    public bool IsQuantifierFinished { get; private set; }

    /// <summary>Where in the key map the typed keys have reached.</summary>
    public KeyMapNode Position { get; private set; } = KeyMapNode.Branch();

    /// <summary>
    /// Switches to another key map, discarding anything partly typed if the map really changed.
    /// </summary>
    /// <param name="keyMap">The map to switch to.</param>
    public void Use(KeyMap keyMap)
    {
        ArgumentNullException.ThrowIfNull(keyMap);

        if (ReferenceEquals(keyMap, KeyMap))
        {
            return;
        }

        KeyMap = keyMap;
        Clear();
    }

    /// <summary>Discards everything typed and starts again.</summary>
    public void Clear()
    {
        _keys.Clear();
        _wildcards.Clear();
        Quantifier = null;
        Result = null;
        IsFinished = false;
        HasFailed = false;
        Position = KeyMap.Root;

        // A map may switch quantifiers off entirely, which the console does so that digits are
        // typed rather than counted.
        IsQuantifierFinished = KeyMap.Root.Child(KeyCodes.AllowQuantifiers) is { Command: "false" };
    }

    /// <summary>
    /// Feeds one key into the buffer.
    /// </summary>
    /// <param name="key">The key code.</param>
    /// <returns>
    /// The command to run when this key completed a binding or fired a passive action, otherwise
    /// <see langword="null"/>.
    /// </returns>
    public string? Add(int key)
    {
        _keys.Add(key);
        Result = null;

        if (!IsQuantifierFinished && key is >= '0' and <= '9')
        {
            Quantifier = ((Quantifier ?? 0) * 10) + (key - '0');
            return null;
        }

        IsQuantifierFinished = true;

        KeyMapNode? next = Advance(key);
        if (next is null)
        {
            IsFinished = true;
            HasFailed = true;
            return null;
        }

        Position = next;

        if (next.IsLeaf)
        {
            IsFinished = true;
            Result = next.Command;
            return Result;
        }

        // Arriving at a branch with a <bg> command runs it without ending the sequence.
        Result = next.PassiveCommand;
        return Result;
    }

    /// <summary>
    /// Follows one key out of the current position, preferring an exact binding over the
    /// wildcard and refusing to let Escape match the wildcard.
    /// </summary>
    private KeyMapNode? Advance(int key)
    {
        if (Position.IsLeaf)
        {
            return null;
        }

        if (Position.Child(key) is { } exact)
        {
            return exact;
        }

        if (key == KeyCodes.Escape)
        {
            return null;
        }

        if (Position.Child(KeyCodes.Any) is { } wildcard)
        {
            _wildcards.Add(key);
            return wildcard;
        }

        return null;
    }

    /// <summary>
    /// The keys typed so far, rendered the way the title bar shows them.
    /// </summary>
    /// <returns>The display string.</returns>
    public override string ToString() => KeyCodes.ToDisplayString(_keys);
}
