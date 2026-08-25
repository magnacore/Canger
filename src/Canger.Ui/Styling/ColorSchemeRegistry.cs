// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Ui.Styling;

/// <summary>
/// The colourschemes available by name, including any a plugin has added.
/// </summary>
/// <remarks>
/// Schemes are held as factories rather than instances because each caches its own results, and
/// two views sharing one instance would share that cache across different terminals. Building a
/// fresh one per use keeps them independent and costs nothing.
/// </remarks>
public sealed class ColorSchemeRegistry
{
    /// <summary>The scheme used when the configured one does not exist.</summary>
    public const string DefaultName = "default";

    private readonly Dictionary<string, Func<IColorScheme>> _schemes = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>Creates a registry holding the built-in schemes.</summary>
    public ColorSchemeRegistry()
    {
        Register(DefaultName, () => new DefaultColorScheme());
        Register("jungle", () => new JungleColorScheme());
        Register("snow", () => new SnowColorScheme());
        Register("solarized", () => new SolarizedColorScheme());
    }

    /// <summary>The registered names, in registration order.</summary>
    public IReadOnlyList<string> Names => _order;

    /// <summary>
    /// Adds a scheme, replacing any with the same name.
    /// </summary>
    /// <param name="name">The name it is selected by.</param>
    /// <param name="create">Builds an instance.</param>
    public void Register(string name, Func<IColorScheme> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(create);

        if (_schemes.TryAdd(name, create))
        {
            _order.Add(name);
        }
        else
        {
            _schemes[name] = create;
        }
    }

    /// <summary>
    /// Builds the named scheme.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The scheme, or <see langword="null"/> when there is no such name.</returns>
    public IColorScheme? Create(string? name) =>
        name is not null && _schemes.TryGetValue(name, out Func<IColorScheme>? create)
            ? create()
            : null;

    /// <summary>
    /// Builds the named scheme, falling back to the default.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The scheme.</returns>
    /// <remarks>
    /// A misspelled name in the configuration costs the colours, not the session: falling back is
    /// far better than refusing to start over a typo.
    /// </remarks>
    public IColorScheme CreateOrDefault(string? name) => Create(name) ?? _schemes[DefaultName]();
}

/// <summary>
/// A scheme that forwards to whichever scheme is currently configured.
/// </summary>
/// <remarks>
/// The widgets are handed a colourscheme when they are built and keep it for their lifetime, so
/// <c>:set colorscheme</c> would otherwise only take effect on the next run. Giving them this
/// instead means the switch happens the moment the setting changes, with no widget rebuilding
/// and no reference to update in more than one place.
/// </remarks>
/// <param name="initial">The scheme to start with.</param>
public sealed class SwitchableColorScheme(IColorScheme initial) : IColorScheme
{
    private IColorScheme _current = initial ?? throw new ArgumentNullException(nameof(initial));

    /// <summary>The scheme currently in use.</summary>
    public IColorScheme Current => _current;

    /// <inheritdoc />
    public string Name => _current.Name;

    /// <inheritdoc />
    public CellStyle Resolve(StyleContext context) => _current.Resolve(context);

    /// <summary>
    /// Switches to another scheme.
    /// </summary>
    /// <param name="scheme">The scheme to use from now on.</param>
    /// <returns><see langword="true"/> when this was a change, so the caller can redraw.</returns>
    public bool SwitchTo(IColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        if (string.Equals(_current.Name, scheme.Name, StringComparison.Ordinal))
        {
            return false;
        }

        _current = scheme;
        return true;
    }
}
