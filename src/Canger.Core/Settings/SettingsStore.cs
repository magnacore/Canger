// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.Signals;

namespace Canger.Core.Settings;

/// <summary>
/// The settings store: three scopes, schema validation, and a signal pipeline around every write.
/// </summary>
/// <remarks>
/// <para>
/// Assignment is deliberately not a plain dictionary write. Each change is published as a
/// <see cref="SettingChange"/>; handlers registered at <see cref="SignalPriority.Sanitize"/> may
/// rewrite the value, an internal handler at <see cref="SignalPriority.Sync"/> commits it, and
/// observers at <see cref="SignalPriority.AfterSync"/> react to the committed result. Collapsing
/// settings into plain properties would lose the sanitise hook that directories, colorschemes
/// and plugins all depend on.
/// </para>
/// <para>
/// The three scopes resolve local, then tag, then global, matching
/// <c>ranger/container/settings.py:221-248</c>.
/// </para>
/// </remarks>
public sealed class SettingsStore : ISettings
{
    private readonly Dictionary<string, object?> _global = new(StringComparer.Ordinal);
    private readonly List<PathScopedValue> _pathScoped = [];
    private readonly Dictionary<char, Dictionary<string, object?>> _tagScoped = [];
    private readonly Lock _gate = new();

    /// <summary>Creates a store seeded with every setting's default value.</summary>
    /// <param name="signals">
    /// The bus that setting changes are published on. When omitted, a private bus is created,
    /// which is convenient for tests.
    /// </param>
    public SettingsStore(SignalBus? signals = null)
    {
        Signals = signals ?? new SignalBus();

        foreach (SettingDefinition definition in SettingsCatalog.All.Values)
        {
            _global[definition.Name] = definition.DefaultValue;
        }

        // The commit handler is just another subscriber, at a priority low enough that every
        // sanitising handler has already had its say.
        Signals.Subscribe(SettingChange.SignalPrefix, Commit, SignalPriority.Sync);
    }

    /// <summary>The bus that setting changes are published on.</summary>
    public SignalBus Signals { get; }

    /// <inheritdoc />
    public object? Get(string name, string? path = null, IReadOnlyCollection<char>? tags = null)
    {
        SettingDefinition definition = SettingsCatalog.Require(name);

        lock (_gate)
        {
            // Local scope wins, then tag scope, then the global value.
            if (path is not null)
            {
                for (int i = _pathScoped.Count - 1; i >= 0; i--)
                {
                    PathScopedValue scoped = _pathScoped[i];
                    if (scoped.Name == definition.Name && scoped.Pattern.IsMatch(path))
                    {
                        return scoped.Value;
                    }
                }
            }

            if (tags is not null)
            {
                foreach (char tag in tags)
                {
                    if (_tagScoped.TryGetValue(tag, out Dictionary<string, object?>? values) &&
                        values.TryGetValue(definition.Name, out object? tagValue))
                    {
                        return tagValue;
                    }
                }
            }

            return _global.TryGetValue(definition.Name, out object? value)
                ? value
                : definition.DefaultValue;
        }
    }

    /// <inheritdoc />
    public T? Get<T>(string name, string? path = null, IReadOnlyCollection<char>? tags = null) =>
        Get(name, path, tags) is T typed ? typed : default;

    /// <inheritdoc />
    public void Set(string name, object? value, SettingScope scope = default)
    {
        SettingDefinition definition = SettingsCatalog.Require(name);
        definition.Validate(value);

        object? previous = Get(name);

        // One signal, announced under both names at once: "setopt" for handlers watching any
        // setting and "setopt.<name>" for handlers watching this one. They are merged into a
        // single priority-ordered pass so that a sanitising handler registered under either
        // name runs before the commit, rather than after it.
        SettingChange change = new(
            SettingChange.SignalNameFor(definition.Name), definition, value, previous, scope);

        Signals.EmitTo(change, SettingChange.SignalPrefix, change.Name);
    }

    /// <inheritdoc />
    public void SetFromText(string name, string text, SettingScope scope = default)
    {
        SettingDefinition definition = SettingsCatalog.Require(name);
        Set(definition.Name, SettingValueParser.Parse(definition, text), scope);
    }

    /// <inheritdoc />
    public void Toggle(string name, SettingScope scope = default)
    {
        SettingDefinition definition = SettingsCatalog.Require(name);
        Set(definition.Name, SettingValueParser.Toggle(definition, Get(definition.Name)), scope);
    }

    /// <inheritdoc />
    public ISettings ForPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new PathBoundSettings(this, path);
    }

    /// <summary>
    /// Writes the value into the scope the change targets. Registered at
    /// <see cref="SignalPriority.Sync"/>, so every sanitising handler has already run.
    /// </summary>
    private void Commit(Signal signal)
    {
        if (signal is not SettingChange change)
        {
            return;
        }

        // A sanitising handler may have replaced the value with something the setting cannot
        // hold, so validate again rather than trusting it.
        change.Definition.Validate(change.Value);

        lock (_gate)
        {
            if (change.Scope.PathPattern is { } pattern)
            {
                _pathScoped.Add(new PathScopedValue(
                    change.SettingName,
                    new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant),
                    change.Value));
            }
            else if (change.Scope.Tags is { } tags)
            {
                foreach (char tag in tags)
                {
                    if (!_tagScoped.TryGetValue(tag, out Dictionary<string, object?>? values))
                    {
                        values = new Dictionary<string, object?>(StringComparer.Ordinal);
                        _tagScoped[tag] = values;
                    }

                    values[change.SettingName] = change.Value;
                }
            }
            else
            {
                _global[change.SettingName] = change.Value;
            }
        }
    }

    /// <summary>One path-scoped assignment.</summary>
    private sealed record PathScopedValue(string Name, Regex Pattern, object? Value);

    /// <summary>
    /// A view of the store bound to one directory, so a directory can read its own settings
    /// without repeating its path at every call.
    /// </summary>
    private sealed class PathBoundSettings(SettingsStore store, string path) : ISettings
    {
        public object? Get(string name, string? overridePath = null,
                           IReadOnlyCollection<char>? tags = null) =>
            store.Get(name, overridePath ?? path, tags);

        public T? Get<T>(string name, string? overridePath = null,
                         IReadOnlyCollection<char>? tags = null) =>
            store.Get<T>(name, overridePath ?? path, tags);

        public void Set(string name, object? value, SettingScope scope = default) =>
            store.Set(name, value, scope);

        public void SetFromText(string name, string text, SettingScope scope = default) =>
            store.SetFromText(name, text, scope);

        public void Toggle(string name, SettingScope scope = default) =>
            store.Toggle(name, scope);

        public ISettings ForPath(string otherPath) => store.ForPath(otherPath);
    }
}
