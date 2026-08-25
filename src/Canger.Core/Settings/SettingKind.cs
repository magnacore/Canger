// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Settings;

/// <summary>
/// The value type of a setting, which determines how a string from cc.conf or the <c>:set</c>
/// command is parsed and what a stored value may be.
/// </summary>
public enum SettingKind
{
    /// <summary>A boolean. Accepts <c>true/on/1</c> and <c>false/off/0</c>.</summary>
    Boolean,

    /// <summary>A string, used for names, regular expressions and enumerated values.</summary>
    String,

    /// <summary>A 32-bit integer.</summary>
    Integer,

    /// <summary>A double-precision number. Only <c>w3m_delay</c> uses this.</summary>
    Float,

    /// <summary>
    /// A comma-separated list of integers. Only <c>column_ratios</c> uses this.
    /// </summary>
    IntegerList,
}
