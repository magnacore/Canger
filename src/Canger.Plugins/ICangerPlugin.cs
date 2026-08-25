// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;

namespace Canger.Plugins;

/// <summary>
/// Something a plugin can do beyond adding commands.
/// </summary>
/// <remarks>
/// <para>
/// Commands, linemodes and colourschemes are found by reflection and need no interface — a plugin
/// only implements this when it wants to run code at a particular moment: subscribing to signals,
/// defining aliases, reading its own configuration.
/// </para>
/// <para>
/// The two moments are separate because they can do different things. <see cref="OnInit"/> runs
/// before the interface exists, which is where anything affecting what is first drawn belongs;
/// <see cref="OnReady"/> runs once it is up, which is where anything that draws or asks a
/// question belongs.
/// </para>
/// </remarks>
public interface ICangerPlugin
{
    /// <summary>
    /// Runs before the interface exists.
    /// </summary>
    /// <param name="fileManager">What the plugin acts on.</param>
    void OnInit(IFileManager fileManager)
    {
    }

    /// <summary>
    /// Runs once the interface is up and the first directory is loaded.
    /// </summary>
    /// <param name="fileManager">What the plugin acts on.</param>
    void OnReady(IFileManager fileManager)
    {
    }
}
