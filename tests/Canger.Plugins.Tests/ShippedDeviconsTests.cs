// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Core.State;
using Canger.Plugins;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// The <c>devicons</c> linemode Canger ships as a plugin.
/// </summary>
/// <remarks>
/// It used to be built in, which meant two copies of the same four hundred glyphs generated from
/// one source by two scripts. It is a plugin in ranger and it is a plugin here now, which makes
/// these tests the only thing standing between a broken generator and a linemode that silently is
/// not there — the same gap <see cref="ShippedCommandsTests"/> exists to cover.
/// </remarks>
public sealed class ShippedDeviconsTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-devicons-" + Path.GetRandomFileName());

    public ShippedDeviconsTests() => Directory.CreateDirectory(Path.Join(_root, "plugins"));

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

    /// <summary>Loads the shipped plugin and returns the linemode it registered.</summary>
    private ILinemode Load()
    {
        string config = ConfigDirectory();
        Assert.SkipWhen(config.Length == 0, "the repository layout was not found");

        string source = Path.Join(config, "plugins", "devicons.cs");
        Assert.True(File.Exists(source), $"{source} is missing");

        File.Copy(source, Path.Join(_root, "plugins", "devicons.cs"));

        LinemodeRegistry linemodes = new();
        PluginHost host = new(new CommandRegistry(), linemodes, new ScriptCompiler());
        host.LoadFrom(_root);

        PluginLoad load = Assert.Single(host.Loads);
        Assert.True(load.Succeeded,
                    "the shipped devicons plugin did not compile: " +
                    string.Join("; ", load.Diagnostics ?? []));

        ILinemode? mode = linemodes.Find("devicons");
        Assert.NotNull(mode);
        return mode;
    }

    /// <summary>A node for a name, so the linemode has something to answer about.</summary>
    private static FsNode Entry(InMemoryFileSystem fs, string name, bool directory = false)
    {
        string path = "/home/" + name;

        if (directory)
        {
            fs.AddDirectory(path);
        }
        else
        {
            fs.AddFile(path);
        }

        DirectoryNode home = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true));
        home.Load(TestContext.Current.CancellationToken);

        return home.Entries.First(e => e.Basename == name);
    }

    private static string Glyph(ILinemode mode, InMemoryFileSystem fs, string name,
                               bool directory = false)
    {
        FsNode node = Entry(fs, name, directory);
        string title = mode.Title(node, FileMetadata.Empty, default);

        // The title is "<glyph> <name>", so what precedes the name is the glyph.
        return title[..title.IndexOf(node.RelativePath, StringComparison.Ordinal)].TrimEnd();
    }

    [Fact]
    public void TheShippedPluginCompilesAndRegistersTheLinemode()
    {
        // Without this nothing catches a generator that emits code which does not build: the
        // linemode simply would not exist, and `default_linemode devicons` would fall back with
        // no complaint anyone would notice.
        Assert.Equal("devicons", Load().Name);
    }

    [Fact]
    public void ItGivesDifferentKindsOfFileDifferentGlyphs()
    {
        ILinemode mode = Load();
        InMemoryFileSystem fs = new();

        string audio = Glyph(mode, fs, "song.mp3");
        string code = Glyph(mode, fs, "code.cs");
        string folder = Glyph(mode, fs, "folder", directory: true);

        Assert.NotEqual(audio, code);
        Assert.NotEqual(audio, folder);
        Assert.NotEqual(code, folder);
    }

    [Fact]
    public void AWholeNameBeatsAnExtension()
    {
        // Makefile and .gitignore have no useful extension, and Dockerfile would otherwise look
        // like any other extensionless file.
        ILinemode mode = Load();
        InMemoryFileSystem fs = new();

        Assert.NotEqual(Glyph(mode, fs, "plain"), Glyph(mode, fs, "Dockerfile"));
    }

    [Fact]
    public void AnUnknownKindStillGetsSomething()
    {
        // A blank where a glyph should be would misalign the name against every other row.
        Assert.NotEmpty(Glyph(Load(), new InMemoryFileSystem(), "thing.qqzz"));
    }

    [Fact]
    public void ItLeavesTheRightHandSideToTheListing()
    {
        // Detail returns null, which the column reads as "show the size". A mode that answered
        // here would take the size column away from every row.
        ILinemode mode = Load();
        InMemoryFileSystem fs = new();

        Assert.Null(mode.Detail(Entry(fs, "song.mp3"), FileMetadata.Empty, default));
    }
}
