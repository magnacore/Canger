// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Signals;

/// <summary>
/// Conventional handler priorities. Higher runs earlier.
/// </summary>
/// <remarks>
/// <para>
/// These mirror ranger's constants in <c>ranger/container/settings.py:17-21</c>, with one
/// deliberate difference: ranger clamps every priority into <c>[0, 1]</c>
/// (<c>ranger/ext/signals.py:106</c>), which silently collapses its own <c>RAW</c> priority of
/// 2.0 down to 1.0 and makes it indistinguishable from <c>Sanitize</c>. Canger does not clamp,
/// so the ordering the names imply is the ordering you actually get.
/// </para>
/// </remarks>
public static class SignalPriority
{
    /// <summary>Runs before any interpretation of the value. Rarely needed.</summary>
    public const double Raw = 2.0;

    /// <summary>Validates and normalises the payload before anything acts on it.</summary>
    public const double Sanitize = 1.0;

    /// <summary>The default for handlers with no particular ordering requirement.</summary>
    public const double Normal = 0.5;

    /// <summary>Reacts to a sanitised payload, ahead of the commit.</summary>
    public const double Between = 0.6;

    /// <summary>Commits the payload — for settings, this is the handler that writes the store.</summary>
    public const double Sync = 0.2;

    /// <summary>Runs after the commit, for observers that need the final value.</summary>
    public const double AfterSync = 0.1;
}
