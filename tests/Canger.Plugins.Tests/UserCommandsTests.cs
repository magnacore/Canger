// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// The ported ranger configuration kept in <c>artifacts/</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are a real user's 48 commands and two ported ranger plugins, installed into
/// <c>~/.config/canger</c> and used daily. They are plugin sources, so nothing in the solution
/// references them and no compiler ever sees them — which is exactly how the shipped
/// <c>commands.cs</c> came to ship broken for weeks.
/// </para>
/// <para>
/// The copies in <c>artifacts/</c> are what these tests compile, so a change to the plugin API
/// that would break them fails here instead of at someone's next launch.
/// </para>
/// </remarks>
public sealed class UserCommandsTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-user-cmds-" + Path.GetRandomFileName());

    public UserCommandsTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
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

    /// <summary>Finds the artifacts directory beside the solution.</summary>
    private static string Artifacts()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Join(directory.FullName, "artifacts");

            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")) &&
                Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    private (PluginHost Host, CommandRegistry Registry) Load()
    {
        CommandRegistry registry = new();
        CommandDispatcher.RegisterBuiltins(registry);

        PluginHost host = new(registry, null, new ScriptCompiler());
        host.LoadFrom(_root);

        return (host, registry);
    }

    [Fact]
    public void TheCommandsFileCompilesAndRegistersEveryCommand()
    {
        string artifacts = Artifacts();
        Assert.SkipWhen(artifacts.Length == 0, "the repository layout was not found");

        string source = Path.Join(artifacts, "commands.cs");
        Assert.SkipWhen(!File.Exists(source), "no ported commands.cs in artifacts");

        File.Copy(source, Path.Join(_root, "commands.cs"));

        (PluginHost host, CommandRegistry registry) = Load();
        PluginLoad load = Assert.Single(host.Loads);

        Assert.True(load.Succeeded,
                    "the ported commands.cs did not compile: "
                    + string.Join("; ", load.Diagnostics ?? []));

        // One per [Command] attribute in the file: a command that compiles but is not registered
        // is a command whose key does nothing, which is the failure this whole exercise was about.
        int declared = File.ReadAllLines(source).Count(l => l.TrimStart().StartsWith("[Command(",
                                                                StringComparison.Ordinal));

        Assert.Equal(declared, load.Commands);
        Assert.True(registry.Find("fzf_select") is not null);
        Assert.True(registry.Find("mkdirmv") is not null);
        Assert.True(registry.Find("toggle_flat") is not null);
    }

    [Fact]
    public void ThePortedPluginsCompileAndRegisterTheirCommandsAndHooks()
    {
        string artifacts = Artifacts();
        Assert.SkipWhen(artifacts.Length == 0, "the repository layout was not found");

        string source = Path.Join(artifacts, "plugins");
        Assert.SkipWhen(!Directory.Exists(source), "no ported plugins in artifacts");

        Directory.CreateDirectory(Path.Join(_root, "plugins"));

        foreach (string file in Directory.EnumerateFiles(source, "*.cs"))
        {
            File.Copy(file, Path.Join(_root, "plugins", Path.GetFileName(file)));
        }

        (PluginHost host, CommandRegistry registry) = Load();
        PluginLoad load = Assert.Single(host.Loads);

        Assert.True(load.Succeeded,
                    "the ported plugins did not compile: "
                    + string.Join("; ", load.Diagnostics ?? []));

        // zoxide's `zi` alias is created by its hook, so the hook has to be found for the binding
        // in cc.conf to mean anything.
        Assert.True(load.Plugins > 0, "no ICangerPlugin was found");

        Assert.True(registry.Find("z") is not null);
        Assert.True(registry.Find("extract_to_dirs") is not null);
        Assert.True(registry.Find("compress") is not null);
    }

    [Fact]
    public void ExtractQueuesTheArchiverInsteadOfTakingTheTerminal()
    {
        // The defect this replaced: `extract` ran the archiver with the `w` flag, which hands
        // over the terminal and holds it. On a large archive the screen went blank for as long as
        // it took, nothing else could be done, and there was no sign anything was happening.
        // Ranger has always run it through the loader (`ranger-archives/extract.py`).
        (FakeFileManager manager, bool loaded) = WithPlugins("/home/notes.zip");
        Assert.SkipWhen(!loaded, "no ported plugins in artifacts");

        manager.Execute("extract");

        Assert.Empty(manager.LaunchedPrograms);
        (string description, string command, string? directory) =
            Assert.Single(manager.BackgroundWork);

        Assert.Equal("Extracting: notes.zip", description);
        Assert.Contains("notes.zip", command, StringComparison.Ordinal);
        Assert.Equal("/home", directory);
    }

    [Fact]
    public void ExtractRereadsTheDirectoryTheFilesLandedIn()
    {
        // Ranger's `refresh` closure, which captures `cwd` rather than asking for the current
        // directory later — the work finishes long after the command returned, by which time the
        // user may be looking at somewhere else entirely.
        (FakeFileManager manager, bool loaded) = WithPlugins("/home/notes.zip");
        Assert.SkipWhen(!loaded, "no ported plugins in artifacts");

        manager.Execute("extract");
        Assert.Empty(manager.Reloaded);

        while (manager.Tasks.HasWork)
        {
            manager.Tasks.Work(TimeSpan.Zero);
        }

        Assert.Equal(["/home"], manager.Reloaded);
    }

    [Fact]
    public void ExtractQueuesOneTaskPerArchive()
    {
        // One per file, as the original does, so a single bad archive does not take the rest
        // down with it.
        (FakeFileManager manager, bool loaded) = WithPlugins("/home/a.zip", "/home/b.zip");
        Assert.SkipWhen(!loaded, "no ported plugins in artifacts");

        manager.CurrentTab.Current.SetAllMarked(true);
        manager.Execute("extract");

        Assert.Equal(2, manager.BackgroundWork.Count);
    }

    /// <summary>Builds a manager with the ported plugins loaded into its own registry.</summary>
    /// <param name="files">Files to put in <c>/home</c>; the first is under the cursor.</param>
    /// <returns>The manager, and whether the plugins were there to load.</returns>
    private (FakeFileManager Manager, bool Loaded) WithPlugins(params string[] files)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home");

        foreach (string file in files)
        {
            fs.AddFile(file);
        }

        FakeFileManager manager = new(fs, "/home");

        string artifacts = Artifacts();
        string source = Path.Join(artifacts, "plugins");

        if (artifacts.Length == 0 || !Directory.Exists(source))
        {
            return (manager, false);
        }

        Directory.CreateDirectory(Path.Join(_root, "plugins"));

        foreach (string file in Directory.EnumerateFiles(source, "*.cs"))
        {
            File.Copy(file, Path.Join(_root, "plugins", Path.GetFileName(file)), overwrite: true);
        }

        PluginHost host = new(manager.Commands, null, new ScriptCompiler());
        host.LoadFrom(_root);

        return (manager, true);
    }

    [Fact]
    public void TheCommandsFileDoesNotShadowACommandCangerAlreadyHas()
    {
        // mark_tag, unmark_tag and paste_ext are built in. A copy here would compile and register
        // over the real one, which is a silent downgrade rather than an error.
        string artifacts = Artifacts();
        Assert.SkipWhen(artifacts.Length == 0, "the repository layout was not found");

        string source = Path.Join(artifacts, "commands.cs");
        Assert.SkipWhen(!File.Exists(source), "no ported commands.cs in artifacts");

        string text = File.ReadAllText(source);

        foreach (string builtin in new[] { "mark_tag", "unmark_tag", "paste_ext" })
        {
            Assert.DoesNotContain($"[Command(\"{builtin}\"", text, StringComparison.Ordinal);
        }
    }
}
