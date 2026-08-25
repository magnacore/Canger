// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;

namespace Canger.Core.Commands;

/// <summary>
/// Turns a typed or bound command line into a command that runs.
/// </summary>
/// <remarks>
/// The order of the steps matters. The command is resolved first, because whether its macros are
/// expanded, and whether they are quoted for the shell, are properties of the command rather than
/// of the line. Expanding before resolving would apply the wrong rules — and would expand
/// <c>:map</c>'s argument, which must stay untouched until the key it binds is actually pressed.
/// </remarks>
public sealed class CommandDispatcher(IFileManager fileManager, CommandRegistry registry)
{
    private readonly IFileManager _fileManager =
        fileManager ?? throw new ArgumentNullException(nameof(fileManager));

    private readonly CommandRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

    private readonly MacroExpander _macros = new(fileManager);

    /// <summary>Registers every command built into Canger.</summary>
    /// <param name="registry">Where to register them.</param>
    /// <returns>How many were registered.</returns>
    public static int RegisterBuiltins(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.RegisterAll(Assembly.GetExecutingAssembly());
    }

    /// <summary>
    /// Builds a command from a line, without running it.
    /// </summary>
    /// <remarks>
    /// The console needs this so it can ask the command for completions and let it act as the
    /// user types, both of which happen before there is any question of running it.
    /// </remarks>
    /// <param name="line">The line as typed.</param>
    /// <param name="quantifier">A numeric prefix, when one was typed.</param>
    /// <param name="wildcards">Keys captured by <c>&lt;any&gt;</c> in the binding that fired.</param>
    /// <returns>The command, or <see langword="null"/> when the name is not known.</returns>
    /// <exception cref="CommandException">The name is ambiguous.</exception>
    /// <exception cref="MacroException">A macro had no value.</exception>
    public CangerCommand? Build(string line, int? quantifier = null,
                                IReadOnlyList<int>? wildcards = null)
    {
        ArgumentNullException.ThrowIfNull(line);

        CommandLine parsed = new(line.TrimStart());
        if (parsed.Name.Length == 0)
        {
            return null;
        }

        if (_registry.Find(parsed.Name) is not { } descriptor)
        {
            return null;
        }

        // Macro rules belong to the command, so they can only be applied once it is known.
        CommandLine effective = descriptor.ResolveMacros
            ? new CommandLine(_macros.Expand(parsed.Line, wildcards,
                                             descriptor.EscapeMacrosForShell))
            : parsed;

        CangerCommand command = descriptor.Create();
        command.Initialize(_fileManager, effective, quantifier);
        return command;
    }

    /// <summary>
    /// Runs a command line, reporting anything that goes wrong in the status bar.
    /// </summary>
    /// <remarks>
    /// A failing command must never take the session down with it: a mistyped name, an ambiguous
    /// abbreviation, an unresolvable macro or a bug in a user-written command all become a
    /// message rather than a crash.
    /// </remarks>
    /// <param name="line">The line as typed.</param>
    /// <param name="quantifier">A numeric prefix, when one was typed.</param>
    /// <param name="wildcards">Keys captured by <c>&lt;any&gt;</c> in the binding that fired.</param>
    /// <returns><see langword="true"/> when a command ran.</returns>
    public bool Execute(string line, int? quantifier = null, IReadOnlyList<int>? wildcards = null)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            CangerCommand? command = Build(line, quantifier, wildcards);

            if (command is null)
            {
                string name = new CommandLine(line.TrimStart()).Name;
                if (name.Length > 0)
                {
                    _fileManager.Notify($"unknown command: {name}", isError: true);
                }

                return false;
            }

            command.Execute();
            return true;
        }
        catch (Exception e) when (e is CommandException or MacroException)
        {
            _fileManager.Notify(e.Message, isError: true);
            return false;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            _fileManager.Notify($"{new CommandLine(line).Name}: {e.Message}", isError: true);
            return false;
        }
    }
}
