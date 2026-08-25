// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Settings;

/// <summary>
/// The schema for one setting: its type, its default, and the values it will accept.
/// </summary>
/// <remarks>
/// Ranger validates settings with <c>assert</c> (<c>ranger/container/settings.py:194</c>), which
/// disappears entirely when Python runs with <c>-O</c>. Canger treats the schema as a real
/// contract and raises on violation, so a typo in cc.conf is reported rather than silently
/// storing a value of the wrong type that fails much later.
/// </remarks>
/// <param name="Name">The setting name as it appears in cc.conf.</param>
/// <param name="Kind">How values are parsed and validated.</param>
/// <param name="DefaultValue">
/// The value in effect before any configuration is loaded. Canger ships working defaults so it
/// runs correctly with no config at all, unlike ranger, which depends on rc.conf always loading.
/// </param>
/// <param name="AllowedValues">
/// For enumerated settings, the permitted values in their canonical order. The order matters:
/// toggling with <c>:set name!</c> cycles through this list. <see langword="null"/> when any
/// value of the right type is acceptable.
/// </param>
/// <param name="AllowsNull">Whether the setting may hold no value at all.</param>
/// <param name="Summary">A one-line description, shown by <c>:help</c> and tab completion.</param>
public sealed record SettingDefinition(
    string Name,
    SettingKind Kind,
    object? DefaultValue,
    IReadOnlyList<string>? AllowedValues = null,
    bool AllowsNull = false,
    string Summary = "")
{
    /// <summary>Whether this setting restricts values to an enumerated set.</summary>
    public bool IsEnumerated => AllowedValues is { Count: > 0 };

    /// <summary>
    /// Checks that a value is acceptable for this setting, throwing when it is not.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <exception cref="SettingValueException">
    /// The value has the wrong type, or is outside the enumerated set.
    /// </exception>
    public void Validate(object? value)
    {
        if (value is null)
        {
            if (!AllowsNull)
            {
                throw new SettingValueException(Name, "null", "this setting requires a value");
            }

            return;
        }

        bool typeMatches = Kind switch
        {
            SettingKind.Boolean => value is bool,
            SettingKind.String => value is string,
            SettingKind.Integer => value is int,
            SettingKind.Float => value is double,
            SettingKind.IntegerList => value is IReadOnlyList<int>,
            _ => false,
        };

        if (!typeMatches)
        {
            throw new SettingValueException(
                Name, value.ToString() ?? "?", $"expected {Kind}, got {value.GetType().Name}");
        }

        if (IsEnumerated && value is string text &&
            !AllowedValues!.Contains(text, StringComparer.Ordinal))
        {
            throw new SettingValueException(
                Name, text, $"allowed values are {string.Join(", ", AllowedValues!)}");
        }
    }
}
