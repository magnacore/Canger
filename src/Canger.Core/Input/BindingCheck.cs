// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;

namespace Canger.Core.Input;

/// <summary>A binding whose command the registry cannot find.</summary>
/// <param name="Keys">The keys, as a person would type them.</param>
/// <param name="Line">The command line the binding holds.</param>
public readonly record struct DeadBinding(string Keys, string Line);

/// <summary>
/// Finds bindings that name a command which does not exist.
/// </summary>
/// <remarks>
/// <para>
/// A binding is stored as the text of a command line and looked up only when the key is pressed,
/// so one naming a command that is not there sits silently until somebody presses it. That is
/// exactly what happens when a plugin is removed: every key pointing at one of its commands goes
/// quiet, and nothing says why.
/// </para>
/// <para>
/// Only the browser's own map. The console, pager, task view and device maps name actions their
/// widgets handle — <c>console_close</c>, <c>pager_move</c>, <c>task_remove</c> — which never
/// reach the registry, so resolving them against it would report every one as missing.
/// </para>
/// <para>
/// Only the command name is checked. Whether its arguments make sense is the command's own
/// business, and many take arguments meaningful only at the moment they run.
/// </para>
/// </remarks>
public static class BindingCheck
{
    /// <summary>Every browser binding whose command cannot be resolved.</summary>
    /// <param name="keyMaps">The bindings to check.</param>
    /// <param name="commands">The registry to resolve against.</param>
    /// <returns>The dead bindings, in the order they were bound.</returns>
    public static IReadOnlyList<DeadBinding> Find(KeyMaps keyMaps, CommandRegistry commands)
    {
        ArgumentNullException.ThrowIfNull(keyMaps);
        ArgumentNullException.ThrowIfNull(commands);

        List<DeadBinding> dead = [];

        foreach ((IReadOnlyList<int> keys, string line) in keyMaps.Browser.Enumerate())
        {
            // Split on any whitespace, not just a space: a `map` line may separate the command
            // from a trailing comment with tabs, and taking "cmd\t\t#" as the name reported a
            // great many perfectly good bindings as broken.
            string name = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
                is [string first, ..] ? first : string.Empty;

            if (name.Length == 0)
            {
                continue;
            }

            bool known;

            try
            {
                known = commands.Find(name) is not null;
            }
            catch (CommandException)
            {
                // Ambiguous rather than missing: the name does resolve to something, and the
                // dispatcher reports the ambiguity itself when the key is pressed.
                known = true;
            }

            if (!known)
            {
                dead.Add(new DeadBinding(
                    string.Concat(keys.Select(KeyCodes.ToDisplayString)), line));
            }
        }

        return dead;
    }
}
