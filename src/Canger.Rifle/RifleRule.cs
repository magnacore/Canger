// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Rifle;

/// <summary>
/// One line of <c>rifle.conf</c>: some conditions, and the command to run when they all hold.
/// </summary>
/// <param name="Conditions">Every condition that must hold.</param>
/// <param name="Command">The command line, with <c>$@</c> standing for the files.</param>
/// <param name="SourceLine">Which line of the configuration this came from, for error messages.</param>
public sealed record RifleRule(
    IReadOnlyList<RifleCondition> Conditions,
    string Command,
    int SourceLine)
{
    /// <summary>The command meaning "ask the user which program to use".</summary>
    public const string AskCommand = "ask";

    /// <summary>Whether this rule asks rather than runs.</summary>
    public bool IsAsk => string.Equals(Command, AskCommand, StringComparison.Ordinal);

    /// <summary>
    /// Tests every condition against a file.
    /// </summary>
    /// <remarks>
    /// All conditions must hold, and evaluation stops at the first that does not — which matters
    /// because a condition may run <c>file</c> or search the path, and a configuration has
    /// hundreds of rules.
    /// </remarks>
    /// <param name="context">What is known about the file.</param>
    /// <returns>The match, or <see langword="null"/> when the rule does not apply.</returns>
    public RifleMatch? Match(RifleContext context)
    {
        string flags = string.Empty;
        string? label = null;
        int? number = null;

        foreach (RifleCondition condition in Conditions)
        {
            ConditionResult result = condition.Evaluate(context);

            if (!result.Matched)
            {
                return null;
            }

            // A later flag replaces an earlier one rather than adding to it, matching ranger.
            if (result.Flags is not null)
            {
                flags = result.Flags;
            }

            label ??= result.Label;
            number ??= result.Number;
        }

        return new RifleMatch(this, flags, label, number);
    }
}

/// <summary>A rule that applies to a file, and what it said about how to run.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Flags">How to run the command.</param>
/// <param name="Label">The name this way of opening the file goes by, when it has one.</param>
/// <param name="Number">The position the rule asked for, when it asked for one.</param>
public sealed record RifleMatch(RifleRule Rule, string Flags, string? Label, int? Number)
{
    /// <summary>The command line to run.</summary>
    public string Command => Rule.Command;

    /// <summary>Whether this asks rather than runs.</summary>
    public bool IsAsk => Rule.IsAsk;
}
