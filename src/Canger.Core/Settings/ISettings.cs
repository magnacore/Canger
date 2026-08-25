// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Settings;

/// <summary>
/// Read and write access to Canger's configuration.
/// </summary>
/// <remarks>
/// Everything that reads a setting takes this interface rather than the concrete store, so
/// behaviour can be tested by handing in a store seeded with whatever values a test needs.
/// </remarks>
public interface ISettings
{
    /// <summary>
    /// Reads a setting, resolving path and tag scopes before falling back to the global value.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="path">
    /// The directory the value is being read for, so that path-scoped assignments apply.
    /// <see langword="null"/> reads the global value.
    /// </param>
    /// <param name="tags">
    /// Tags carried by the file the value is being read for, so that tag-scoped assignments
    /// apply. <see langword="null"/> ignores tag scopes.
    /// </param>
    /// <returns>The effective value.</returns>
    /// <exception cref="SettingValueException">No setting has that name.</exception>
    object? Get(string name, string? path = null, IReadOnlyCollection<char>? tags = null);

    /// <summary>
    /// Reads a setting and casts it to the expected type.
    /// </summary>
    /// <typeparam name="T">The setting's value type.</typeparam>
    /// <param name="name">The setting name.</param>
    /// <param name="path">Directory for path-scoped resolution, or <see langword="null"/>.</param>
    /// <param name="tags">Tags for tag-scoped resolution, or <see langword="null"/>.</param>
    /// <returns>The effective value.</returns>
    T? Get<T>(string name, string? path = null, IReadOnlyCollection<char>? tags = null);

    /// <summary>
    /// Assigns a setting, running the change through the signal pipeline.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="value">The value, already parsed to the setting's type.</param>
    /// <param name="scope">Where the assignment applies. Defaults to global.</param>
    /// <exception cref="SettingValueException">
    /// No setting has that name, or the value is not one the setting can hold.
    /// </exception>
    void Set(string name, object? value, SettingScope scope = default);

    /// <summary>
    /// Assigns a setting from its textual form, as written in cc.conf or typed at <c>:set</c>.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="text">The value as written.</param>
    /// <param name="scope">Where the assignment applies. Defaults to global.</param>
    /// <exception cref="SettingValueException">The name or the value is not valid.</exception>
    void SetFromText(string name, string text, SettingScope scope = default);

    /// <summary>
    /// Toggles a boolean setting, or advances an enumerated one to its next value.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="scope">Where the assignment applies. Defaults to global.</param>
    /// <exception cref="SettingValueException">The setting cannot be toggled.</exception>
    void Toggle(string name, SettingScope scope = default);

    /// <summary>
    /// Returns a view of these settings bound to one directory, so callers that always read for
    /// the same path need not pass it every time.
    /// </summary>
    /// <param name="path">The directory to bind to.</param>
    /// <returns>The bound view.</returns>
    ISettings ForPath(string path);
}
