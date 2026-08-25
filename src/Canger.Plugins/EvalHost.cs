// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Canger.Core.Commands;

namespace Canger.Plugins;

/// <summary>
/// Compiles and runs the C# snippets an <c>eval</c> line carries.
/// </summary>
/// <remarks>
/// <para>
/// Ranger's <c>eval</c> evaluates Python. The equivalent here is C#, which is the language the
/// rest of the configuration is extended in, so a snippet and a plugin are written the same way.
/// A snippet is a method body with three things in scope:
/// </para>
/// <list type="bullet">
/// <item><description><c>fm</c> — the file manager, or <see langword="null"/> when the snippet
/// runs while the configuration is being read and the interface does not yet exist.</description></item>
/// <item><description><c>cmd</c> — runs a line, which is what a snippet that generates bindings
/// calls.</description></item>
/// <item><description><c>quantifier</c> — the count typed before the key, when the snippet is
/// bound to one.</description></item>
/// </list>
/// <para>
/// Each snippet is its own compilation so that it runs exactly where it appears — the keybinding
/// trie is order-sensitive, and batching them would silently move bindings around. Compiled
/// results are cached on disk by content, so the cost is paid once rather than at every startup.
/// </para>
/// </remarks>
/// <param name="compiler">Compiles the snippets.</param>
public sealed partial class EvalHost(ScriptCompiler compiler)
{
    private readonly Dictionary<string, MethodInfo?> _compiled = new(StringComparer.Ordinal);

    /// <summary>What went wrong, in the order it went wrong.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>
    /// Runs a snippet.
    /// </summary>
    /// <param name="code">The C# to run, as a method body.</param>
    /// <param name="fileManager">
    /// What <c>fm</c> refers to, or <see langword="null"/> during configuration reading.
    /// </param>
    /// <param name="run">What <c>cmd</c> calls.</param>
    /// <param name="quantifier">What <c>quantifier</c> refers to.</param>
    /// <returns><see langword="true"/> when the snippet compiled and ran.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "The snippet is user-written code. Whatever it throws is "
                                   + "reported against that line rather than allowed to stop the "
                                   + "configuration being read.")]
    public bool Run(string code, IFileManager? fileManager, Action<string> run,
                    int? quantifier = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(run);

        if (Resolve(code) is not { } method)
        {
            return false;
        }

        try
        {
            method.Invoke(null, [fileManager, run, quantifier]);
            return true;
        }
        catch (TargetInvocationException e)
        {
            // The wrapper says only "an exception was thrown"; what the snippet actually threw
            // is the part worth reporting.
            Errors.Add((e.InnerException ?? e).Message);
            return false;
        }
        catch (Exception e)
        {
            Errors.Add(e.Message);
            return false;
        }
    }

    /// <summary>Compiles a snippet, or returns the method compiled for it earlier.</summary>
    private MethodInfo? Resolve(string code)
    {
        if (_compiled.TryGetValue(code, out MethodInfo? cached))
        {
            return cached;
        }

        MethodInfo? method = Compile(code);
        _compiled[code] = method;
        return method;
    }

    private MethodInfo? Compile(string code)
    {
        // Checked before compiling, not after it fails. A configuration carried over from ranger
        // has ten of these, and handing each to Roslyn to watch it fail cost the best part of a
        // second on every single launch.
        if (LooksLikePython(code))
        {
            Errors.Add(Explain(code, []));
            return null;
        }

        // The class is named after the snippet's content so two snippets never collide, and so a
        // cached build on disk belongs unambiguously to one snippet.
        string name = "Eval_" + ShortHash(code);
        string source =
            $$"""
              public static class {{name}}
              {
                  public static void Run(Canger.Core.Commands.IFileManager fm,
                                         System.Action<string> cmd,
                                         int? quantifier)
                  {
                      {{code}}
                  }
              }
              """;

        // A random component, so the name cannot be pre-created as a link to something else by
        // anyone sharing /tmp. `BulkRenameCommand` already does this; this one hashed the snippet,
        // which is a pure function of text that often comes from a shipped configuration.
        string file = Path.Join(Path.GetTempPath(),
                                name + "-" + Path.GetRandomFileName() + ".cs");

        try
        {
            File.WriteAllText(file, source);
            CompilationResult result = compiler.Compile(name, [file]);

            if (!result.Succeeded)
            {
                Errors.Add(Explain(code, result.Diagnostics));
                return null;
            }

            return result.Assembly!.GetType(name)?.GetMethod("Run");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Errors.Add(e.Message);
            return null;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary file is not worth reporting.
            }
        }
    }

    /// <summary>
    /// Turns a wall of compiler output into one line a person can act on.
    /// </summary>
    /// <remarks>
    /// A snippet is one expression, so the first error is the one that matters and the dozen
    /// after it are its consequences. Python gets a message of its own, because a configuration
    /// carried over from ranger will contain Python <c>eval</c> lines and thirteen C# syntax
    /// errors is a hostile way to explain that the language changed.
    /// </remarks>
    private static string Explain(string code, IReadOnlyList<string> diagnostics)
    {
        if (LooksLikePython(code))
        {
            return "eval evaluates C# in Canger, not Python. Rewrite it, or use the named "
                 + $"commands the shipped cc.conf uses: {Summarise(code)}";
        }

        // Only the first diagnostic, and only its message — the file and position refer to a
        // temporary file the user has never seen.
        string first = diagnostics.Count > 0 ? diagnostics[0] : "did not compile";
        int colon = first.IndexOf("error ", StringComparison.Ordinal);

        return colon >= 0 ? first[colon..] : first;
    }

    /// <summary>Whether a snippet is Python rather than C#.</summary>
    /// <remarks>
    /// Looks for a handful of things that cannot be C# rather than trying to parse: the only
    /// decision it drives is which message to show, so a wrong guess costs nothing but
    /// wording. These four cover every <c>eval</c> in ranger's own shipped configuration.
    /// </remarks>
    private static bool LooksLikePython(string code) =>
        code.Contains(".format(", StringComparison.Ordinal)
        || code.Contains("dict(", StringComparison.Ordinal)
        || code.Contains("os.getenv", StringComparison.Ordinal)
        || PythonForLoop().IsMatch(code);

    [GeneratedRegex(@"\bfor\s+\w+\s+in\s")]
    private static partial Regex PythonForLoop();

    /// <summary>The snippet, shortened enough to sit on a status line.</summary>
    private static string Summarise(string code)
    {
        string collapsed = code.ReplaceLineEndings(" ").Trim();

        return collapsed.Length <= 60 ? collapsed : collapsed[..57] + "...";
    }

    private static string ShortHash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    /// <summary>Formats a value the way a snippet's own output would be shown.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A readable rendering.</returns>
    internal static string Describe(object? value) => value switch
    {
        null => "null",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
