// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.Settings;

namespace Canger.Core.Configuration;

/// <summary>
/// Implements the <c>set</c> family of cc.conf directives.
/// </summary>
/// <remarks>
/// <para>
/// Accepted forms, matching ranger's <c>parse_setting_line</c>
/// (<c>ranger/api/commands.py:171-221</c>):
/// </para>
/// <list type="bullet">
///   <item><description><c>set option value</c></description></item>
///   <item><description><c>set option=value</c></description></item>
///   <item><description><c>set option!</c> — toggle a boolean, or cycle an enumerated setting</description></item>
///   <item><description><c>set option</c> — assign the empty string, which is how cc.conf clears
///     <c>global_inode_type_filter</c></description></item>
///   <item><description><c>setinpath path=DIR option=value</c> — scope to a directory</description></item>
///   <item><description><c>setinregex re=PATTERN option=value</c> — scope to matching paths</description></item>
///   <item><description><c>setintag TAGS option=value</c> — scope to tagged files</description></item>
/// </list>
/// </remarks>
/// <param name="settings">The store to assign into.</param>
public sealed class SetDirective(ISettings settings) : IConfigurationDirective
{
    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; } =
        ["set", "setlocal", "setinpath", "setinregex", "setintag"];

    /// <inheritdoc />
    public void Execute(string directive, string arguments)
    {
        ArgumentNullException.ThrowIfNull(directive);
        ArgumentNullException.ThrowIfNull(arguments);

        // "setlocal" is ranger's older spelling of "setinpath".
        (SettingScope scope, string remainder) = directive switch
        {
            "setlocal" or "setinpath" => ParsePathScope(arguments, isRegex: false),
            "setinregex" => ParsePathScope(arguments, isRegex: true),
            "setintag" => ParseTagScope(arguments),
            _ => (SettingScope.Global, arguments),
        };

        Assign(remainder, scope);
    }

    private void Assign(string assignment, SettingScope scope)
    {
        string text = assignment.Trim();
        if (text.Length == 0)
        {
            throw new SettingValueException("A 'set' directive needs an option name.");
        }

        // "option!" toggles rather than assigns.
        if (text.EndsWith('!') && !text.Contains('=', StringComparison.Ordinal))
        {
            settings.Toggle(text[..^1].Trim(), scope);
            return;
        }

        (string name, string value) = SplitAssignment(text);
        settings.SetFromText(name, value, scope);
    }

    /// <summary>
    /// Splits "option=value" or "option value" into its two halves. A bare "option" yields an
    /// empty value, which is a legitimate assignment for string settings.
    /// </summary>
    private static (string Name, string Value) SplitAssignment(string text)
    {
        int equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            return (text[..equals].Trim(), text[(equals + 1)..].Trim());
        }

        int space = text.IndexOf(' ', StringComparison.Ordinal);
        return space >= 0
            ? (text[..space].Trim(), text[(space + 1)..].Trim())
            : (text, string.Empty);
    }

    /// <summary>
    /// Parses the leading operand of a scoped assignment: <c>path=</c>, <c>pattern=</c>,
    /// <c>re=</c> or <c>regex=</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operand may be quoted with either single or double quotes, which is required whenever
    /// it contains whitespace, and a quote inside may be backslash-escaped. Real configurations
    /// depend on this — directory names with spaces are common.
    /// </para>
    /// <para>
    /// The two directives differ in how the operand becomes a pattern, matching
    /// <c>ranger/config/commands.py:558-586</c>. <c>setinpath</c> escapes the operand and anchors
    /// it at the <em>end</em>, so <c>path=build</c> matches a build directory anywhere in the
    /// tree. <c>setinregex</c> uses the operand as a regular expression untouched. Both expand a
    /// leading <c>~</c> first.
    /// </para>
    /// </remarks>
    private static (SettingScope Scope, string Remainder) ParsePathScope(string arguments, bool isRegex)
    {
        Match match = ScopeOperand.Match(arguments.TrimStart());
        if (!match.Success)
        {
            throw new SettingValueException(
                $"Expected '{(isRegex ? "re" : "path")}=...' before the option name.");
        }

        string key = match.Groups["key"].Value;
        string[] accepted = isRegex ? ["re", "regex", "pattern"] : ["path", "pattern"];
        if (!accepted.Contains(key, StringComparer.Ordinal))
        {
            throw new SettingValueException(
                $"'{key}=' is not valid here; expected one of {string.Join(", ", accepted)}.");
        }

        string operand = ExpandHome(Unescape(FirstNonEmpty(match)));
        string pattern = isRegex ? operand : Regex.Escape(operand) + "$";

        return (SettingScope.ForPathPattern(pattern), match.Groups["rest"].Value.TrimStart());
    }

    /// <summary>
    /// Matches <c>key="value with spaces"</c>, <c>key='value'</c> or <c>key=value</c>, capturing
    /// the remainder of the line separately. A closing quote must not be backslash-escaped.
    /// </summary>
    private static readonly Regex ScopeOperand = new(
        """^(?<key>[A-Za-z]+)=(?:"(?<dq>(?:[^"\\]|\\.)*)"|'(?<sq>(?:[^'\\]|\\.)*)'|(?<bare>\S*))\s*(?<rest>.*)$""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string FirstNonEmpty(Match match)
    {
        foreach (string group in (string[])["dq", "sq", "bare"])
        {
            if (match.Groups[group].Success)
            {
                return match.Groups[group].Value;
            }
        }

        return string.Empty;
    }

    /// <summary>Removes the backslashes that protected quotes inside a quoted operand.</summary>
    private static string Unescape(string text) =>
        text.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\'", "'", StringComparison.Ordinal);

    /// <summary>Parses the leading tag-character operand of a <c>setintag</c> assignment.</summary>
    private static (SettingScope Scope, string Remainder) ParseTagScope(string arguments)
    {
        (string tags, string remainder) = SplitFirstWord(arguments.TrimStart());
        if (tags.Length == 0)
        {
            throw new SettingValueException("A 'setintag' directive needs at least one tag.");
        }

        return (SettingScope.ForTags([.. tags]), remainder);
    }

    private static (string Word, string Remainder) SplitFirstWord(string text)
    {
        int space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..].TrimStart());
    }

    private static string ExpandHome(string path) =>
        path.StartsWith('~')
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..])
            : path;
}
