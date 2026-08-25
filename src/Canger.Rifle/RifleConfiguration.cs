// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Rifle;

/// <summary>A line of <c>rifle.conf</c> that could not be understood.</summary>
/// <param name="LineNumber">The one-based line number.</param>
/// <param name="Line">The line as written.</param>
/// <param name="Message">What was wrong with it.</param>
public readonly record struct RifleConfigurationError(int LineNumber, string Line, string Message);

/// <summary>
/// The rules that decide which program opens which file.
/// </summary>
/// <remarks>
/// <para>
/// The format is one rule per line: conditions, an equals sign, then the command. Conditions are
/// separated by commas and all must hold. Rules are tried in order, so the file reads as a list
/// of preferences from most specific to most general.
/// </para>
/// <para>
/// Only the <em>first</em> equals sign separates the conditions from the command, because a
/// command routinely contains one.
/// </para>
/// </remarks>
public sealed class RifleConfiguration
{
    private readonly List<RifleRule> _rules = [];
    private readonly List<RifleConfigurationError> _errors = [];

    /// <summary>The rules, in the order they will be tried.</summary>
    public IReadOnlyList<RifleRule> Rules => _rules;

    /// <summary>Lines that could not be understood.</summary>
    public IReadOnlyList<RifleConfigurationError> Errors => _errors;

    /// <summary>Reads rules from a file.</summary>
    /// <param name="path">Path to the configuration.</param>
    /// <returns>The rules.</returns>
    public static RifleConfiguration FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return FromLines(File.ReadLines(path));
    }

    /// <summary>Reads rules from lines of configuration.</summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The rules.</returns>
    public static RifleConfiguration FromLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        RifleConfiguration configuration = new();
        int lineNumber = 0;

        foreach (string line in lines)
        {
            lineNumber++;
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            int equals = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                configuration._errors.Add(new RifleConfigurationError(
                    lineNumber, trimmed, "no '=' separating the conditions from the command"));
                continue;
            }

            string conditionText = trimmed[..equals];
            string command = trimmed[(equals + 1)..].Trim();

            if (command.Length == 0)
            {
                configuration._errors.Add(new RifleConfigurationError(
                    lineNumber, trimmed, "no command after the '='"));
                continue;
            }

            IReadOnlyList<RifleCondition> conditions =
            [
                .. conditionText
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(RifleCondition.Parse),
            ];

            configuration._rules.Add(new RifleRule(conditions, command, lineNumber));
        }

        return configuration;
    }

    /// <summary>
    /// The candidates for opening a file, in preference order.
    /// </summary>
    /// <remarks>
    /// Numbering is by position among the rules that apply, so <c>:open_with 2</c> means "the
    /// second thing that could open this", not "the second rule in the file". A rule may claim a
    /// particular number with a <c>number</c> condition, which is how a configuration pins a
    /// choice to a key regardless of what else is installed.
    /// </remarks>
    /// <param name="context">What is known about the file.</param>
    /// <returns>The applicable rules, numbered.</returns>
    public IReadOnlyList<NumberedMatch> Candidates(RifleContext context)
    {
        List<NumberedMatch> matches = [];
        int next = 0;

        foreach (RifleRule rule in _rules)
        {
            if (rule.Match(context) is not { } match)
            {
                continue;
            }

            int number = match.Number ?? next;
            next = number + 1;

            matches.Add(new NumberedMatch(number, match));
        }

        return matches;
    }
}

/// <summary>A rule that applies, and the position it occupies among the alternatives.</summary>
/// <param name="Number">Its position, as <c>:open_with</c> counts them.</param>
/// <param name="Match">The rule and how it should be run.</param>
public readonly record struct NumberedMatch(int Number, RifleMatch Match);
