// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using Canger.Core.Commands;
using Canger.Core.Input;
using Canger.Core.Settings;

namespace Canger.App;

/// <summary>
/// Writes Canger's manual page.
/// </summary>
/// <remarks>
/// <para>
/// The reference sections — options, key bindings, settings, commands — are generated from the
/// same metadata the program itself uses, so they cannot drift out of step with it. A hand-written
/// reference for eighty settings and eighty commands is out of date the moment either changes,
/// and quietly so; this is wrong only if the program is.
/// </para>
/// <para>
/// The prose sections are written by hand, because they explain things no metadata knows: what
/// the layout means, which files are read in what order, and why.
/// </para>
/// </remarks>
public static class ManPage
{
    /// <summary>
    /// Renders the manual page in the <c>man</c> macro language.
    /// </summary>
    /// <param name="version">The version to stamp on it.</param>
    /// <param name="keyMaps">The bindings to document.</param>
    /// <param name="commands">The commands to document.</param>
    /// <returns>The page, ready to write to <c>canger.1</c>.</returns>
    public static string Render(string version, KeyMaps keyMaps, CommandRegistry commands)
    {
        ArgumentNullException.ThrowIfNull(keyMaps);
        ArgumentNullException.ThrowIfNull(commands);

        StringBuilder man = new();

        man.Append(CultureInfo.InvariantCulture,
                   $""".TH CANGER 1 "" "canger {version}" "Canger Manual"{'\n'}""");

        Prose(man);
        Options(man);
        Bindings(man, keyMaps);
        Settings(man);
        Commands(man, commands);
        Files(man);

        return man.ToString();
    }

    /// <summary>The hand-written sections: what Canger is and how it is laid out.</summary>
    private static void Prose(StringBuilder man)
    {
        man.Append("""
                   .SH NAME
                   canger \- a file manager for the terminal
                   .SH SYNOPSIS
                   .B canger
                   .RI [ options ]
                   .RI [ path ...]
                   .SH DESCRIPTION
                   .B Canger
                   is a file manager with vi-style key bindings, a port of
                   .B ranger
                   to .NET. It shows a directory as a set of columns: the path leading to where you
                   are, the listing itself, and a preview of whatever the cursor is on. Moving right
                   enters a directory or opens a file; moving left goes back up.
                   .PP
                   Almost everything is a command, and almost every key is bound to one. Typing
                   .B :
                   opens a prompt where the same commands can be typed by name, which is how to
                   reach the ones no key is bound to.
                   .SH CONCEPTS
                   .SS Selection
                   Commands act on the
                   .IR selection ,
                   which is every marked file, or the file under the cursor when nothing is marked.
                   .B Space
                   marks a file and moves on, so a run of files is marked by holding one key.
                   .SS Tabs
                   Several directories can be open at once.
                   .B gn
                   opens a tab,
                   .B gt
                   moves between them, and
                   .B ~
                   switches to a view showing every tab side by side rather than one path.
                   .SS Copying
                   .BR yy " copies and " dd " cuts; " pp
                   pastes. Both take a direction, so
                   .B 3dj
                   cuts the next three files without marking anything.
                   .PP
                   On a copy-on-write filesystem such as btrfs or XFS, a copy within one filesystem
                   is cloned rather than duplicated: it completes instantly and consumes no extra
                   space until one of the copies is written to. The task view reports which files
                   were cloned and which were copied, along with a rate and an estimate of the time
                   remaining for the latter.
                   .SS Removable drives
                   .B <F9>
                   lists the drives that can be unplugged, with
                   .BR m " to mount, " u " to unmount, " l " to unlock an encrypted one, " e
                   to remove it safely, and
                   .B Enter
                   to mount it and go there.
                   .PP
                   Safely removing a drive unmounts everything on it, locks every encrypted
                   container on it, and then cuts its power, stopping at the first step that
                   fails. Everything is done through
                   .BR udisksctl (1),
                   so a drive mounted here behaves exactly as one mounted from a desktop file
                   manager, and a passphrase is typed to udisks rather than to Canger. Nothing is
                   ever forced: a filesystem in use fails to unmount and says so. Ranger has no
                   equivalent.
                   .PP
                   An encrypted drive is unlocked by
                   .BR udisksctl (1),
                   which prompts for the passphrase itself. With
                   .B unlock_prompt
                   set to
                   .BR builtin ,
                   Canger asks instead, with the typing hidden, and offers to remember the answer
                   for the session or in the desktop keyring \(em under the same schema Thunar and
                   GNOME Disks use, so a passphrase saved in either unlocks the drive in the
                   other. The passphrase reaches udisks down a pipe and is kept out of the command
                   history.
                   .B :forget_passphrases
                   drops what is held in memory without touching the keyring.
                   .SS Version control
                   With
                   .B vcs_aware
                   set, files carry a marker showing their status, the branch appears in the title
                   bar and the latest commit on the status line.
                   .BR :stage " and " :unstage
                   move the selection in and out of the index.

                   """);
    }

    /// <summary>The command-line options, taken from the same text <c>--help</c> prints.</summary>
    private static void Options(StringBuilder man)
    {
        man.Append(".SH OPTIONS\n");

        foreach (string line in CommandLineOptions.Usage.Split('\n'))
        {
            string trimmed = line.TrimStart();

            // The usage text has one option per line, indented, with the description after it.
            if (!trimmed.StartsWith('-'))
            {
                continue;
            }

            int gap = trimmed.IndexOf("  ", StringComparison.Ordinal);

            if (gap < 0)
            {
                man.Append(CultureInfo.InvariantCulture, $".TP\n.B {Escape(trimmed)}\n");
                continue;
            }

            man.Append(CultureInfo.InvariantCulture,
                       $".TP\n.B {Escape(trimmed[..gap])}\n{Escape(trimmed[gap..].Trim())}\n");
        }
    }

    /// <summary>The key bindings, one section per context.</summary>
    private static void Bindings(StringBuilder man, KeyMaps keyMaps)
    {
        man.Append(".SH KEY BINDINGS\n");

        foreach ((KeyContext context, string title) in ((KeyContext, string)[])
                 [
                     (KeyContext.Browser, "Browser"),
                     (KeyContext.Console, "Console"),
                     (KeyContext.Pager, "Pager"),
                     (KeyContext.TaskView, "Task view"),
                     (KeyContext.Devices, "Devices"),
                 ])
        {
            man.Append(CultureInfo.InvariantCulture, $".SS {title}\n");

            // Sorted by what is shown rather than by keycode, so the page reads as a reference
            // rather than as a dump of the trie's internal order.
            IEnumerable<(string Keys, string Command)> bindings = keyMaps[context]
                .Enumerate()
                .Select(b => (Keys: KeyCodes.ToDisplayString(b.Keys), b.Command))
                .OrderBy(b => b.Keys, StringComparer.Ordinal);

            foreach ((string keys, string command) in bindings)
            {
                man.Append(CultureInfo.InvariantCulture,
                           $".TP\n.B {Escape(keys)}\n{Escape(command)}\n");
            }
        }
    }

    /// <summary>Every setting, with its type, default and permitted values.</summary>
    private static void Settings(StringBuilder man)
    {
        man.Append(".SH SETTINGS\n");
        man.Append("Set with\n.BR :set \" in a configuration file or at the prompt.\"\n");

        foreach (SettingDefinition setting in
                 SettingsCatalog.All.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            man.Append(CultureInfo.InvariantCulture, $".TP\n.B {Escape(setting.Name)}\n");

            if (setting.Summary.Length > 0)
            {
                man.Append(CultureInfo.InvariantCulture, $"{Escape(setting.Summary)}\n");
            }

            man.Append(CultureInfo.InvariantCulture,
                       $".br\nType: {setting.Kind.ToString().ToLowerInvariant()}. ");

            if (setting.IsEnumerated)
            {
                man.Append(CultureInfo.InvariantCulture,
                           $"One of: {Escape(string.Join(", ", setting.AllowedValues!))}. ");
            }

            man.Append(CultureInfo.InvariantCulture,
                       $"Default: {Escape(Describe(setting.DefaultValue))}.\n");
        }
    }

    /// <summary>Every command, with the summary it declares.</summary>
    private static void Commands(StringBuilder man, CommandRegistry commands)
    {
        man.Append(".SH COMMANDS\n");
        man.Append("Names may be abbreviated to any unambiguous prefix.\n");

        foreach (CommandDescriptor command in commands.All())
        {
            man.Append(CultureInfo.InvariantCulture,
                       $".TP\n.B :{Escape(command.Name)}\n{Escape(command.Summary)}\n");
        }
    }

    /// <summary>Where Canger reads and writes, and what it reads from the environment.</summary>
    private static void Files(StringBuilder man)
    {
        man.Append("""
                   .SH FILES
                   Configuration is read from the shipped defaults, then
                   .IR /etc/canger/ ,
                   then
                   .IR ~/.config/canger/ ,
                   each adding to the last. A personal file therefore needs only the lines it
                   changes.
                   .TP
                   .I ~/.config/canger/cc.conf
                   Settings and key bindings. Every line is a command, the same ones the prompt
                   accepts.
                   .TP
                   .I ~/.config/canger/commands.cs
                   Commands written in C#, compiled at startup.
                   .TP
                   .I ~/.config/canger/rifle.conf
                   Which program opens which file. Unlike cc.conf this is not additive: a personal
                   copy replaces the shipped rules entirely, because a rule's meaning depends on
                   the rules before it.
                   .TP
                   .I ~/.config/canger/scope.sh
                   Generates previews. The interface is the same as ranger's, so an existing script
                   works unchanged.
                   .TP
                   .I ~/.config/canger/plugins/
                   Plugins, as C# source or compiled assemblies. Names beginning with an underscore
                   are skipped, which is how one is disabled without deleting it.
                   .TP
                   .I ~/.local/share/canger/
                   State rather than configuration: bookmarks, tags and history.
                   .SH ENVIRONMENT
                   .TP
                   .B CANGER_INSTALL_DIR
                   Where Canger is installed. Set by Canger itself, so a binding or a shell command
                   can reach the shipped configuration by name.
                   .TP
                   .B CANGER_CONFIG_DIR
                   The configuration directory in use.
                   .TP
                   .BR VISUAL ", " EDITOR
                   Which editor opens a file.
                   .TP
                   .BR XDG_CONFIG_HOME ", " XDG_DATA_HOME ", " XDG_CACHE_HOME
                   Where configuration, state and cached data live.
                   .SH SEE ALSO
                   .BR rifle (1),
                   .BR udisksctl (1)
                   .SH LICENSE
                   GNU General Public License version 3 or later.

                   """);
    }

    /// <summary>Renders a default value the way the configuration file would spell it.</summary>
    private static string Describe(object? value) => value switch
    {
        null => "unset",
        bool flag => flag ? "true" : "false",
        IEnumerable<int> numbers => string.Join(",", numbers),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Escapes text for the man macro language.
    /// </summary>
    /// <remarks>
    /// A backslash begins an escape, and a line starting with a full stop or an apostrophe is
    /// read as a macro rather than as text — which is how a key binding for <c>.</c> would
    /// otherwise silently swallow the rest of the page.
    /// </remarks>
    private static string Escape(string text)
    {
        string escaped = text.Replace("\\", "\\e", StringComparison.Ordinal);

        return escaped.StartsWith('.') || escaped.StartsWith('\'')
            ? "\\&" + escaped
            : escaped;
    }
}
