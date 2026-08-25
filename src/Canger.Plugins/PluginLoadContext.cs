// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Runtime.Loader;

namespace Canger.Plugins;

/// <summary>
/// The load context a plugin assembly lives in.
/// </summary>
/// <remarks>
/// <para>
/// Collectible, so an assembly can in principle be unloaded and replaced without restarting.
/// Canger does not reload plugins today, but making the context collectible from the start costs
/// nothing and is not something that can be retrofitted once references have leaked out.
/// </para>
/// <para>
/// Resolution deliberately falls through to the default context rather than loading its own copy
/// of anything: a plugin's <c>CangerCommand</c> must be the same type as Canger's, or the
/// registry would not recognise it. That is the usual plugin-isolation trade-off, and here
/// sharing is the whole point.
/// </para>
/// </remarks>
/// <param name="name">Names the context in diagnostics.</param>
public sealed class PluginLoadContext(string name)
    : AssemblyLoadContext(name, isCollectible: true)
{
    /// <inheritdoc />
    /// <remarks>
    /// Returning <see langword="null"/> defers to the default context, which is what shares
    /// Canger's own assemblies with the plugin.
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName) => null;
}
