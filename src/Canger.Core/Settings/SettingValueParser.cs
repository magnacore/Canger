// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Settings;

/// <summary>
/// Turns the textual values written in cc.conf or typed at the <c>:set</c> prompt into typed
/// setting values.
/// </summary>
/// <remarks>
/// Ranger parses loosely and untyped, guessing at int, then bool, then float, then falling back
/// to a string (<c>ranger/core/actions.py:113-137</c>). Canger parses against the setting's
/// declared kind instead, which means <c>set scroll_offset banana</c> is reported as an error
/// rather than silently storing the string "banana" in an integer setting.
/// </remarks>
public static class SettingValueParser
{
    private static readonly string[] TruthyWords = ["true", "on", "yes", "1"];
    private static readonly string[] FalsyWords = ["false", "off", "no", "0"];

    /// <summary>
    /// Parses a textual value for a setting.
    /// </summary>
    /// <param name="definition">The setting's schema.</param>
    /// <param name="text">
    /// The value as written. An empty string is a legitimate value for string settings such as
    /// <c>global_inode_type_filter</c>, which cc.conf clears with a bare <c>set</c> line.
    /// </param>
    /// <returns>The parsed value, ready to store.</returns>
    /// <exception cref="SettingValueException">The text is not valid for this setting.</exception>
    public static object? Parse(SettingDefinition definition, string text)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();

        if (definition.AllowsNull &&
            trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return definition.Kind switch
        {
            SettingKind.Boolean => ParseBoolean(definition, trimmed),
            SettingKind.Integer => ParseInteger(definition, trimmed),
            SettingKind.Float => ParseFloat(definition, trimmed),
            SettingKind.IntegerList => ParseIntegerList(definition, trimmed),
            SettingKind.String => ParseString(definition, trimmed),
            _ => throw new SettingValueException(
                definition.Name, trimmed, $"unsupported setting kind {definition.Kind}"),
        };
    }

    private static object ParseBoolean(SettingDefinition definition, string text)
    {
        if (TruthyWords.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (FalsyWords.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new SettingValueException(
            definition.Name, text, "expected one of true, on, yes, 1, false, off, no, 0");
    }

    private static object ParseInteger(SettingDefinition definition, string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new SettingValueException(definition.Name, text, "expected a whole number");

    private static object ParseFloat(SettingDefinition definition, string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new SettingValueException(definition.Name, text, "expected a number");

    private static object ParseIntegerList(SettingDefinition definition, string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                         StringSplitOptions.TrimEntries);
        int[] values = new int[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out values[i]))
            {
                throw new SettingValueException(
                    definition.Name, text, "expected a comma-separated list of whole numbers");
            }
        }

        return values;
    }

    private static object ParseString(SettingDefinition definition, string text)
    {
        if (definition.IsEnumerated &&
            !definition.AllowedValues!.Contains(text, StringComparer.Ordinal))
        {
            throw new SettingValueException(
                definition.Name, text,
                $"allowed values are {string.Join(", ", definition.AllowedValues!)}");
        }

        return text;
    }

    /// <summary>
    /// Produces the next value when a setting is toggled with <c>:set name!</c>.
    /// </summary>
    /// <remarks>
    /// Booleans invert. Enumerated settings advance to the next allowed value and wrap, which is
    /// what makes <c>map ~ set viewmode!</c> flip between miller and multipane. This mirrors
    /// <c>ranger/core/actions.py:691-708</c>.
    /// </remarks>
    /// <param name="definition">The setting's schema.</param>
    /// <param name="current">The value in effect.</param>
    /// <returns>The value to store.</returns>
    /// <exception cref="SettingValueException">The setting cannot be toggled.</exception>
    public static object? Toggle(SettingDefinition definition, object? current)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Kind == SettingKind.Boolean)
        {
            return current is not true;
        }

        if (definition.IsEnumerated)
        {
            IReadOnlyList<string> allowed = definition.AllowedValues!;
            int index = current is string text
                ? allowed.ToList().IndexOf(text)
                : -1;

            return allowed[(index + 1) % allowed.Count];
        }

        throw new SettingValueException(
            definition.Name, "!", "only boolean and enumerated settings can be toggled");
    }
}
