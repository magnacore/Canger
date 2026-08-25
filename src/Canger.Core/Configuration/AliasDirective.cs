// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;

namespace Canger.Core.Configuration;

/// <summary>
/// Implements the <c>alias</c> directive, which gives a command line a new name.
/// </summary>
/// <remarks>
/// The shipped configuration leans on this heavily: <c>scout</c> is one general search command,
/// and <c>filter</c>, <c>find</c>, <c>mark</c> and <c>search</c> are all aliases for it with
/// different flags. That is why several keys appear to do quite different things while only one
/// command exists behind them.
/// </remarks>
/// <param name="registry">Where the alias is recorded.</param>
public sealed class AliasDirective(CommandRegistry registry) : IConfigurationDirective
{
    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; } = ["alias"];

    /// <inheritdoc />
    public void Execute(string directive, string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string text = arguments.TrimStart();
        int space = text.IndexOf(' ', StringComparison.Ordinal);

        if (space <= 0)
        {
            throw new CommandException("alias needs a new name and a command line.");
        }

        registry.Alias(text[..space], text[(space + 1)..].Trim());
    }
}
