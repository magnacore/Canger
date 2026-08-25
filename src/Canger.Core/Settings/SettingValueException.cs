// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Settings;

/// <summary>
/// Raised when a setting is given a value it cannot hold — the wrong type, or one outside its
/// enumerated set.
/// </summary>
public sealed class SettingValueException : Exception
{
    /// <summary>Creates an exception describing an unusable value.</summary>
    /// <param name="settingName">The setting that was being assigned.</param>
    /// <param name="offendingValue">The value as the user wrote it.</param>
    /// <param name="reason">Why it was rejected, phrased for display in the status bar.</param>
    public SettingValueException(string settingName, string offendingValue, string reason)
        : base($"Cannot set '{settingName}' to '{offendingValue}': {reason}.")
    {
        SettingName = settingName;
        OffendingValue = offendingValue;
        Reason = reason;
    }

    /// <summary>Creates an exception with a plain message.</summary>
    /// <param name="message">The message.</param>
    public SettingValueException(string message) : base(message)
    {
        SettingName = string.Empty;
        OffendingValue = string.Empty;
        Reason = message;
    }

    /// <summary>Creates an exception with a message and an inner cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SettingValueException(string message, Exception innerException)
        : base(message, innerException)
    {
        SettingName = string.Empty;
        OffendingValue = string.Empty;
        Reason = message;
    }

    /// <summary>Creates an exception with no detail. Prefer the descriptive overloads.</summary>
    public SettingValueException() : this("Invalid setting value.")
    {
    }

    /// <summary>The setting that was being assigned.</summary>
    public string SettingName { get; }

    /// <summary>The value as the user wrote it.</summary>
    public string OffendingValue { get; }

    /// <summary>Why the value was rejected.</summary>
    public string Reason { get; }
}
