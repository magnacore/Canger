// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Signals;

namespace Canger.Core.Settings;

/// <summary>
/// Published when a setting is assigned, before the value is committed to the store.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism that makes settings extensible. Assignment is not a plain write: the
/// change travels through the signal bus, so a handler at <see cref="SignalPriority.Sanitize"/>
/// can rewrite <see cref="Value"/> — clamping a ratio, resolving a colorscheme name to an
/// instance — before the commit handler at <see cref="SignalPriority.Sync"/> stores it, and
/// observers at <see cref="SignalPriority.AfterSync"/> see the final value.
/// </para>
/// <para>
/// Two signal names are published for every change: <c>setopt</c> for handlers interested in any
/// setting, and <c>setopt.&lt;name&gt;</c> for handlers interested in one. This matches
/// <c>ranger/container/settings.py:205-206</c>.
/// </para>
/// </remarks>
/// <param name="name">The signal name, either <c>setopt</c> or <c>setopt.&lt;setting&gt;</c>.</param>
/// <param name="definition">The schema of the setting being assigned.</param>
/// <param name="value">The incoming value. Handlers may replace it.</param>
/// <param name="previousValue">The value in effect before this change.</param>
/// <param name="scope">Which scope the assignment targets.</param>
public sealed class SettingChange(
    string name,
    SettingDefinition definition,
    object? value,
    object? previousValue,
    SettingScope scope) : Signal(name)
{
    /// <summary>The prefix of every settings signal name.</summary>
    public const string SignalPrefix = "setopt";

    /// <summary>The schema of the setting being assigned.</summary>
    public SettingDefinition Definition { get; } = definition;

    /// <summary>
    /// The value being assigned. Sanitising handlers replace this; the value that finally
    /// reaches the store is whatever sits here when the commit handler runs.
    /// </summary>
    public object? Value { get; set; } = value;

    /// <summary>The value that was in effect before this change.</summary>
    public object? PreviousValue { get; } = previousValue;

    /// <summary>Which scope the assignment targets.</summary>
    public SettingScope Scope { get; } = scope;

    /// <summary>The name of the setting being assigned.</summary>
    public string SettingName => Definition.Name;

    /// <summary>Builds the per-setting signal name for a setting.</summary>
    /// <param name="settingName">The setting name.</param>
    /// <returns>The signal name, for example <c>setopt.sort</c>.</returns>
    public static string SignalNameFor(string settingName) => $"{SignalPrefix}.{settingName}";
}
