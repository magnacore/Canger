// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Configuration;

/// <summary>
/// Where Canger reads its configuration and writes its state.
/// </summary>
/// <remarks>
/// <para>
/// Configuration and state are kept apart, following the XDG base directory specification and
/// ranger's own layout. Files you edit live under <c>~/.config/canger</c>; files Canger writes
/// for itself — bookmarks, tags, history, the tab list — live under
/// <c>~/.local/share/canger</c>, so a configuration directory can be version controlled without
/// dragging mutable state along with it.
/// </para>
/// <para>
/// Every location honours its <c>XDG_*_HOME</c> environment variable, and each can be overridden
/// individually from the command line.
/// </para>
/// </remarks>
public sealed class CangerPaths
{
    /// <summary>The directory name used under each XDG root.</summary>
    public const string ApplicationName = "canger";

    /// <summary>Creates a set of paths, defaulting to the XDG locations.</summary>
    /// <param name="configDirectory">Overrides the configuration directory.</param>
    /// <param name="dataDirectory">Overrides the state directory.</param>
    /// <param name="cacheDirectory">Overrides the cache directory.</param>
    public CangerPaths(string? configDirectory = null, string? dataDirectory = null,
                       string? cacheDirectory = null)
    {
        ConfigDirectory = configDirectory ?? XdgDirectory("XDG_CONFIG_HOME", ".config");
        DataDirectory = dataDirectory ?? XdgDirectory("XDG_DATA_HOME", Path.Join(".local", "share"));
        CacheDirectory = cacheDirectory ?? XdgDirectory("XDG_CACHE_HOME", ".cache");
    }

    /// <summary>Where user-edited configuration lives, by default <c>~/.config/canger</c>.</summary>
    public string ConfigDirectory { get; }

    /// <summary>Where Canger's own state lives, by default <c>~/.local/share/canger</c>.</summary>
    public string DataDirectory { get; }

    /// <summary>Where cached previews live, by default <c>~/.cache/canger</c>.</summary>
    public string CacheDirectory { get; }

    /// <summary>The system-wide configuration directory, read before the user's.</summary>
    public static string SystemConfigDirectory { get; } = Path.Join("/etc", ApplicationName);

    /// <summary>The directory Canger itself is installed in, holding the shipped defaults.</summary>
    public static string InstallDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>Path to a file in the configuration directory.</summary>
    /// <param name="parts">Path components below the configuration directory.</param>
    /// <returns>The joined path.</returns>
    public string Config(params string[] parts) => Path.Join([ConfigDirectory, .. parts]);

    /// <summary>Path to a file in the state directory.</summary>
    /// <param name="parts">Path components below the state directory.</param>
    /// <returns>The joined path.</returns>
    public string Data(params string[] parts) => Path.Join([DataDirectory, .. parts]);

    /// <summary>Path to a file in the cache directory.</summary>
    /// <param name="parts">Path components below the cache directory.</param>
    /// <returns>The joined path.</returns>
    public string Cache(params string[] parts) => Path.Join([CacheDirectory, .. parts]);

    /// <summary>
    /// The cc.conf files to load, in order, skipping any that do not exist.
    /// </summary>
    /// <remarks>
    /// Later files add to and override earlier ones rather than replacing them, so a personal
    /// cc.conf only needs the lines the user wants to change. Setting
    /// <c>CANGER_LOAD_DEFAULT_CC=FALSE</c> omits the shipped defaults, in which case the user
    /// takes responsibility for every setting.
    /// </remarks>
    /// <returns>The configuration files to source, in load order.</returns>
    public IEnumerable<string> ConfigurationFiles()
    {
        bool loadDefaults = !string.Equals(
            Environment.GetEnvironmentVariable("CANGER_LOAD_DEFAULT_CC"), "FALSE",
            StringComparison.OrdinalIgnoreCase);

        if (loadDefaults)
        {
            yield return Path.Join(InstallDirectory, "config", "cc.conf");
        }

        yield return Path.Join(SystemConfigDirectory, "cc.conf");
        yield return Config("cc.conf");
    }

    private static string XdgDirectory(string variable, string fallbackRelativeToHome)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);
        string root = string.IsNullOrEmpty(configured)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        fallbackRelativeToHome)
            : configured;

        return Path.Join(root, ApplicationName);
    }
}
