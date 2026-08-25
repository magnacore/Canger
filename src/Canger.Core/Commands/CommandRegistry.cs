// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;

namespace Canger.Core.Commands;

/// <summary>What is known about a command: how to make one, and how it wants to be treated.</summary>
/// <param name="Name">The name typed at the prompt.</param>
/// <param name="Create">Builds an instance.</param>
/// <param name="AllowAbbreviation">Whether an unambiguous prefix is enough to run it.</param>
/// <param name="ResolveMacros">Whether macros are expanded before it runs.</param>
/// <param name="EscapeMacrosForShell">Whether expanded macros are quoted for the shell.</param>
/// <param name="Summary">A one-line description.</param>
public sealed record CommandDescriptor(
    string Name,
    Func<CangerCommand> Create,
    bool AllowAbbreviation,
    bool ResolveMacros,
    bool EscapeMacrosForShell,
    string Summary);

/// <summary>
/// Every command Canger knows, whether built in, defined in <c>commands.cs</c>, or supplied by a
/// plugin.
/// </summary>
/// <remarks>
/// <para>
/// Names can be abbreviated to any unambiguous prefix, so <c>:quita</c> reaches <c>:quitall</c>.
/// An exact match always wins over a prefix, which is what keeps <c>:q</c> meaning <c>:quit</c>
/// even though several commands begin with those letters.
/// </para>
/// </remarks>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.Ordinal);

    /// <summary>Every registered name, in order.</summary>
    public IReadOnlyList<string> Names => [.. _commands.Keys.Order(StringComparer.Ordinal)];

    /// <summary>How many commands are registered.</summary>
    public int Count => _commands.Count;

    /// <summary>Registers a command type.</summary>
    /// <typeparam name="T">The command.</typeparam>
    public void Register<T>() where T : CangerCommand, new() => Register(typeof(T));

    /// <summary>
    /// Registers a command type, reading its name and behaviour from its attribute.
    /// </summary>
    /// <param name="type">The command type.</param>
    /// <exception cref="ArgumentException">The type is not a usable command.</exception>
    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(CangerCommand).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new ArgumentException(
                $"{type.Name} is not a concrete CangerCommand.", nameof(type));
        }

        if (type.GetCustomAttribute<CommandAttribute>() is not { } attribute)
        {
            throw new ArgumentException(
                $"{type.Name} has no [Command] attribute.", nameof(type));
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new ArgumentException(
                $"{type.Name} needs a parameterless constructor.", nameof(type));
        }

        _commands[attribute.Name] = new CommandDescriptor(
            attribute.Name,
            () => (CangerCommand)Activator.CreateInstance(type)!,
            attribute.AllowAbbreviation,
            attribute.ResolveMacros,
            attribute.EscapeMacrosForShell,
            attribute.Summary);
    }

    /// <summary>
    /// Registers every command in an assembly.
    /// </summary>
    /// <remarks>
    /// This is how a plugin's commands become available: the host loads the assembly and hands it
    /// here, without needing to know what is inside.
    /// </remarks>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>How many commands were registered.</returns>
    public int RegisterAll(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        int count = 0;
        foreach (Type type in assembly.GetTypes())
        {
            if (!type.IsAbstract && typeof(CangerCommand).IsAssignableFrom(type) &&
                type.GetCustomAttribute<CommandAttribute>() is not null &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            {
                Register(type);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Makes one name stand for another command, with arguments already supplied.
    /// </summary>
    /// <remarks>
    /// This is what <c>alias</c> in cc.conf does. <c>alias filter scout -prts</c> means that
    /// typing <c>:filter foo</c> runs <c>:scout -prts foo</c>, which is how the shipped
    /// configuration turns one general command into several specific ones.
    /// </remarks>
    /// <param name="name">The new name.</param>
    /// <param name="target">The command line it stands for.</param>
    /// <exception cref="CommandException">The target command does not exist.</exception>
    public void Alias(string name, string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(target);

        CommandLine targetLine = new(target);
        CommandDescriptor descriptor = Find(targetLine.Name)
            ?? throw new CommandException($"alias failed: no such command: {targetLine.Name}");

        _commands[name] = descriptor with
        {
            Name = name,
            Create = () =>
            {
                CangerCommand command = descriptor.Create();
                return new AliasedCommand(command, target);
            },
            Summary = $"alias for '{target}'",
        };
    }

    /// <summary>
    /// Looks up a command by name, accepting any unambiguous abbreviation.
    /// </summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The command, or <see langword="null"/> when nothing matches.</returns>
    /// <exception cref="CommandException">The name matches several commands.</exception>
    public CommandDescriptor? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // An exact match always wins, so a short command name is never shadowed by a longer one.
        if (_commands.TryGetValue(name, out CommandDescriptor? exact))
        {
            return exact;
        }

        if (name.Length == 0)
        {
            return null;
        }

        List<CommandDescriptor> matches =
        [
            .. _commands.Values.Where(
                c => c.AllowAbbreviation && c.Name.StartsWith(name, StringComparison.Ordinal)),
        ];

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new CommandException(
                $"ambiguous command '{name}': " +
                string.Join(", ", matches.Select(m => m.Name).Order(StringComparer.Ordinal))),
        };
    }

    /// <summary>Names that begin with a prefix, for completing the command itself.</summary>
    /// <param name="prefix">What has been typed.</param>
    /// <returns>The matching names, in order.</returns>
    public IReadOnlyList<string> Matching(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return
        [
            .. _commands.Keys
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Every command, for <c>:help</c> and for listing what is available.</summary>
    /// <returns>The commands, ordered by name.</returns>
    public IReadOnlyList<CommandDescriptor> All() =>
        [.. _commands.Values.OrderBy(c => c.Name, StringComparer.Ordinal)];

    private readonly Dictionary<string, object> _state = new(StringComparer.Ordinal);

    /// <summary>
    /// State that outlives a single invocation of a command.
    /// </summary>
    /// <remarks>
    /// A command is constructed afresh for each invocation, so anything a command must remember
    /// between runs — a prompt chain part-way through, a search still being narrowed — has
    /// nowhere of its own to live. Keeping it here rather than in a static means two file
    /// managers in one process do not share it, which the tests depend on.
    /// </remarks>
    /// <typeparam name="T">What is being remembered.</typeparam>
    /// <param name="key">Names the state; a command's own type name is the usual choice.</param>
    /// <param name="create">Builds it the first time it is asked for.</param>
    /// <returns>The remembered value.</returns>
    public T State<T>(string key, Func<T> create) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(create);

        if (_state.TryGetValue(key, out object? existing) && existing is T typed)
        {
            return typed;
        }

        T made = create();
        _state[key] = made;
        return made;
    }

    /// <summary>
    /// Wraps a command so that it runs with arguments supplied by an alias, followed by whatever
    /// the user typed.
    /// </summary>
    private sealed class AliasedCommand(CangerCommand inner, string target) : CangerCommand
    {
        public override void Execute() => inner.Execute();

        public override IReadOnlyList<string> Complete(int direction) => inner.Complete(direction);

        public override bool Quick() => inner.Quick();

        public override void Cancel() => inner.Cancel();

        /// <summary>
        /// Hands the wrapped command the expanded line rather than what the user typed, so it
        /// sees the alias's arguments as if they had been typed in full.
        /// </summary>
        public override void Initialize(IFileManager fileManager, CommandLine line,
                                        int? quantifier)
        {
            base.Initialize(fileManager, line, quantifier);

            string expanded = line.Count > 1 ? target + " " + line.Rest(1) : target;
            inner.Initialize(fileManager, new CommandLine(expanded), quantifier);
        }
    }
}

/// <summary>Raised when a command cannot be found, built or run.</summary>
public sealed class CommandException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public CommandException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public CommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with no detail. Prefer the descriptive overloads.</summary>
    public CommandException() : base("Command failed.")
    {
    }
}
