// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Reflection;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// The archives plugin's choice of what to measure unpacking against.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a control found nothing pinning it. Extraction passed the archive's size on
/// disk while <c>tar</c> counts the uncompressed stream, the bar sat at 100% for the last seconds
/// of every extraction, and reverting the fix broke none of 1 982 tests — the format knowledge
/// lives in the plugin, and nothing compiled the plugin and asked it anything.
/// </para>
/// <para>
/// So the plugin is compiled here the way Canger compiles it, and its answer is checked against a
/// real archive whose two sizes differ by three orders of magnitude. Reflection is the price of
/// reaching into a plugin, and it is worth paying for the one decision that was wrong.
/// </para>
/// </remarks>
public sealed class ShippedArchivesTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-archives-" + Path.GetRandomFileName());

    public ShippedArchivesTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Finds the shipped plugins beside the repository root.</summary>
    private static string PluginDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Join(directory.FullName, "artifacts", "plugins");

            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")) &&
                Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    /// <summary>Asks the compiled plugin what total it would measure an extraction against.</summary>
    private static long? UncompressedSize(Assembly plugin, string archive)
    {
        Type decompression = plugin.GetType("Decompression") ??
                             throw new InvalidOperationException("the plugin defines no Decompression");

        MethodInfo method =
            decompression.GetMethod("UncompressedSize",
                                    BindingFlags.Static | BindingFlags.NonPublic |
                                    BindingFlags.Public) ??
            throw new InvalidOperationException("Decompression has no UncompressedSize");

        LocalFileSystem fileSystem = new();
        FileNode node = new(fileSystem, archive,
                            fileSystem.GetStatus(archive, followSymbolicLinks: true));

        return (long?)method.Invoke(null, [node]);
    }

    [Fact]
    public void MeasuresExtractionAgainstWhatComesOutNotTheFileOnDisk()
    {
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        // A tree that compresses hard, so the two candidate totals cannot be confused.
        string source = Path.Join(_root, "src");
        Directory.CreateDirectory(source);

        for (int file = 0; file < 4; file++)
        {
            File.WriteAllText(Path.Join(source, $"f{file}.txt"),
                              string.Concat(Enumerable.Repeat("compressible text\n", 40_000)));
        }

        string archive = Path.Join(_root, "test.tar.lz");
        Assert.SkipUnless(Run("tar", $"-cf {archive} --use-compress-program lzip -C {_root} src"),
                          "tar or lzip is not installed");

        long onDisk = new FileInfo(archive).Length;
        long stream = CompressedStreamSize.Of(archive) ?? 0;

        // The premise of the test: if these were close, it could not tell the two apart.
        Assert.True(stream > onDisk * 100,
                    $"the fixture is not compressible enough: {onDisk} on disk, {stream} inside");

        CompilationResult compiled =
            new ScriptCompiler().Compile("archives", [Path.Join(plugins, "archives.cs")]);

        Assert.True(compiled.Succeeded,
                    "the shipped archives.cs did not compile: " +
                    string.Join("; ", compiled.Diagnostics));

        Assert.Equal(stream, UncompressedSize(compiled.Assembly!, archive));
    }

    [Fact]
    public void MeasuresAPlainTarAgainstItsOwnSize()
    {
        // Nothing wraps a plain tar, so the file on disk *is* the stream tar reads, and reading
        // its trailer would find no compression header at all.
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        string source = Path.Join(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Join(source, "f.txt"), new string('x', 100_000));

        string archive = Path.Join(_root, "test.tar");
        Assert.SkipUnless(Run("tar", $"-cf {archive} -C {_root} src"), "tar is not installed");

        CompilationResult compiled =
            new ScriptCompiler().Compile("archives", [Path.Join(plugins, "archives.cs")]);

        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
        Assert.Equal(new FileInfo(archive).Length, UncompressedSize(compiled.Assembly!, archive));
    }

    [Fact]
    public void ClaimsNoTotalForAnArchiveThatRecordsNoSize()
    {
        // bzip2 records nothing, and a guess would be a bar that lies. Unpacking shows bytes.
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        string source = Path.Join(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Join(source, "f.txt"), new string('x', 100_000));

        string archive = Path.Join(_root, "test.tar.bz2");
        Assert.SkipUnless(Run("tar", $"-cjf {archive} -C {_root} src"),
                          "tar or bzip2 is not installed");

        CompilationResult compiled =
            new ScriptCompiler().Compile("archives", [Path.Join(plugins, "archives.cs")]);

        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
        Assert.Null(UncompressedSize(compiled.Assembly!, archive));
    }

    [Fact]
    public void WatchesAZipBeingBuiltByTheEntriesZipNames()
    {
        // The storing half of the same report. `zip` writes to a temporary file and renames it at
        // the end, so watching the archive grow saw nothing at all until the job was over — the
        // count appeared only on the line that said it had finished. What zip does say, all the
        // way through, is which file it is adding.
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        InMemoryFileSystem files = new();
        files.AddFileOfSize("/w/data/f0.bin", 1000);
        files.AddFileOfSize("/w/data/f1.bin", 2000);

        FakeFileManager manager = new(files, "/w");
        PluginHost host = new(manager.Commands, null, new ScriptCompiler());

        // Loaded the way Canger loads it: `plugins/` beneath the configuration directory, which
        // is where LoadFrom looks for anything that is not commands.cs.
        string configured = Path.Join(_root, "config");
        Directory.CreateDirectory(Path.Join(configured, "plugins"));
        File.Copy(Path.Join(plugins, "archives.cs"),
                  Path.Join(configured, "plugins", "archives.cs"));
        host.LoadFrom(configured);

        Assert.True(Assert.Single(host.Loads).Succeeded,
                    string.Join("; ", Assert.Single(host.Loads).Diagnostics ?? []));

        bool ran = manager.Execute("compress out.zip");

        Assert.True(ran && manager.BackgroundProgress.Count == 1,
                    $"ran={ran}; queued={manager.BackgroundProgress.Count}; " +
                    $"selection={((Canger.Core.Commands.IFileManager)manager).Selection.Count}; " +
                    $"said={string.Join(" | ", manager.Messages.Select(m => m.Message))}");

        ICommandProgress? progress = Assert.Single(manager.BackgroundProgress);
        ManifestProgress manifest = Assert.IsType<ManifestProgress>(progress);

        // The manifest is walked by the queue in slices, as a copy's measuring is.
        IEnumerator<Unit> walk = manifest.Measure();
        while (walk.MoveNext())
        {
        }

        Assert.Equal(3000, manifest.Total);
    }

    /// <summary>Asks the compiled plugin what it would watch an unpacking with.</summary>
    private static object? ProgressFor(Assembly plugin, string archive)
    {
        Type decompression = plugin.GetType("Decompression") ??
                             throw new InvalidOperationException("the plugin defines no Decompression");

        MethodInfo method =
            decompression.GetMethod("ProgressFor",
                                    BindingFlags.Static | BindingFlags.NonPublic |
                                    BindingFlags.Public) ??
            throw new InvalidOperationException("Decompression has no ProgressFor");

        LocalFileSystem fileSystem = new();
        FileNode node = new(fileSystem, archive,
                            fileSystem.GetStatus(archive, followSymbolicLinks: true));

        return method.Invoke(null, [node]);
    }

    [Fact]
    public void WatchesAZipByTheEntriesUnzipNames()
    {
        // Reported: "when I use the .zip format, I don't see any progress". unzip reports no
        // bytes, but names each entry as it writes it, and the archive's own index says what
        // those names weigh — so the total is known before a byte is written.
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        string source = Path.Join(_root, "src");
        Directory.CreateDirectory(source);

        for (int file = 0; file < 3; file++)
        {
            File.WriteAllText(Path.Join(source, $"f{file}.txt"), new string('x', 1000));
        }

        // Absolute, because Run does not set a working directory — a relative name here made
        // the tool fail and the test skip as though zip were missing.
        Assert.SkipUnless(Run("zip", $"-q -r -j {Path.Join(_root, "out.zip")} {source}"),
                          "zip is not installed");

        CompilationResult compiled =
            new ScriptCompiler().Compile("archives", [Path.Join(plugins, "archives.cs")]);

        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));

        object? progress = ProgressFor(compiled.Assembly!, Path.Join(_root, "out.zip"));

        Assert.NotNull(progress);
        Assert.Equal("ManifestProgress", progress.GetType().Name);
        Assert.Equal(3000L, ((ICommandProgress)progress).Total);
    }

    [Fact]
    public void ClaimsNothingForAnArchiveNoToolWillReportOn()
    {
        // A 7z archive: neither a tar to be asked for checkpoints nor an Info-ZIP that names what
        // it writes. The spinner it always had is the honest answer.
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        string source = Path.Join(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Join(source, "f.txt"), new string('x', 1000));

        Assert.SkipUnless(Run("7z", $"a -bso0 -bsp0 {Path.Join(_root, "out.7z")} {source}"),
                          "7z is not installed");

        CompilationResult compiled =
            new ScriptCompiler().Compile("archives", [Path.Join(plugins, "archives.cs")]);

        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
        Assert.Null(ProgressFor(compiled.Assembly!, Path.Join(_root, "out.7z")));
    }

    /// <summary>Runs a program, and says whether it was there to run and succeeded.</summary>
    private static bool Run(string program, string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return false;
            }

            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}
