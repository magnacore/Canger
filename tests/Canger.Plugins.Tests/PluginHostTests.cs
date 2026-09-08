// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.Model;
using Canger.Plugins;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// Compiling and loading a user's own C#, which is how Canger is extended.
/// </summary>
public class PluginHostTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-plugins-" + Path.GetRandomFileName());

    public PluginHostTests() => Directory.CreateDirectory(_root);

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

        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a file into the fake configuration directory.</summary>
    private string Write(string relativePath, string content)
    {
        string path = Path.Join(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A command with no namespace or usings, as a plugin author would write it.</summary>
    /// <param name="name">The command name, which is matched exactly and so stays lower case.</param>
    /// <param name="message">What it says when run.</param>
    private static string CommandSource(string name, string message) =>
        $$"""
          [Command("{{name}}", Summary = "A command from a test.")]
          public sealed class {{char.ToUpperInvariant(name[0]) + name[1..]}}Command : CangerCommand
          {
              public override void Execute() => FileManager.Notify("{{message}}");
          }
          """;

    private static PluginHost Host(CommandRegistry registry, LinemodeRegistry? linemodes = null,
                                   string? cache = null) =>
        new(registry, linemodes, new ScriptCompiler(cache));

    [Fact]
    public void LoadFrom_CompilesCommandsFileAndRegistersWhatItDefines()
    {
        Write("commands.cs", CommandSource("hello", "hello"));

        CommandRegistry registry = new();
        PluginHost host = Host(registry);
        host.LoadFrom(_root);

        Assert.All(host.Loads, load => Assert.True(load.Succeeded, load.Error));
        Assert.NotNull(registry.Find("hello"));
    }

    [Fact]
    public void LoadFrom_NeedsNoUsingsForTheTypesAPluginActuallyUses()
    {
        // A plugin should be a few lines, not a preamble. The implicit namespaces have to go in
        // as global usings: the compilation options have a `usings` field, but it applies only
        // to script compilations and is silently ignored by a regular one.
        Write("commands.cs", CommandSource("terse", "terse"));

        CommandRegistry registry = new();
        Host(registry).LoadFrom(_root);

        Assert.NotNull(registry.Find("terse"));
    }

    [Fact]
    public void LoadFrom_CompilesPluginFilesTogetherSoTheyCanReferToEachOther()
    {
        Write("plugins/shared.cs",
              "public static class Shared { public const string Text = \"from shared\"; }");
        Write("plugins/user.cs",
              """
              [Command("shared_text", Summary = "Uses another file.")]
              public sealed class SharedTextCommand : CangerCommand
              {
                  public override void Execute() => FileManager.Notify(Shared.Text);
              }
              """);

        CommandRegistry registry = new();
        PluginHost host = Host(registry);
        host.LoadFrom(_root);

        Assert.All(host.Loads, load => Assert.True(load.Succeeded, load.Error));
        Assert.NotNull(registry.Find("shared_text"));
    }

    [Fact]
    public void LoadFrom_SkipsFilesWhoseNameStartsWithAnUnderscore()
    {
        // This is how a plugin is disabled without deleting it, so the file must not even be
        // compiled — it is very often broken, which is why it was disabled.
        Write("plugins/_broken.cs", "this is not C# at all");
        Write("plugins/working.cs", CommandSource("working", "working"));

        CommandRegistry registry = new();
        PluginHost host = Host(registry);
        host.LoadFrom(_root);

        Assert.All(host.Loads, load => Assert.True(load.Succeeded, load.Error));
        Assert.NotNull(registry.Find("working"));
    }

    [Fact]
    public void LoadFrom_ReportsACompilerErrorRatherThanThrowing()
    {
        // Canger is how the user would go and fix the file, so it has to still start.
        Write("commands.cs", "public class Broken { this is not valid }");

        CommandRegistry registry = new();
        PluginHost host = Host(registry);
        host.LoadFrom(_root);

        PluginLoad load = Assert.Single(host.Loads);
        Assert.False(load.Succeeded);
        Assert.NotEmpty(load.Diagnostics!);
    }

    [Fact]
    public void LoadFrom_DoesNothingWhenThereIsNothingToLoad()
    {
        CommandRegistry registry = new();
        PluginHost host = Host(registry);
        host.LoadFrom(_root);

        Assert.Empty(host.Loads);
    }

    [Fact]
    public void LoadFrom_RegistersLinemodesAPluginDefines()
    {
        Write("plugins/mode.cs",
              """
              public sealed class ShoutingLinemode : ILinemode
              {
                  public string Name => "shouting";

                  public string Title(FsNode node, Canger.Core.State.FileMetadata metadata,
                                      in LinemodeContext context) =>
                      node.RelativePath.ToUpperInvariant();
              }
              """);

        LinemodeRegistry linemodes = new();
        PluginHost host = Host(new CommandRegistry(), linemodes);
        host.LoadFrom(_root);

        Assert.All(host.Loads, load => Assert.True(load.Succeeded, load.Error));
        Assert.NotNull(linemodes.Find("shouting"));
    }

    [Fact]
    public void LoadFrom_FindsPluginHooks()
    {
        Write("plugins/hook.cs",
              """
              public sealed class TestPlugin : ICangerPlugin
              {
                  public void OnInit(IFileManager fm) => fm.Notify("init");
                  public void OnReady(IFileManager fm) => fm.Notify("ready");
              }
              """);

        PluginHost host = Host(new CommandRegistry());
        host.LoadFrom(_root);

        Assert.Single(host.Plugins);
    }

    [Fact]
    public void NotifyInit_AndNotifyReady_RunTheHooksInTurn()
    {
        Write("plugins/hook.cs",
              """
              public sealed class TestPlugin : ICangerPlugin
              {
                  public void OnInit(IFileManager fm) => fm.Notify("init");
                  public void OnReady(IFileManager fm) => fm.Notify("ready");
              }
              """);

        PluginHost host = Host(new CommandRegistry());
        host.LoadFrom(_root);

        FakeFileManager manager = new(new InMemoryFileSystem().AddDirectory("/home"), "/home");

        host.NotifyInit(manager);
        host.NotifyReady(manager);

        Assert.Contains(manager.Messages, m => m.Message == "init");
        Assert.Contains(manager.Messages, m => m.Message == "ready");
    }

    [Fact]
    public void NotifyInit_SurvivesAPluginThatThrows()
    {
        // Third-party code failing must not take the session with it.
        Write("plugins/bad.cs",
              """
              public sealed class ThrowingPlugin : ICangerPlugin
              {
                  public void OnInit(IFileManager fm) =>
                      throw new InvalidOperationException("deliberate");
              }
              """);

        PluginHost host = Host(new CommandRegistry());
        host.LoadFrom(_root);

        FakeFileManager manager = new(new InMemoryFileSystem().AddDirectory("/home"), "/home");
        host.NotifyInit(manager);

        Assert.Contains(manager.Messages, m => m.IsError && m.Message.Contains("deliberate",
                                                                               StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ReusesACachedBuildForUnchangedSource()
    {
        // An unchanged configuration should add nothing to startup after the first run.
        string cache = Path.Join(_root, "cache");
        Write("commands.cs", CommandSource("cached", "cached"));

        Assert.False(Host(new CommandRegistry(), cache: cache) is var _ &&
                     FirstLoadWasCached(cache));

        Assert.True(FirstLoadWasCached(cache));
    }

    /// <summary>Loads once and says whether the compilation came from the cache.</summary>
    private bool FirstLoadWasCached(string cache)
    {
        ScriptCompiler compiler = new(cache);
        return compiler.Compile("commands", [Path.Join(_root, "commands.cs")]).FromCache;
    }

    [Fact]
    public void Compile_RecompilesWhenTheSourceChanges()
    {
        string cache = Path.Join(_root, "cache");
        string file = Write("commands.cs", CommandSource("first", "first"));

        ScriptCompiler compiler = new(cache);
        Assert.True(compiler.Compile("commands", [file]).Succeeded);
        Assert.True(compiler.Compile("commands", [file]).FromCache);

        File.WriteAllText(file, CommandSource("second", "second"));

        CompilationResult result = compiler.Compile("commands", [file]);
        Assert.False(result.FromCache);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Compile_ReportsNothingToDoForAnEmptyFileList()
    {
        CompilationResult result = new ScriptCompiler().Compile("nothing", []);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Diagnostics);
    }
}

/// <summary>
/// What a plugin is allowed to reference.
/// </summary>
/// <remarks>
/// The reference set was everything Canger had <em>loaded</em>, which is not the same as
/// everything it can use: the runtime loads an assembly when something first needs it, so a
/// plugin asking for <c>System.Net.Sockets</c> was told the namespace does not exist merely
/// because Canger had never opened a socket. A plugin's reach depended on what the program
/// happened to have done first, which is not a rule anyone could work with.
/// </remarks>
public class PluginReferenceTests
{
    private static CompilationResult Compile(string source)
    {
        string directory = Path.Join(Path.GetTempPath(), "canger-refs-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            string file = Path.Join(directory, "plugin.cs");
            File.WriteAllText(file, source);

            return new ScriptCompiler().Compile("refs", [file]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // `System.Net.Sockets` is the one that prompted this — an mpv control socket — but it is not
    // listed, because the test host loads it for its own reasons and the case would pass with the
    // fix removed. These two do not resolve without it.
    [Theory]
    [InlineData("System.Net.Http", "HttpClient")]
    [InlineData("System.Text.Json", "JsonSerializerOptions")]
    public void APluginMayNameAFrameworkTypeCangerHasNeverTouched(string space, string type)
    {
        // Named types rather than a namespace check, because it is the type that has to resolve.
        CompilationResult result = Compile($$"""
            using {{space}};

            internal static class Probe
            {
                internal static string Name => typeof({{type}}).Name;
            }
            """);

        Assert.True(result.Succeeded,
                    $"{space}.{type} did not resolve: {string.Join("; ", result.Diagnostics)}");
    }

    [Fact]
    public void APluginStillCannotNameSomethingThatDoesNotExist()
    {
        // The other half: a wider reference set must not have become a compiler that accepts
        // anything, or the test above would pass for the wrong reason.
        CompilationResult result = Compile("""
            internal static class Probe
            {
                internal static string Name => typeof(System.Net.NoSuchTypeAtAll).Name;
            }
            """);

        Assert.False(result.Succeeded);
    }
}
