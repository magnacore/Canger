// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.Configuration;

namespace Canger.Plugins;

/// <summary>
/// The <c>eval</c> line in a configuration file.
/// </summary>
/// <remarks>
/// <para>
/// This exists for what a declarative line cannot say: generating a family of bindings from a
/// list, or choosing what to configure based on the machine. Canger's own configuration uses it
/// nowhere — everything ranger expressed as <c>eval</c> is a named command here — so a user
/// reaching for it is doing something genuinely bespoke.
/// </para>
/// <para>
/// The snippet runs where it appears, not at the end, because the keybinding trie is
/// order-sensitive.
/// </para>
/// </remarks>
/// <param name="host">Compiles and runs the snippets.</param>
/// <param name="run">What a snippet's <c>cmd</c> calls: usually another configuration line.</param>
public sealed class EvalDirective(EvalHost host, Action<string> run) : IConfigurationDirective
{
    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; } = ["eval"];

    /// <inheritdoc />
    public void Execute(string directive, string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string code = arguments.Trim();

        if (code.Length == 0)
        {
            throw new CommandException("eval needs something to evaluate.");
        }

        // Ranger's own rc.conf writes its sixty chmod bindings with a `for` loop over a string
        // of characters. That is not C#, and telling the user to rewrite what ranger itself
        // ships would be a poor answer, so it is expanded here into the lines it stands for.
        if (RangerEvalTranslator.ExpandLoop(code) is { } expanded)
        {
            foreach (string line in expanded)
            {
                run(line);
            }

            return;
        }

        int before = host.Errors.Count;

        if (!host.Run(Statement(code), fileManager: null, run) && host.Errors.Count > before)
        {
            // Reported as a configuration error so it appears beside the offending line rather
            // than in a log the user will not read.
            throw new CommandException(string.Join("; ", host.Errors.Skip(before)));
        }
    }

    /// <summary>
    /// Makes a snippet a statement, so a bare expression works as well as a block.
    /// </summary>
    /// <param name="code">The snippet as written.</param>
    /// <returns>Something that will parse as a method body.</returns>
    /// <remarks>
    /// <c>eval cmd("map x quit")</c> should work without a semicolon, the way ranger's does,
    /// while a snippet that already ends in <c>;</c> or <c>}</c> is left alone.
    /// </remarks>
    internal static string Statement(string code) =>
        code.EndsWith(';') || code.EndsWith('}') ? code : code + ";";
}
