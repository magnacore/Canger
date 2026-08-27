// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.Configuration;
using Canger.Core.FileSystem;
using Canger.Core.Input;
using Canger.Core.Model;
using Canger.Core.Processes;
using Canger.Plugins;
using Canger.Preview;
using Canger.Preview.Images;
using Canger.Rifle;
using Canger.Core.Settings;
using Canger.Core.State;
using Canger.Tui;
using Canger.Ui;

namespace Canger.App;

/// <summary>Process entry point and composition root.</summary>
internal static class Program
{
    /// <summary>Runs Canger.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A process exit code.</returns>
    internal static int Main(string[] args)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);

        foreach (string unknown in options.Unknown)
        {
            // A mistyped flag that silently does nothing is worse than one that says so.
            Console.Error.WriteLine($"canger: unrecognised option: {unknown}");
        }

        if (options.Unknown.Count > 0)
        {
            Console.Error.WriteLine("canger: try --help");
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(CommandLineOptions.Usage);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine($"canger {Version}");
            return 0;
        }

        // Published so a binding or a shell command can reach the shipped configuration and
        // scripts by name — `cd $CANGER_INSTALL_DIR` rather than a path that differs per install.
        // Child processes inherit it too, which is what makes it useful from rifle and :shell.
        Environment.SetEnvironmentVariable("CANGER_INSTALL_DIR", CangerPaths.InstallDirectory);

        // --selectfile names a file, but Canger opens a directory: the file's own directory is
        // where to start, with the cursor put on it once the listing is loaded.
        if (options.SelectFile is { } selected)
        {
            options.Paths.Insert(0, Path.GetDirectoryName(Path.GetFullPath(selected)) ?? "/");
        }

        string path = options.Paths.Count > 0
            ? Path.GetFullPath(options.Paths[0])
            : Directory.GetCurrentDirectory();

        // Everything after the first opens a tab of its own. Ranger builds one tab per start
        // path (`core/fm.py:127`); Canger read `Paths[0]` and dropped the rest, so `canger a b c`
        // silently opened only `a` despite the usage line saying `[path ...]`.
        List<string> startPaths = [.. options.Paths.Skip(1).Select(Path.GetFullPath)];

        IFileSystem fileSystem = LocalFileSystem.Instance;

        if (!fileSystem.DirectoryExists(path))
        {
            Console.Error.WriteLine($"canger: not a directory: {path}");
            return 1;
        }

        // Build the configuration before anything else: it decides how the browser behaves, and
        // the diagnostic modes below exist to inspect the result of exactly this step.
        CangerPaths paths = new(options.ConfigDirectory, options.DataDirectory,
                                options.CacheDirectory);

        Environment.SetEnvironmentVariable("CANGER_CONFIG_DIR", paths.ConfigDirectory);

        if (options.CopyConfig is { } which)
        {
            return CopyConfiguration(paths, which);
        }
        SettingsStore store = new();
        CangerSettings settings = new(store);
        KeyMaps keyMaps = new();

        CommandRegistry commands = new();
        CommandDispatcher.RegisterBuiltins(commands);

        // Plugins load before the configuration is read, so a cc.conf line can bind a key to a
        // command a plugin defines. A plugin that fails to compile is reported and skipped —
        // Canger is how the user would go and fix the file.
        LinemodeRegistry linemodes = new();
        ScriptCompiler compiler = new(paths.Cache("plugins"));
        PluginHost pluginHost = new(commands, linemodes, compiler);

        if (!options.Clean)
        {
            // The shipped commands.cs is loaded before the user's, exactly as cc.conf is and
            // exactly as ranger loads its own commands.py before a personal one. Without this
            // the file Canger ships is never loaded at all — it is only a thing to copy, which
            // is not what the rest of the configuration does.
            //
            // Inside the --clean guard, because it was outside it: `canger --clean --config`
            // reported two plugin commands, and --clean is meant to leave nothing loaded that
            // could be the thing being diagnosed.
            pluginHost.LoadFrom(Path.Join(CangerPaths.InstallDirectory, "config"));

            pluginHost.LoadFrom(paths.ConfigDirectory);
        }

        // `eval` needs the compiler, so it exists only once the plugin host does. Registering the
        // command here rather than among the built-ins keeps Canger.Core free of a Roslyn
        // dependency it would otherwise carry for one command.
        EvalHost evalHost = new(compiler);
        EvalCommand.Host = evalHost;
        commands.Register<EvalCommand>();

        ConfigurationReader reader = new();
        reader.Register(new SetDirective(store));
        reader.Register(new KeyBindingDirective(keyMaps));
        reader.Register(new AliasDirective(commands));

        // A configuration-time snippet's `cmd` feeds another line back through the reader, which
        // is what lets one `eval` generate a family of bindings.
        reader.Register(new EvalDirective(evalHost, line => reader.ReadLines([line], "eval")));

        // Anything no directive claims is an ordinary command — which is what cc.conf lines are.
        // They cannot run yet, because most of them act on an interface that does not exist until
        // the terminal is open, so they are kept in order and run once it does. This is what
        // makes `default_linemode`, and any command a plugin defines, usable in a config file.
        List<string> deferred = [];

        reader.Fallback = line =>
        {
            if (commands.Find(line.Split(' ', 2)[0]) is null)
            {
                return false;
            }

            deferred.Add(line);
            return true;
        };

        // --clean still reads the shipped defaults: without them there are no key bindings at
        // all, which is not "clean" so much as unusable. It is the user's own files that are
        // skipped, along with their plugins and saved state.
        reader.ReadFiles(options.Clean
            ? [Path.Join(CangerPaths.InstallDirectory, "config", "cc.conf")]
            : paths.ConfigurationFiles());

        // --cmd runs after the configuration, so it can override anything the files set. That is
        // the whole point: it is how a script says "like my usual setup, but sorted by size".
        foreach (string command in options.Commands)
        {
            reader.ReadLines([command], "--cmd");
        }

        if (options.ShowOnlyDirectories)
        {
            store.Set("show_only_dirs", true);
        }

        DirectoryCache cache = new(fileSystem);

        if (options.ReportConfiguration)
        {
            return ReportConfiguration(paths, reader, settings, keyMaps, commands, pluginHost);
        }

        if (options.ListOnly)
        {
            return ListDirectory(cache, settings, path);
        }

        if (options.KeyProbe)
        {
            return KeyProbe.Run(keyMaps);
        }

        if (options.ManPage)
        {
            // Generated from the same metadata the program uses, so the reference sections
            // cannot drift out of step with it.
            Console.Write(ManPage.Render(Version, keyMaps, commands));
            return 0;
        }

        if (options.ListTaggedFiles is { } tag)
        {
            return ListTaggedFiles(paths, tag);
        }

        try
        {
            using Terminal terminal = new(enableMouse: settings.MouseEnabled);

            TerminalProcessRunner runner = new(terminal);

            // In chooser mode nothing is ever launched: opening a file writes its path and quits.
            // Substituting the opener covers every route to opening a file at once.
            Browser? browserForQuit = null;

            // The preview worker finishes after the browser has moved on, so it asks for a
            // redraw rather than returning a value to anyone.
            ScriptPreviewProvider previews = BuildPreviewProvider(
                paths, settings, fileSystem, () => browserForQuit?.RequestRedraw());

            IFileOpener opener = options.IsChooser
                ? new ChooserFileOpener(options.ChooseFiles ?? options.ChooseFile!,
                                        multiple: options.ChooseFiles is not null,
                                        () => browserForQuit?.Quit())
                : new RifleLauncher(LoadRifleRules(paths, options.Clean), runner, fileSystem);

            // --clean keeps neither bookmarks nor tags, and writes nothing back. /dev/null is
            // the path that makes both of those true without a flag threaded through each.
            Bookmarks bookmarks = new(fileSystem,
                                      options.Clean ? "/dev/null" : paths.Data("bookmarks"))
            {
                AutoSave = !options.Clean && settings.AutosaveBookmarks,
                SaveBacktickBookmark = settings.SaveBacktickBookmark,
            };

            Tags tags = new(paths.Data("tagged")) { Persistent = !options.Clean };

            if (!options.Clean)
            {
                bookmarks.Load();
                tags.Reload();
            }

            using IImageDisplay images = settings.PreviewImages
                ? ImageDisplayFactory.Create(settings.PreviewImagesMethod, Console.Out)
                : new NoImageDisplay();

            using Browser browser = new(terminal, settings, keyMaps, commands, cache, path,
                                        fileSystem, runner, opener,
                                        previews,
                                        images, bookmarks, tags, linemodes);

            // The console history lives beside the bookmarks and tags, in the same file ranger
            // uses. --clean reads none and writes none.
            browser.ConsoleHistoryPath = options.Clean ? null : paths.Data("history");
            browser.LoadConsoleHistory();

            browserForQuit = browser;

            // Needs both the launcher and the browser, so it is wired here where both exist —
            // which is also where ranger installs it, on the way up rather than inside either.
            ImageViewerHandover.Install(opener, browser);
            pluginHost.NotifyInit(browser);

            // Tabs remembered from the last session, but only when the user has not said where
            // to start — an explicit path is an instruction, not a suggestion. Ranger applies
            // the same condition (`core/main.py:161`), and neither restores under --clean.
            if (!options.Clean && settings.SaveTabsOnExit && options.Paths.Count == 0)
            {
                startPaths.AddRange(
                    SavedTabs.Take(paths.Data("tabs"), settings.FilterDeadTabsOnStartup)
                             .Where(p => !string.Equals(p, path, StringComparison.Ordinal)));
            }

            browser.Ready += (_, _) =>
            {
                foreach (string line in deferred)
                {
                    browser.Execute(line);
                }

                // After the deferred commands, so a `--cmd` that opens tabs of its own is not
                // interleaved with these, and after Ready so the first listing is loaded.
                foreach (string extra in startPaths)
                {
                    browser.OpenTab(browser.Tabs.Keys.Max() + 1, extra);
                }

                if (startPaths.Count > 0)
                {
                    browser.OpenTab(1);
                }

                // --selectfile names a file to start on. It has to wait until the listing is
                // loaded, which is exactly what Ready means.
                if (options.SelectFile is { } wanted)
                {
                    browser.SelectPath(Path.GetFullPath(wanted));
                }

                pluginHost.NotifyReady(browser);
            };

            int exitCode = browser.Run();

            // The previous-directory bookmark and anything set during the session are written
            // once on the way out, rather than on every navigation.
            if (!options.Clean)
            {
                bookmarks.Save();

                if (settings.SaveTabsOnExit)
                {
                    SavedTabs.Save(paths.Data("tabs"),
                                   [.. browser.Tabs.OrderBy(t => t.Key).Select(t => t.Value.Path)]);
                }
            }

            // --choosedir reports where the user ended up, which is the answer a directory
            // picker exists to give.
            if (options.ChooseDir is { } directoryOutput)
            {
                WriteChoice(directoryOutput, [browser.CurrentTab.Path]);
            }

            return exitCode;
        }
        catch (InvalidOperationException e)
        {
            // Most often: not attached to a terminal, which is worth saying plainly.
            Console.Error.WriteLine($"canger: {e.Message}");
            return 1;
        }
    }

    /// <summary>Writes a chooser's answer, reporting rather than throwing if it cannot.</summary>
    private static void WriteChoice(string path, IReadOnlyList<string> lines)
    {
        try
        {
            File.WriteAllLines(path, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"canger: {path}: {e.Message}");
        }
    }

    /// <summary>The version, taken from the assembly so there is only one place to change it.</summary>
    private static string Version =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Copies the shipped configuration into the user's directory so it can be edited.
    /// </summary>
    /// <remarks>
    /// Existing files are never overwritten. A user running this a second time has almost
    /// certainly edited something since the first, and losing that would be far worse than
    /// having to delete a file by hand to get a fresh copy.
    /// </remarks>
    /// <param name="paths">Where the files go.</param>
    /// <param name="which">Which files: <c>all</c> or one of their names.</param>
    /// <returns>A process exit code.</returns>
    private static int CopyConfiguration(CangerPaths paths, string which)
    {
        string[] names = which.ToLowerInvariant() switch
        {
            "all" => ["cc.conf", "rifle.conf", "commands.cs", "scope.sh"],
            "cc" or "rc" or "cc.conf" => ["cc.conf"],
            "rifle" or "rifle.conf" => ["rifle.conf"],
            "commands" or "commands.cs" => ["commands.cs"],
            "scope" or "scope.sh" => ["scope.sh"],
            _ => [],
        };

        if (names.Length == 0)
        {
            Console.Error.WriteLine(
                $"canger: unknown --copy-config value: {which} " +
                "(expected all, cc, rifle, commands or scope)");

            return 2;
        }

        try
        {
            Directory.CreateDirectory(paths.ConfigDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"canger: {e.Message}");
            return 1;
        }

        int copied = 0;

        foreach (string name in names)
        {
            string source = Path.Join(CangerPaths.InstallDirectory, "config", name);
            string destination = paths.Config(name);

            if (!File.Exists(source))
            {
                Console.Error.WriteLine($"canger: not shipped: {name}");
                continue;
            }

            if (File.Exists(destination))
            {
                Console.Error.WriteLine($"canger: already exists, left alone: {destination}");
                continue;
            }

            try
            {
                File.Copy(source, destination);

                // scope.sh is run as a program, so it has to keep the execute bit.
                if (name.EndsWith(".sh", StringComparison.Ordinal))
                {
                    File.SetUnixFileMode(destination,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                Console.WriteLine($"created {destination}");
                copied++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"canger: {name}: {e.Message}");
            }
        }

        return copied > 0 ? 0 : 1;
    }

    /// <summary>Prints the files carrying a tag.</summary>
    /// <param name="paths">Where the tag file lives.</param>
    /// <param name="tag">The tag, or <c>*</c> for the default one.</param>
    /// <returns>A process exit code.</returns>
    private static int ListTaggedFiles(CangerPaths paths, string tag)
    {
        Tags tags = new(paths.Data("tagged"));
        tags.Reload();

        IReadOnlyList<string> tagged = tag == "*"
            ? tags.Tagged()
            : tags.Tagged([.. tag]);

        foreach (string file in tagged)
        {
            Console.WriteLine(file);
        }

        return 0;
    }

    /// <summary>Builds the preview provider from the configured script and settings.</summary>
    /// <param name="paths">Where the script and the cache live.</param>
    /// <param name="settings">What to preview and how much of it.</param>
    /// <param name="fileSystem">Where files are examined.</param>
    /// <param name="onGenerated">
    /// Called when a preview finishes on its worker, so the browser can draw it. Without it the
    /// provider falls back to generating on the calling thread, which is right for <c>--list</c>
    /// and wrong for the browser.
    /// </param>
    private static ScriptPreviewProvider BuildPreviewProvider(
        CangerPaths paths, CangerSettings settings, IFileSystem fileSystem,
        Action? onGenerated = null)
    {
        string script = settings.PreviewScript ?? FindPreviewScript(paths);
        Directory.CreateDirectory(paths.CacheDirectory);

        ScopeScriptRunner runner = new(script, paths.CacheDirectory)
        {
            ImagesEnabled = settings.PreviewImages,
        };

        return new ScriptPreviewProvider(fileSystem, runner, cache: null, onGenerated)
        {
            PreviewFiles = settings.PreviewFiles,
            PreviewDirectories = settings.PreviewDirectories,
            UseScript = settings.UsePreviewScript,
            MaximumSize = settings.PreviewMaxSize,
        };
    }

    /// <summary>Finds the preview script, preferring the user's copy.</summary>
    private static string FindPreviewScript(CangerPaths paths)
    {
        foreach (string candidate in (string[])
                 [
                     paths.Config("scope.sh"),
                     Path.Join(CangerPaths.SystemConfigDirectory, "scope.sh"),
                     Path.Join(CangerPaths.InstallDirectory, "config", "scope.sh"),
                 ])
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Reads the file-opening rules, preferring the user's copy over the shipped one.
    /// </summary>
    /// <remarks>
    /// Unlike cc.conf, rifle.conf is not additive: a user's copy replaces the shipped rules
    /// entirely, because a rule's meaning depends on which rules precede it.
    /// </remarks>
    private static RifleConfiguration LoadRifleRules(CangerPaths paths, bool clean = false)
    {
        string[] candidates = clean
            ? [Path.Join(CangerPaths.InstallDirectory, "config", "rifle.conf")]
            : [
                  paths.Config("rifle.conf"),
                  Path.Join(CangerPaths.SystemConfigDirectory, "rifle.conf"),
                  Path.Join(CangerPaths.InstallDirectory, "config", "rifle.conf"),
              ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return RifleConfiguration.FromFile(candidate);
            }
        }

        return RifleConfiguration.FromLines([]);
    }

    /// <summary>
    /// Prints what the configuration produced, without starting the interface.
    /// </summary>
    /// <remarks>
    /// This is how to answer "did my cc.conf take effect?" without hunting through a full-screen
    /// interface for the evidence.
    /// </remarks>
    /// <summary>The headline for the binding check.</summary>
    /// <param name="unresolved">How many bound commands the registry could not find.</param>
    /// <param name="hooksPending">Whether any plugin's <c>OnInit</c> is still to run.</param>
    /// <returns>The line to print.</returns>
    /// <remarks>
    /// The distinction is the whole point. A plugin's <c>OnInit</c> runs after this report, so an
    /// alias it adds is absent here and present in a real session — which makes "does not exist"
    /// a false accusation against every such binding, and a check that cries wolf is one people
    /// learn to skip. With no hooks pending the check knows the answer and says so plainly.
    /// </remarks>
    internal static string BindingSummary(int unresolved, bool hooksPending) => unresolved switch
    {
        0 => "browser bindings: every command resolves",
        _ when hooksPending => $"browser bindings: {unresolved} could not be checked here",
        _ => $"browser bindings: {unresolved} name a command that does not exist",
    };

    private static int ReportConfiguration(CangerPaths paths, ConfigurationReader reader,
                                           CangerSettings settings, KeyMaps keyMaps,
                                           CommandRegistry commands, PluginHost plugins)
    {
        Console.WriteLine($"config: {paths.ConfigDirectory}");
        Console.WriteLine($"state:  {paths.DataDirectory}");

        ReportProblems(reader);
        ReportPlugins(plugins);

        Console.WriteLine();
        Console.WriteLine($"key bindings: browser {keyMaps.Browser.Enumerate().Count()}, " +
                          $"console {keyMaps.Console.Enumerate().Count()}, " +
                          $"pager {keyMaps.Pager.Enumerate().Count()}, " +
                          $"taskview {keyMaps.TaskView.Enumerate().Count()}");

        Console.WriteLine($"commands: {commands.Count}");

        ReportUnresolvableBindings(keyMaps, commands, plugins);

        Console.WriteLine();
        Console.WriteLine("effective settings (sample):");
        Console.WriteLine($"  viewmode            {settings.Viewmode}");
        Console.WriteLine($"  column_ratios       {string.Join(",", settings.ColumnRatios)}");
        Console.WriteLine($"  sort                {settings.Sort} (reverse: {settings.SortReverse})");
        Console.WriteLine($"  show_hidden         {settings.ShowHidden}");
        Console.WriteLine($"  hidden_filter       {settings.HiddenFilter}");
        Console.WriteLine($"  scroll_offset       {settings.ScrollOffset}");
        Console.WriteLine($"  colorscheme         {settings.Colorscheme}");
        Console.WriteLine($"  preview_images      {settings.PreviewImages} " +
                          $"({settings.PreviewImagesMethod})");

        return 0;
    }

    /// <summary>
    /// Names every binding whose command does not exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A binding is stored as the text of a command line and only looked up when the key is
    /// pressed, so one naming a command that was never ported sits there silently until someone
    /// presses it and gets nothing. Counting bindings does not catch that; resolving every one of
    /// them does.
    /// </para>
    /// <para>
    /// Only the command name is checked. Whether its arguments make sense is the command's own
    /// business, and a great many take arguments only meaningful at the moment they run.
    /// </para>
    /// </remarks>
    private static void ReportUnresolvableBindings(KeyMaps keyMaps, CommandRegistry commands,
                                                  PluginHost plugins)
    {
        List<(string Keys, string Command)> broken = [];

        // Only the browser context. The console, pager and taskview maps name actions their own
        // widgets handle — `console_close`, `pager_move`, `task_remove` — which never reach the
        // registry at all, so resolving them against it would report every one of them as
        // missing.
        {
            foreach ((IReadOnlyList<int> keys, string line) in keyMaps.Browser.Enumerate())
            {
                // Split on any whitespace, not just a space: a `map` line may separate the
                // command from a trailing comment with tabs, and taking "cmd\t\t#" as the name
                // reported a great many perfectly good bindings as broken.
                string name = line.Split(
                    (char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries) is [string first, ..]
                    ? first
                    : string.Empty;

                if (name.Length == 0)
                {
                    continue;
                }

                bool known;

                try
                {
                    known = commands.Find(name) is not null;
                }
                catch (CommandException)
                {
                    // Ambiguous rather than missing: the name does resolve to something, and the
                    // dispatcher reports the ambiguity itself when the key is pressed.
                    known = true;
                }

                if (!known)
                {
                    broken.Add((string.Concat(keys.Select(KeyCodes.ToDisplayString)), line));
                }
            }
        }

        // A plugin's `OnInit` runs once the interface exists, which is after this report, so any
        // alias it adds there is absent here and present in a real session. Whether an unresolved
        // name is broken is therefore not knowable from here — and saying "does not exist" anyway
        // was a false accusation against every such binding. A check that cries wolf is a check
        // people learn to skip, which is how thirty-six genuinely dead bindings once went unseen
        // behind a different fault in this same report.
        bool hooksPending = plugins.Plugins.Count > 0;

        Console.WriteLine(BindingSummary(broken.Count, hooksPending));

        foreach ((string keys, string line) in broken)
        {
            // The command only, not the trailing comment a `map` line may carry: ranger strips
            // comments only at the start of a line, so plenty of real configurations have one
            // sitting inside the command text.
            Console.WriteLine($"  {keys,-14} {line.Split('#', 2)[0].TrimEnd()}");
        }

        if (broken.Count > 0 && hooksPending)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"  {plugins.Plugins.Count} plugin hook(s) have not run — this report is made "
                + "before the interface exists, so a command or alias a plugin adds in OnInit is "
                + "not visible here. Anything above that a plugin defines works in a real "
                + "session; anything it does not is a dead binding.");
        }
    }

    /// <summary>
    /// Says what each plugin contributed, or why it did not load.
    /// </summary>
    /// <remarks>
    /// A plugin that fails only shows a one-line notification in the running interface, which is
    /// no use for finding a compiler error. This is where the full diagnostics go.
    /// </remarks>
    private static void ReportPlugins(PluginHost plugins)
    {
        if (plugins.Loads.Count == 0)
        {
            return;
        }

        Console.WriteLine();

        foreach (PluginLoad load in plugins.Loads)
        {
            if (load.Succeeded)
            {
                Console.WriteLine($"plugin {load.Name}: {load.Commands} commands, " +
                                  $"{load.Linemodes} linemodes, {load.Plugins} hooks");
            }
            else
            {
                Console.Error.WriteLine($"plugin {load.Name}: {load.Error}");
            }

            foreach (string diagnostic in load.Diagnostics ?? [])
            {
                Console.Error.WriteLine($"  {diagnostic}");
            }
        }
    }

    /// <summary>Prints a listing and exits, without taking over the terminal.</summary>
    private static int ListDirectory(DirectoryCache cache, CangerSettings settings, string path)
    {
        Tab tab = new(cache, path, settings.MaxHistorySize ?? 20);
        DirectoryNode directory = tab.Current;

        directory.ShowHidden = settings.ShowHidden;
        directory.HiddenPattern = settings.HiddenFilter;
        directory.SortOrder = new SortOrder(
            SortOrder.ParseKey(settings.Sort),
            settings.SortReverse,
            settings.SortDirectoriesFirst,
            settings.SortCaseInsensitive,
            settings.SortUnicode);
        directory.Refilter();

        Console.WriteLine($"listing {directory.Path}  " +
                          $"({directory.Count} shown of {directory.AllEntries.Count})");

        foreach (FsNode entry in directory.Entries)
        {
            string kind = entry.IsDirectory ? "dir " : entry.IsSymbolicLink ? "link" : "file";
            string cursor = ReferenceEquals(entry, tab.Selected) ? ">" : " ";
            Console.WriteLine($" {cursor} {kind}  {entry.RelativePath}");
        }

        return 0;
    }

    /// <summary>
    /// Prints configuration problems, separating directives that later phases will implement
    /// from genuine mistakes in the user's configuration.
    /// </summary>
    private static void ReportProblems(ConfigurationReader reader)
    {
        if (reader.Errors.Count == 0)
        {
            return;
        }

        List<ConfigurationError> real = [];
        Dictionary<string, int> pending = new(StringComparer.Ordinal);

        foreach (ConfigurationError error in reader.Errors)
        {
            const string unknown = "unknown directive '";
            if (error.Message.StartsWith(unknown, StringComparison.Ordinal))
            {
                string name = error.Message[unknown.Length..].TrimEnd('\'');
                pending[name] = pending.GetValueOrDefault(name) + 1;
            }
            else
            {
                real.Add(error);
            }
        }

        if (pending.Count > 0)
        {
            IEnumerable<string> summary = pending
                .OrderByDescending(p => p.Value)
                .Select(p => $"{p.Key} ({p.Value})");

            Console.WriteLine($"not yet implemented: {string.Join(", ", summary)}");
        }

        foreach (ConfigurationError error in real)
        {
            Console.Error.WriteLine($"canger: {error}");
        }
    }
}
