// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;

namespace Canger.Plugins;

/// <summary>
/// Runs a C# snippet at the prompt, or from a key binding.
/// </summary>
/// <remarks>
/// <para>
/// Where the <c>eval</c> configuration directive runs while the configuration is being read, this
/// runs whenever the key is pressed, so <c>fm</c> is a live file manager and <c>quantifier</c> is
/// whatever count was typed. That is what lets a binding do something no built-in command covers
/// without writing a plugin for it.
/// </para>
/// <para>
/// Registered only when a plugin host exists, since it needs a compiler. A configuration that
/// binds <c>eval</c> without one gets the usual "unknown command", which is accurate.
/// </para>
/// </remarks>
[Command("eval", Summary = "Run a C# snippet: eval [-q] <code>")]
public sealed class EvalCommand : CangerCommand
{
    /// <summary>
    /// Compiles and runs the snippets. Set by the composition root when a host is available.
    /// </summary>
    /// <remarks>
    /// A static because a command is constructed afresh per invocation by a registry that knows
    /// nothing about plugins, and the alternative — threading a compiler through the command
    /// registry — would put a plugin concern into the core for the sake of one command.
    /// </remarks>
    public static EvalHost? Host { get; set; }

    /// <inheritdoc />
    public override void Execute()
    {
        if (Host is not { } host)
        {
            FileManager.Notify("eval: no plugin host available", isError: true);
            return;
        }

        string code = Rest(1).Trim();

        // -q suppresses the report, matching ranger's own flag: a binding that does its own
        // notifying does not want a second message after it.
        bool quiet = code.StartsWith("-q", StringComparison.Ordinal);

        if (quiet)
        {
            code = code[2..].Trim();
        }

        if (code.Length == 0)
        {
            FileManager.Notify("eval: nothing to evaluate", isError: true);
            return;
        }

        int before = host.Errors.Count;

        // A snippet's `cmd` runs a command line, which is the same thing the prompt does.
        bool ran = host.Run(EvalDirective.Statement(code), FileManager,
                            line => FileManager.Execute(line), Quantifier);

        if (!ran && host.Errors.Count > before)
        {
            FileManager.Notify($"eval: {string.Join("; ", host.Errors.Skip(before))}",
                               isError: true);
        }
        else if (ran && !quiet)
        {
            FileManager.Notify("eval: done");
        }
    }
}
