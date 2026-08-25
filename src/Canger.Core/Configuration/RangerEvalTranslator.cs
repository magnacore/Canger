// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Canger.Core.Configuration;

/// <summary>
/// Recognises the Python <c>eval</c> bindings ranger ships and gives Canger's equivalent.
/// </summary>
/// <remarks>
/// <para>
/// Ranger's own <c>rc.conf</c> binds most of the console — Escape, Enter, the arrow keys, every
/// editing key — to <c>eval fm.ui.console.…</c>. A configuration carried over from ranger
/// therefore <em>overrides</em> Canger's working bindings with Python that cannot run, and since
/// Escape and Enter are among them the result is a prompt with no way out. That is bad enough to
/// be worth translating rather than merely reporting.
/// </para>
/// <para>
/// This is a fixed table, not an interpreter. It covers the forms ranger itself ships, which is
/// what appears in a carried-over configuration; anything else is left alone and reported by the
/// <c>eval</c> machinery as usual. Matching ignores whitespace inside the call so that a
/// hand-edited line still resolves.
/// </para>
/// </remarks>
public static partial class RangerEvalTranslator
{
    /// <summary>
    /// The forms ranger ships, and what Canger calls them.
    /// </summary>
    /// <remarks>
    /// Keys are normalised — all whitespace removed — so the lookup does not care how the
    /// original was spaced.
    /// </remarks>
    private static readonly Dictionary<string, string> Equivalents = new(StringComparer.Ordinal)
    {
        // The console. These are the ones that matter: without them the prompt cannot be left.
        ["fm.ui.console.close()"] = "console_close",
        ["fm.ui.console.close(True)"] = "console_close",
        ["fm.ui.console.execute()"] = "console_accept",
        ["fm.ui.console.tab()"] = "console_complete",
        ["fm.ui.console.tab(-1)"] = "console_complete_back",
        ["fm.ui.console.history_move(-1)"] = "console_history_back",
        ["fm.ui.console.history_move(1)"] = "console_history_forward",
        ["fm.ui.console.move(left=1)"] = "console_left",
        ["fm.ui.console.move(right=1)"] = "console_right",
        ["fm.ui.console.move(right=0,absolute=True)"] = "console_home",
        ["fm.ui.console.move(right=-1,absolute=True)"] = "console_end",
        ["fm.ui.console.move_word(left=1)"] = "console_word_left",
        ["fm.ui.console.move_word(right=1)"] = "console_word_right",
        ["fm.ui.console.delete(-1)"] = "console_delete_back",
        ["fm.ui.console.delete(0)"] = "console_delete",
        ["fm.ui.console.delete_word()"] = "console_delete_word",
        ["fm.ui.console.delete_word(backward=False)"] = "console_delete_word",
        ["fm.ui.console.delete_rest(1)"] = "console_delete_to_end",
        ["fm.ui.console.delete_rest(-1)"] = "console_delete_to_start",
        ["fm.ui.console.paste()"] = "console_paste",

        // The task view, which ranger also drives entirely through eval.
        ["fm.ui.taskview.task_move(-1)"] = "task_move_down",
        ["fm.ui.taskview.task_move(0)"] = "task_move_up",
        ["fm.ui.taskview.task_remove()"] = "task_remove",

        // The browser forms that became named commands with arguments.
        ["fm.cut(dirarg=dict(down=1),narg=quantifier)"] = "cut down=1",
        ["fm.cut(dirarg=dict(up=1),narg=quantifier)"] = "cut up=1",
        ["fm.cut(dirarg=dict(to=0),narg=quantifier)"] = "cut to=0",
        ["fm.cut(dirarg=dict(to=-1),narg=quantifier)"] = "cut to=-1",
        ["fm.copy(dirarg=dict(down=1),narg=quantifier)"] = "copy down=1",
        ["fm.copy(dirarg=dict(up=1),narg=quantifier)"] = "copy up=1",
        ["fm.copy(dirarg=dict(to=0),narg=quantifier)"] = "copy to=0",
        ["fm.copy(dirarg=dict(to=-1),narg=quantifier)"] = "copy to=-1",
    };

    /// <summary>
    /// Translates a binding's action, when it is one of ranger's <c>eval</c> forms.
    /// </summary>
    /// <param name="action">The action as the configuration wrote it.</param>
    /// <returns>
    /// Canger's equivalent, or <see langword="null"/> when this is not a form we recognise —
    /// including anything that is not an <c>eval</c> at all.
    /// </returns>
    public static string? Translate(string action)
    {
        ArgumentNullException.ThrowIfNull(action);

        string trimmed = action.TrimStart();

        if (!trimmed.StartsWith("eval ", StringComparison.Ordinal))
        {
            return null;
        }

        string snippet = trimmed["eval ".Length..].Trim();

        // -q only suppresses eval's own report, which a translated binding does not produce.
        if (snippet.StartsWith("-q", StringComparison.Ordinal))
        {
            snippet = snippet[2..].Trim();
        }

        string normalised = Whitespace().Replace(snippet, string.Empty).TrimEnd(';');

        return Equivalents.TryGetValue(normalised, out string? equivalent) ? equivalent : null;
    }

    /// <summary>
    /// Expands ranger's <c>for … in "chars": cmd("…{0}…".format(arg))</c> idiom.
    /// </summary>
    /// <param name="code">The snippet, without the leading <c>eval</c>.</param>
    /// <returns>
    /// One configuration line per character, or <see langword="null"/> when the snippet is not
    /// this shape.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the only <c>eval</c> in ranger's *own* shipped <c>rc.conf</c>, where it writes the
    /// sixty chmod bindings without sixty lines:
    /// </para>
    /// <code>
    /// eval for arg in "rwxXst": cmd("map +u{0} shell -f chmod u+{0} %s".format(arg))
    /// </code>
    /// <para>
    /// Worth translating rather than telling the user to rewrite it: it appears in every
    /// configuration derived from ranger's default, and Canger's own equivalent is sixty
    /// hand-written lines that can drift from it. Bounded and total — a fixed string, one
    /// substitution, no evaluation — so there is no Python here to be wrong about.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string>? ExpandLoop(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (CharacterLoop().Match(code) is not { Success: true } loop)
        {
            return null;
        }

        // The value formatted in has to be the loop variable; anything else is a snippet doing
        // something this does not model.
        if (!string.Equals(loop.Groups["var"].Value, loop.Groups["arg"].Value,
                           StringComparison.Ordinal))
        {
            return null;
        }

        string template = loop.Groups["template"].Value;

        return
        [
            .. loop.Groups["chars"].Value.Select(
                c => template.Replace("{0}", c.ToString(), StringComparison.Ordinal)),
        ];
    }

    /// <summary>
    /// <c>for arg in "chars": cmd("template".format(arg))</c>, however it is spaced.
    /// </summary>
    [GeneratedRegex(@"^\s*for\s+(?<var>\w+)\s+in\s+""(?<chars>[^""]*)""\s*:\s*cmd\(\s*""(?<template>[^""]*)""\s*\.format\(\s*(?<arg>\w+)\s*\)\s*\)\s*$")]
    private static partial Regex CharacterLoop();

    /// <summary>
    /// Two special cases that need a value out of the original rather than a fixed answer.
    /// </summary>
    /// <param name="action">The action as the configuration wrote it.</param>
    /// <returns>Canger's equivalent, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Kept apart from the table because they carry a path or a name that has to be preserved.
    /// <c>fm.cd('/run/media/' + os.getenv('USER'))</c> is the shape ranger uses for a mount
    /// point, and the rename prompts differ only in where the cursor lands.
    /// </remarks>

    public static string? TranslateParameterised(string action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Translate(action) is { } exact)
        {
            return exact;
        }

        string trimmed = action.TrimStart();

        if (!trimmed.StartsWith("eval ", StringComparison.Ordinal))
        {
            return null;
        }

        // Two views of the same snippet. The compact one makes matching indifferent to spacing;
        // the original is the only one that can be trusted for a value, because a path may well
        // contain spaces — "~/Documents/SENSITIVE DATA" is not the same as "~/Documents/SENSITIVEDATA".
        string original = trimmed["eval ".Length..];
        string snippet = Whitespace().Replace(original, string.Empty);

        // The rename prompts: position=7 is the length of "rename ", so the cursor sits at the
        // start of the name; without it, at the end.
        if (snippet.Contains("fm.open_console('rename'+", StringComparison.Ordinal)
            || snippet.Contains("fm.open_console(\"rename\"+", StringComparison.Ordinal)
            || snippet.Contains("fm.open_console('rename'", StringComparison.Ordinal))
        {
            return snippet.Contains("position=7", StringComparison.Ordinal)
                ? "rename_append where=start"
                : "rename_append where=end";
        }

        // A mount point built from the user name.
        if (MediaDirectory().Match(snippet) is { Success: true } media)
        {
            return $"cd {media.Groups[1].Value}$USER";
        }

        // Where ranger itself is installed.
        if (snippet.Contains("fm.cd(ranger.RANGERDIR)", StringComparison.Ordinal))
        {
            return "cd $CANGER_INSTALL_DIR";
        }

        // A tab opened at a fixed path, which is how a config keeps a set of working
        // directories one keystroke away. Ranger's `narg` names the tab; Canger keeps the name
        // on the tab so it can be shown, and the path is the part that has to be right.
        if (NewTabWithPath().Match(original) is { Success: true } tab)
        {
            string label = TabLabel().Match(original) is { Success: true } named
                ? $" label={named.Groups[1].Value}"
                : string.Empty;

            return $"tab_new {tab.Groups[1].Value}{label}";
        }

        // A tab opened at the current directory, written as a macro.
        if (snippet.Contains("fm.tab_new('%d')", StringComparison.Ordinal)
            || snippet.Contains("fm.tab_new(\"%d\")", StringComparison.Ordinal))
        {
            return "tab_new %d";
        }

        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"fm\.cd\('([^']*)'\+os\.getenv\('USER'\)\)")]
    private static partial Regex MediaDirectory();

    [GeneratedRegex(@"fm\.tab_new\([^)]*path=[""'](.+?)[""']")]
    private static partial Regex NewTabWithPath();

    [GeneratedRegex(@"narg=[""'](.+?)[""']")]
    private static partial Regex TabLabel();
}
