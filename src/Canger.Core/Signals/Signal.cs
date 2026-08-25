// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Signals;

/// <summary>
/// Base class for anything published on a <see cref="SignalBus"/>.
/// </summary>
/// <remarks>
/// Signals are mutable on purpose. Handlers registered at a higher priority run first and may
/// rewrite the payload before lower-priority handlers see it — that is how a setting is
/// sanitised before it is stored. See <c>SettingChange</c> in Canger.Core.Settings for the canonical example.
/// </remarks>
/// <param name="name">The signal name handlers subscribe to.</param>
public abstract class Signal(string name)
{
    /// <summary>The name handlers subscribe to, for example <c>setopt.sort</c>.</summary>
    public string Name { get; } = name;

    /// <summary>Whether a handler has asked that the signal stop propagating.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>
    /// Prevents any remaining handlers from running. Emission then reports failure to the caller.
    /// </summary>
    public void Stop() => IsStopped = true;
}
