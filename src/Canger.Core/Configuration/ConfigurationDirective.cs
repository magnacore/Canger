// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Configuration;

/// <summary>
/// Handles one kind of cc.conf line, such as <c>set</c> or <c>map</c>.
/// </summary>
/// <remarks>
/// cc.conf is not a distinct file format. Every non-comment line is a command, exactly the ones
/// you can type at the <c>:</c> prompt, which is why <c>ranger/core/actions.py:369-389</c> simply
/// feeds each line to the console dispatcher. Canger keeps that property but routes lines through
/// a registry of directives so that subsystems can contribute their own — the keybinding layer
/// registers <c>map</c> and friends without the reader knowing anything about keys.
/// </remarks>
public interface IConfigurationDirective
{
    /// <summary>
    /// The directive names this handles, for example <c>map</c>, <c>cmap</c>, <c>pmap</c>.
    /// </summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Executes one line.
    /// </summary>
    /// <param name="directive">The first word of the line, identifying which name matched.</param>
    /// <param name="arguments">
    /// The rest of the line, with leading whitespace removed and trailing whitespace kept. Some
    /// directives are whitespace-sensitive beyond the first argument, so the text is passed
    /// through rather than pre-tokenised.
    /// </param>
    void Execute(string directive, string arguments);
}
