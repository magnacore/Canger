// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Canger.Core.Commands;
using Canger.Core.Model;

namespace Canger.Plugins;

/// <summary>
/// What happened while loading one plugin.
/// </summary>
/// <param name="Name">What it was loaded from.</param>
/// <param name="Commands">How many commands it added.</param>
/// <param name="Linemodes">How many linemodes it added.</param>
/// <param name="Plugins">How many <see cref="ICangerPlugin"/> implementations it holds.</param>
/// <param name="Diagnostics">Compiler output, if it was compiled.</param>
/// <param name="Error">Why it could not be loaded at all, if it could not be.</param>
public sealed record PluginLoad(
    string Name,
    int Commands = 0,
    int Linemodes = 0,
    int Plugins = 0,
    IReadOnlyList<string>? Diagnostics = null,
    string? Error = null)
{
    /// <summary>Whether the plugin loaded.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Finds, compiles and loads plugins.
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of thing are loaded, all from <c>~/.config/canger</c>: <c>commands.cs</c>, a single
/// file for the commands a user wants without the ceremony of a plugin; <c>plugins/*.cs</c>,
/// compiled together so a plugin can span files; and <c>plugins/*.dll</c>, already compiled.
/// Entries whose name starts with an underscore are skipped, which is how a plugin is disabled
/// without deleting it.
/// </para>
/// <para>
/// A plugin that fails to compile is reported and skipped. It must not stop Canger starting:
/// the file manager is how the user would go and fix the file.
/// </para>
/// </remarks>
/// <param name="registry">Where discovered commands are registered.</param>
/// <param name="linemodes">Where discovered linemodes are registered.</param>
/// <param name="compiler">Compiles the C# sources.</param>
public sealed class PluginHost(
    CommandRegistry registry,
    LinemodeRegistry? linemodes = null,
    ScriptCompiler? compiler = null)
{
    private readonly ScriptCompiler _compiler = compiler ?? new ScriptCompiler();
    private readonly List<ICangerPlugin> _plugins = [];

    /// <summary>What happened to each thing that was loaded, in load order.</summary>
    public List<PluginLoad> Loads { get; } = [];

    /// <summary>The plugin objects that were found, in load order.</summary>
    public IReadOnlyList<ICangerPlugin> Plugins => _plugins;

    /// <summary>
    /// Loads everything in a configuration directory.
    /// </summary>
    /// <param name="configDirectory">Usually <c>~/.config/canger</c>.</param>
    public void LoadFrom(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);

        string commandsFile = Path.Join(configDirectory, "commands.cs");

        if (File.Exists(commandsFile))
        {
            LoadSources(Named("commands", configDirectory), [commandsFile]);
        }

        string pluginDirectory = Path.Join(configDirectory, "plugins");

        if (!Directory.Exists(pluginDirectory))
        {
            return;
        }

        // Sorted so the load order is the same on every machine; a plugin that depends on
        // another loading first can then rely on naming to arrange it.
        string[] sources = [.. Enabled(pluginDirectory, "*.cs").Order(StringComparer.Ordinal)];

        if (sources.Length > 0)
        {
            LoadSources(Named("plugins", pluginDirectory), sources);
        }

        foreach (string library in Enabled(pluginDirectory, "*.dll").Order(StringComparer.Ordinal))
        {
            LoadLibrary(library);
        }
    }

    /// <summary>
    /// Names a compilation after where it came from as well as what it is.
    /// </summary>
    /// <param name="kind">Which file: <c>commands</c> or <c>plugins</c>.</param>
    /// <param name="directory">The directory it was read from.</param>
    /// <returns>A name unique to that pairing.</returns>
    /// <remarks>
    /// The shipped <c>commands.cs</c> and the user's are both called <c>commands.cs</c>. Naming
    /// the compilation after the file alone gave them the same cache slot, so each deleted the
    /// other's cached build as stale and *both* recompiled on every launch — well over a second
    /// of Roslyn on a file that had not changed.
    /// </remarks>
    private static string Named(string kind, string directory)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(directory)));

        return $"{kind}-{Convert.ToHexStringLower(hash)[..8]}";
    }

    /// <summary>Tells every plugin that Canger is starting.</summary>
    /// <param name="fileManager">What the plugins act on.</param>
    public void NotifyInit(IFileManager fileManager) =>
        Notify(fileManager, static (plugin, fm) => plugin.OnInit(fm));

    /// <summary>Tells every plugin that the interface is up.</summary>
    /// <param name="fileManager">What the plugins act on.</param>
    public void NotifyReady(IFileManager fileManager) =>
        Notify(fileManager, static (plugin, fm) => plugin.OnReady(fm));

    /// <summary>Files a plugin directory offers, minus the disabled ones.</summary>
    private static IEnumerable<string> Enabled(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern)
                 .Where(f => !Path.GetFileName(f).StartsWith('_'));

    /// <summary>Compiles a set of sources and registers what they contain.</summary>
    private void LoadSources(string name, IReadOnlyList<string> files)
    {
        CompilationResult result = _compiler.Compile(name, files);

        if (!result.Succeeded)
        {
            Loads.Add(new PluginLoad(name, Diagnostics: result.Diagnostics,
                                     Error: "did not compile"));
            return;
        }

        Loads.Add(Register(name, result.Assembly!) with { Diagnostics = result.Diagnostics });
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "A plugin is third-party code. Anything it throws while "
                                   + "loading is reported against that plugin rather than being "
                                   + "allowed to stop Canger starting.")]
    private void LoadLibrary(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);

        try
        {
            PluginLoadContext context = new(name);
            Loads.Add(Register(name, context.LoadFromAssemblyPath(Path.GetFullPath(path))));
        }
        catch (Exception e)
        {
            Loads.Add(new PluginLoad(name, Error: e.Message));
        }
    }

    /// <summary>Registers everything an assembly offers.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "As above: a plugin's own failure is its own.")]
    private PluginLoad Register(string name, Assembly assembly)
    {
        try
        {
            int commands = registry.RegisterAll(assembly);
            int modes = 0;
            int found = 0;

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                if (linemodes is not null && typeof(ILinemode).IsAssignableFrom(type))
                {
                    linemodes.Register((ILinemode)Activator.CreateInstance(type)!);
                    modes++;
                }

                if (typeof(ICangerPlugin).IsAssignableFrom(type))
                {
                    _plugins.Add((ICangerPlugin)Activator.CreateInstance(type)!);
                    found++;
                }
            }

            return new PluginLoad(name, commands, modes, found);
        }
        catch (ReflectionTypeLoadException e)
        {
            // Says which type could not be loaded, which is far more use than the summary
            // message this exception carries by default.
            string detail = string.Join("; ",
                e.LoaderExceptions.Where(x => x is not null).Select(x => x!.Message).Distinct());

            return new PluginLoad(name, Error: detail.Length > 0 ? detail : e.Message);
        }
        catch (Exception e)
        {
            return new PluginLoad(name, Error: e.Message);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "A plugin throwing from a hook must not take the session "
                                   + "with it; the failure is reported and the rest still run.")]
    private void Notify(IFileManager fileManager, Action<ICangerPlugin, IFileManager> hook)
    {
        ArgumentNullException.ThrowIfNull(fileManager);

        foreach (ICangerPlugin plugin in _plugins)
        {
            try
            {
                hook(plugin, fileManager);
            }
            catch (Exception e)
            {
                fileManager.Notify($"{plugin.GetType().Name}: {e.Message}", isError: true);
            }
        }
    }
}
