// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.FileSystem;
using Canger.Core.Processes;

namespace Canger.Rifle;

/// <summary>What is known about the file a rule is being tested against.</summary>
/// <param name="Path">Absolute path.</param>
/// <param name="MimeType">Its media type, when one could be determined.</param>
/// <param name="Label">A label the caller is looking for, when it asked for one.</param>
/// <param name="HasGraphicalDisplay">Whether a graphical session is available.</param>
/// <param name="HasTerminal">Whether a terminal is available.</param>
/// <param name="FileSystem">Used to test what the path is.</param>
public readonly record struct RifleContext(
    string Path,
    string? MimeType,
    string? Label,
    bool HasGraphicalDisplay,
    bool HasTerminal,
    IFileSystem FileSystem);

/// <summary>
/// The result of testing one condition, which is not simply true or false.
/// </summary>
/// <remarks>
/// Some conditions do not answer a question at all: <c>flag</c>, <c>label</c> and <c>number</c>
/// tell the engine <em>how</em> to run the rule and always pass. Ranger models this by having
/// them mutate state while being evaluated; naming the outcomes instead makes the effect visible
/// rather than hidden in an evaluation order.
/// </remarks>
public readonly record struct ConditionResult(bool Matched, string? Flags = null,
                                              string? Label = null, int? Number = null)
{
    /// <summary>The condition did not match.</summary>
    public static ConditionResult No => new(false);

    /// <summary>The condition matched.</summary>
    public static ConditionResult Yes => new(true);
}

/// <summary>
/// One test in a rifle rule, such as <c>ext pdf</c> or <c>has zathura</c>.
/// </summary>
/// <param name="Name">The condition's name.</param>
/// <param name="Argument">Its argument, or an empty string when it takes none.</param>
/// <param name="Negated">Whether the condition was written with a leading <c>!</c>.</param>
public sealed record RifleCondition(string Name, string Argument, bool Negated)
{
    /// <summary>
    /// Parses a condition as written in the configuration.
    /// </summary>
    /// <param name="text">The condition, for example <c>!mime ^text</c>.</param>
    /// <returns>The parsed condition.</returns>
    public static RifleCondition Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();
        bool negated = trimmed.StartsWith('!');

        if (negated)
        {
            trimmed = trimmed[1..].TrimStart();
        }

        // The name is one word; everything after the first run of whitespace is the argument,
        // spaces included, because a regular expression may contain them.
        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return space < 0
            ? new RifleCondition(trimmed, string.Empty, negated)
            : new RifleCondition(trimmed[..space], trimmed[(space + 1)..].Trim(), negated);
    }

    /// <summary>
    /// Tests this condition against a file.
    /// </summary>
    /// <remarks>
    /// Two of ranger's behaviours are preserved deliberately because real configurations depend
    /// on them. An unrecognised condition never matches, so a typo disables its rule rather than
    /// enabling it for everything. And <c>ext</c> does not match a directory, so <c>!ext</c>
    /// <em>does</em> — which is how rules are written to apply to directories only.
    /// </remarks>
    /// <param name="context">What is known about the file.</param>
    /// <returns>Whether it matched, and anything it wants to tell the engine.</returns>
    public ConditionResult Evaluate(RifleContext context)
    {
        ConditionResult result = EvaluateCore(context);

        return Negated
            ? result with { Matched = !result.Matched }
            : result;
    }

    private ConditionResult EvaluateCore(RifleContext context)
    {
        switch (Name)
        {
            case "ext":
                {
                    // Deliberately false for anything that is not a regular file.
                    if (context.FileSystem.GetStatus(context.Path, followSymbolicLinks: true)
                        is not { Kind: FileKind.Regular })
                    {
                        return ConditionResult.No;
                    }

                    string basename = System.IO.Path.GetFileName(context.Path);
                    int dot = basename.LastIndexOf('.');

                    // A leading dot does not begin an extension, so a dotfile has none.
                    if (dot <= 0 || dot == basename.Length - 1)
                    {
                        return ConditionResult.No;
                    }

                    return Match($"^({Argument})$", basename[(dot + 1)..].ToLowerInvariant());
                }

            case "name":
                return Match(Argument, System.IO.Path.GetFileName(context.Path));

            case "match":
                return Match(Argument, context.Path);

            case "path":
                return Match(Argument, System.IO.Path.GetFullPath(context.Path));

            case "mime":
                return context.MimeType is { } mime ? Match(Argument, mime) : ConditionResult.No;

            case "file":
                return new ConditionResult(
                    context.FileSystem.GetStatus(context.Path, followSymbolicLinks: true)
                        is { Kind: FileKind.Regular });

            case "directory":
                return new ConditionResult(context.FileSystem.DirectoryExists(context.Path));

            case "has":
                return new ConditionResult(HasProgram(Argument));

            case "env":
                return new ConditionResult(
                    Environment.GetEnvironmentVariable(Argument) is { Length: > 0 });

            case "terminal":
                return new ConditionResult(context.HasTerminal);

            case "X":
                return new ConditionResult(context.HasGraphicalDisplay);

            case "else":
                return ConditionResult.Yes;

            // These describe how to run the rule rather than whether it applies.
            case "flag":
                return new ConditionResult(true, Flags: Argument);

            case "label":
                return new ConditionResult(
                    context.Label is null ||
                    string.Equals(context.Label, Argument, StringComparison.Ordinal),
                    Label: Argument);

            case "number":
                return new ConditionResult(
                    true,
                    Number: int.TryParse(Argument, out int number) ? number : null);

            default:
                // A typo disables its rule rather than enabling it for everything.
                return ConditionResult.No;
        }
    }

    /// <summary>Whether a program, or the program a variable names, is on the path.</summary>
    private static bool HasProgram(string argument)
    {
        if (argument.StartsWith('$'))
        {
            string? value = Environment.GetEnvironmentVariable(argument[1..]);
            return value is { Length: > 0 } && Executables.Exists(value);
        }

        return Executables.Exists(argument);
    }

    private static ConditionResult Match(string pattern, string text)
    {
        try
        {
            return new ConditionResult(
                Regex.IsMatch(text, pattern,
                              RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
        }
        catch (ArgumentException)
        {
            // A malformed pattern disables its rule rather than stopping the whole config.
            return ConditionResult.No;
        }
    }
}
