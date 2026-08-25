// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>The parts of the interface that have their own key bindings.</summary>
public enum KeyContext
{
    /// <summary>The file browser, bound with <c>map</c>.</summary>
    Browser,

    /// <summary>The command line, bound with <c>cmap</c>.</summary>
    Console,

    /// <summary>The file pager, bound with <c>pmap</c>.</summary>
    Pager,

    /// <summary>The task view, bound with <c>tmap</c>.</summary>
    TaskView,
}

/// <summary>
/// The key maps for every context.
/// </summary>
/// <remarks>
/// Each context is independent, so <c>q</c> can quit in the browser and close the pager without
/// the two interfering. Which map is consulted is decided by whichever widget currently has
/// focus.
/// </remarks>
public sealed class KeyMaps
{
    private readonly Dictionary<KeyContext, KeyMap> _maps = new()
    {
        [KeyContext.Browser] = new KeyMap(),
        [KeyContext.Console] = new KeyMap(),
        [KeyContext.Pager] = new KeyMap(),
        [KeyContext.TaskView] = new KeyMap(),
    };

    /// <summary>The key map for a context.</summary>
    /// <param name="context">The context.</param>
    /// <returns>Its key map.</returns>
    public KeyMap this[KeyContext context] => _maps[context];

    /// <summary>The browser key map, which is the one in effect most of the time.</summary>
    public KeyMap Browser => _maps[KeyContext.Browser];

    /// <summary>The command line key map.</summary>
    public KeyMap Console => _maps[KeyContext.Console];

    /// <summary>The pager key map.</summary>
    public KeyMap Pager => _maps[KeyContext.Pager];

    /// <summary>The task view key map.</summary>
    public KeyMap TaskView => _maps[KeyContext.TaskView];

    /// <summary>Discards every binding in every context.</summary>
    public void Clear()
    {
        foreach (KeyMap map in _maps.Values)
        {
            map.Clear();
        }
    }
}
