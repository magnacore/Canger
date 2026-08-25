// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;

namespace Canger.Core.Configuration;

/// <summary>
/// Implements the key binding directives: the <c>map</c>, <c>copymap</c> and <c>unmap</c>
/// families.
/// </summary>
/// <remarks>
/// <para>
/// Each family has one directive per context, distinguished by a prefix letter: none for the
/// browser, <c>c</c> for the console, <c>p</c> for the pager and <c>t</c> for the task view. The
/// deprecated spellings <c>cunmap</c>, <c>punmap</c> and <c>tunmap</c> are accepted as well,
/// because configurations in the wild still use them.
/// </para>
/// <para>
/// The command is taken from the line verbatim after the key sequence, not split into words,
/// because commands contain spaces and their own syntax:
/// <c>map dj eval fm.cut(dirarg=dict(down=1), narg=quantifier)</c> must arrive intact. Macros
/// such as <c>%f</c> are left alone here and expanded when the binding fires, so that they see
/// the state at that moment rather than at load time.
/// </para>
/// </remarks>
/// <param name="keyMaps">The key maps to bind into.</param>
public sealed class KeyBindingDirective(KeyMaps keyMaps) : IConfigurationDirective
{
    /// <summary>Which context and operation each directive name selects.</summary>
    private static readonly Dictionary<string, (KeyContext Context, Operation Operation)> Directives =
        new(StringComparer.Ordinal)
        {
            ["map"] = (KeyContext.Browser, Operation.Bind),
            ["cmap"] = (KeyContext.Console, Operation.Bind),
            ["pmap"] = (KeyContext.Pager, Operation.Bind),
            ["tmap"] = (KeyContext.TaskView, Operation.Bind),

            ["copymap"] = (KeyContext.Browser, Operation.Copy),
            ["copycmap"] = (KeyContext.Console, Operation.Copy),
            ["copypmap"] = (KeyContext.Pager, Operation.Copy),
            ["copytmap"] = (KeyContext.TaskView, Operation.Copy),

            ["unmap"] = (KeyContext.Browser, Operation.Unbind),
            ["uncmap"] = (KeyContext.Console, Operation.Unbind),
            ["unpmap"] = (KeyContext.Pager, Operation.Unbind),
            ["untmap"] = (KeyContext.TaskView, Operation.Unbind),

            // Deprecated spellings, still found in existing configurations.
            ["cunmap"] = (KeyContext.Console, Operation.Unbind),
            ["punmap"] = (KeyContext.Pager, Operation.Unbind),
            ["tunmap"] = (KeyContext.TaskView, Operation.Unbind),
        };

    private enum Operation
    {
        Bind,
        Copy,
        Unbind,
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; } = [.. Directives.Keys];

    /// <inheritdoc />
    public void Execute(string directive, string arguments)
    {
        ArgumentNullException.ThrowIfNull(directive);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Directives.TryGetValue(directive, out (KeyContext Context, Operation Operation) target))
        {
            throw new KeyBindingException($"'{directive}' is not a key binding directive.");
        }

        KeyMap map = keyMaps[target.Context];

        switch (target.Operation)
        {
            case Operation.Bind:
                Bind(map, directive, arguments);
                break;

            case Operation.Copy:
                Copy(map, directive, arguments);
                break;

            case Operation.Unbind:
                foreach (string binding in Words(arguments))
                {
                    map.Unbind(binding);
                }

                break;

            default:
                throw new KeyBindingException($"Unhandled operation for '{directive}'.");
        }
    }

    /// <summary>Binds a key sequence to everything that follows it on the line.</summary>
    private static void Bind(KeyMap map, string directive, string arguments)
    {
        string text = arguments.TrimStart();
        int space = text.IndexOf(' ', StringComparison.Ordinal);

        if (space < 0)
        {
            throw new KeyBindingException($"'{directive}' needs a key sequence and a command.");
        }

        string binding = text[..space];
        string command = text[(space + 1)..].TrimStart();

        if (command.Length == 0)
        {
            throw new KeyBindingException($"'{directive} {binding}' has no command.");
        }

        // A configuration carried over from ranger binds most of the console to Python, and since
        // Escape and Enter are among those bindings, leaving them untranslated would give the
        // user a prompt with no way out. The recognised forms are ranger's own.
        map.Bind(binding, RangerEvalTranslator.TranslateParameterised(command) ?? command);
    }

    /// <summary>Copies one binding, or a whole branch, to one or more destinations.</summary>
    private static void Copy(KeyMap map, string directive, string arguments)
    {
        string[] words = Words(arguments);

        if (words.Length < 2)
        {
            throw new KeyBindingException(
                $"'{directive}' needs a source and at least one destination.");
        }

        foreach (string destination in words[1..])
        {
            map.Copy(words[0], destination);
        }
    }

    private static string[] Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
