// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Canger.Plugins;

/// <summary>
/// What came of compiling a set of source files.
/// </summary>
/// <param name="Assembly">The compiled assembly, or <see langword="null"/> if it failed.</param>
/// <param name="Diagnostics">Errors and warnings, in the compiler's own words.</param>
/// <param name="FromCache">Whether the result was reused rather than compiled afresh.</param>
public sealed record CompilationResult(
    Assembly? Assembly,
    IReadOnlyList<string> Diagnostics,
    bool FromCache)
{
    /// <summary>Whether there is an assembly to load.</summary>
    public bool Succeeded => Assembly is not null;
}

/// <summary>
/// Compiles C# source files into an assembly Canger can load.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes <c>commands.cs</c> and <c>plugins/*.cs</c> work the way ranger's
/// <c>commands.py</c> does — edit a file, restart, and the new command is there. The cost of
/// compiling is paid once and cached by content hash, so an unchanged configuration adds nothing
/// to startup after the first run.
/// </para>
/// <para>
/// Requiring a JIT is why Canger ships framework-dependent rather than ahead-of-time compiled:
/// there is no way to have both Roslyn and NativeAOT.
/// </para>
/// </remarks>
/// <param name="cacheDirectory">
/// Where compiled assemblies are kept between runs, or <see langword="null"/> to compile every
/// time.
/// </param>
public sealed class ScriptCompiler(string? cacheDirectory = null)
{
    /// <summary>
    /// Namespaces every script gets without asking, so a short plugin needs no preamble.
    /// </summary>
    private static readonly string[] ImplicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "Canger.Core.Commands",
        "Canger.Core.Model",
        "Canger.Core.Processes",
        "Canger.Plugins",
    ];

    /// <summary>Extra assemblies a script may reference, beyond what Canger already loads.</summary>
    public List<Assembly> AdditionalReferences { get; } = [];

    /// <summary>
    /// The reference list, built once.
    /// </summary>
    /// <remarks>
    /// Reading metadata from every loaded assembly is the slow part of a small compilation, and
    /// a configuration full of <c>eval</c> lines compiles many small things. The set does not
    /// change between them, so it is built once and reused.
    /// </remarks>
    private List<MetadataReference>? _references;

    /// <summary>
    /// Compiles source files into one assembly.
    /// </summary>
    /// <param name="name">A name for the assembly, used in diagnostics and cache filenames.</param>
    /// <param name="files">The files to compile.</param>
    /// <returns>What came of it.</returns>
    /// <remarks>
    /// All the files go into one compilation so they can refer to each other, which is what lets
    /// a plugin split itself across files without ceremony.
    /// </remarks>
    public CompilationResult Compile(string name, IReadOnlyList<string> files)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(files);

        List<(string Path, string Text)> sources = [];

        foreach (string file in files)
        {
            try
            {
                sources.Add((file, File.ReadAllText(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new CompilationResult(null, [$"{file}: {e.Message}"], FromCache: false);
            }
        }

        if (sources.Count == 0)
        {
            return new CompilationResult(null, [], FromCache: false);
        }

        string hash = Hash(sources);

        if (LoadFromCache(name, hash) is { } cached)
        {
            return new CompilationResult(cached, [], FromCache: true);
        }

        return CompileFresh(name, hash, sources);
    }

    /// <summary>Compiles and, if it worked, caches and loads the result.</summary>
    private CompilationResult CompileFresh(string name, string hash,
                                           List<(string Path, string Text)> sources)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        // The implicit namespaces go in as a synthetic file of global usings. The compilation
        // options have a `usings` field, but it applies only to script compilations, so a
        // regular one silently ignores it — which reads as "your plugin does not compile".
        string preamble = string.Concat(ImplicitUsings.Select(u => $"global using {u};\n"));

        SyntaxTree[] trees =
        [
            CSharpSyntaxTree.ParseText(preamble, parseOptions, path: "<implicit usings>",
                                       encoding: Encoding.UTF8),
            .. sources.Select(s => CSharpSyntaxTree.ParseText(
                s.Text, parseOptions, path: s.Path, encoding: Encoding.UTF8))
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            trees,
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                // A plugin author is not writing a library; nullable warnings on their first
                // attempt would be noise rather than help.
                nullableContextOptions: NullableContextOptions.Annotations));

        using MemoryStream stream = new();
        EmitResult emitted = compilation.Emit(stream);

        string[] diagnostics =
        [
            .. emitted.Diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .Select(d => d.ToString())
        ];

        if (!emitted.Success)
        {
            return new CompilationResult(null, diagnostics, FromCache: false);
        }

        byte[] bytes = stream.ToArray();
        Cache(name, hash, bytes);

        return new CompilationResult(Load(name, bytes), diagnostics, FromCache: false);
    }

    /// <summary>
    /// What a script may reference: everything Canger itself has loaded.
    /// </summary>
    /// <remarks>
    /// Handing over the whole loaded set rather than a curated list means a plugin can use
    /// anything Canger can, which is the point of writing plugins in the same language.
    /// </remarks>
    private List<MetadataReference> References()
    {
        if (_references is not null)
        {
            return _references;
        }

        List<MetadataReference> references = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        IEnumerable<Assembly> candidates = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(AdditionalReferences);

        foreach (Assembly assembly in candidates)
        {
            // Dynamic assemblies have no file to read metadata from, which includes anything
            // this compiler itself produced.
            if (assembly.IsDynamic || assembly.Location.Length == 0 || !seen.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        _references = references;
        return references;
    }

    /// <summary>Loads compiled bytes into a context of their own.</summary>
    private static Assembly Load(string name, byte[] bytes)
    {
        PluginLoadContext context = new(name);
        using MemoryStream stream = new(bytes);
        return context.LoadFromStream(stream);
    }

    /// <summary>The file a compilation of this content would be cached in.</summary>
    private string? CachePath(string name, string hash) =>
        cacheDirectory is null ? null : Path.Join(cacheDirectory, $"{name}.{hash}.dll");

    private Assembly? LoadFromCache(string name, string hash)
    {
        if (CachePath(name, hash) is not { } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return Load(name, File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or BadImageFormatException)
        {
            // A truncated or corrupt cache entry should cost a recompile, not the session.
            return null;
        }
    }

    private void Cache(string name, string hash, byte[] bytes)
    {
        if (CachePath(name, hash) is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(cacheDirectory!);

            // Written aside and moved into place, so a cache entry is never half a file — two
            // Cangers starting at once would otherwise be able to read one.
            string temporary = path + ".tmp" + Environment.ProcessId.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);

            RemoveStaleEntries(name, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Caching is an optimisation; failing to cache is not worth reporting.
        }
    }

    /// <summary>Deletes cached builds of earlier versions of the same source.</summary>
    private void RemoveStaleEntries(string name, string keep)
    {
        foreach (string file in Directory.EnumerateFiles(cacheDirectory!, $"{name}.*.dll"))
        {
            if (string.Equals(file, keep, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Another Canger may have it open; it will be cleaned up next time.
            }
        }
    }

    /// <summary>
    /// Hashes the source, so a cached build is only reused for exactly the same input.
    /// </summary>
    /// <remarks>
    /// Paths are hashed alongside the text because the same content in a different file is a
    /// different compilation as far as diagnostics and debugging are concerned.
    /// </remarks>
    private static string Hash(List<(string Path, string Text)> sources)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach ((string path, string text) in sources.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData(Encoding.UTF8.GetBytes(text));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }
}
