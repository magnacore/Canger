// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Plugins;

namespace Canger.Plugins.Tests;

/// <summary>
/// The <c>commands.cs</c> Canger ships.
/// </summary>
/// <remarks>
/// It shipped for a long while written against an API that never existed — a <c>Tab(int)</c>
/// override with the wrong signature and a method on the file manager that was never added. Anyone
/// who copied it, which is exactly what it is for, got a compile error and no custom commands at
/// all. Nothing caught it because nothing ever compiled it.
/// </remarks>
public sealed class ShippedCommandsTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-shipped-" + Path.GetRandomFileName());

    public ShippedCommandsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    /// <summary>Finds the shipped configuration beside the repository root.</summary>
    private static string ConfigDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Join(directory.FullName, "config");

            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")) &&
                Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    [Fact]
    public void TheShippedSampleCompilesAndRegistersItsCommands()
    {
        string config = ConfigDirectory();
        Assert.SkipWhen(config.Length == 0, "the repository layout was not found");

        string source = Path.Join(config, "commands.cs");
        Assert.True(File.Exists(source), $"{source} is missing");

        // Copied into a directory of its own, so the host loads exactly this one file the way it
        // would load a user's.
        File.Copy(source, Path.Join(_root, "commands.cs"));

        CommandRegistry registry = new();
        CommandDispatcher.RegisterBuiltins(registry);
        int before = registry.Count;

        PluginHost host = new(registry, null, new ScriptCompiler());
        host.LoadFrom(_root);

        PluginLoad load = Assert.Single(host.Loads);

        Assert.True(load.Succeeded,
                    "the shipped commands.cs did not compile: " +
                    string.Join("; ", load.Diagnostics ?? []));

        // A sample that compiles but defines nothing would be no sample at all.
        Assert.True(load.Commands > 0, "the shipped commands.cs defined no commands");
        Assert.True(registry.Count > before);
    }

    [Fact]
    public void TheShippedSampleNeedsNoUsingDirectives()
    {
        // The file tells the reader which namespaces are already in scope. If it also had to
        // declare them, that claim would be wrong.
        string config = ConfigDirectory();
        Assert.SkipWhen(config.Length == 0, "the repository layout was not found");

        string text = File.ReadAllText(Path.Join(config, "commands.cs"));

        Assert.DoesNotContain("\nusing ", text, StringComparison.Ordinal);
    }
}
