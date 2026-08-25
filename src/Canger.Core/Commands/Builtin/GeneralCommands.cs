// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Settings;

using Canger.Core.Processes;

namespace Canger.Core.Commands.Builtin;

/// <summary>Leaves Canger.</summary>
[Command("quit", Summary = "Close the current tab, or quit when it is the last one.")]
public sealed class QuitCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Quitting.CloseTabOrQuit(FileManager, force: false);
}

/// <summary>Leaves Canger without asking about anything outstanding.</summary>
[Command("quit!", AllowAbbreviation = false,
         Summary = "Quit without asking about running tasks.")]
public sealed class QuitBangCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Quitting.CloseTabOrQuit(FileManager, force: true);
}

/// <summary>Leaves Canger, closing every tab.</summary>
[Command("quitall", Summary = "Quit, closing every tab.")]
public sealed class QuitAllCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Quitting.Quit(FileManager, force: false, "quitall");
}

/// <summary>Leaves Canger, closing every tab, without asking about anything outstanding.</summary>
[Command("quitall!", AllowAbbreviation = false,
         Summary = "Quit every tab without asking about running tasks.")]
public sealed class QuitAllBangCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Quitting.Quit(FileManager, force: true, "quitall");
}

/// <summary>
/// What the four quit commands share.
/// </summary>
/// <remarks>
/// All four used to call <c>Quit()</c> and nothing else, so <c>q</c> closed the whole program even
/// with several tabs open — while its own summary said it closed a tab — and a copy in progress
/// was abandoned without a word. Ranger's rules are in <c>config/commands.py:654-710</c>.
/// </remarks>
internal static class Quitting
{
    /// <summary>
    /// Closes the current tab, or quits when it is the only one.
    /// </summary>
    /// <param name="fileManager">What to act on.</param>
    /// <param name="force">Whether to quit even with work in progress.</param>
    /// <remarks>
    /// This is <c>q</c>. Ranger checks <c>len(self.fm.tabs) &gt;= 2</c> and closes a tab if so
    /// (<c>config/commands.py:666-670</c>), which is what makes one key mean both "I am done with
    /// this tab" and "I am done".
    /// </remarks>
    internal static void CloseTabOrQuit(IFileManager fileManager, bool force)
    {
        ArgumentNullException.ThrowIfNull(fileManager);

        if (fileManager.Tabs.Count >= 2)
        {
            fileManager.CloseTab();
            return;
        }

        Quit(fileManager, force, "quit");
    }

    /// <summary>
    /// Quits, unless there is work in progress and this is not a forced quit.
    /// </summary>
    /// <param name="fileManager">What to act on.</param>
    /// <param name="force">Whether to quit regardless.</param>
    /// <param name="command">The command's name, so the message names its forcing form.</param>
    /// <remarks>
    /// Ranger refuses rather than abandoning a copy halfway, and names the escape hatch in the
    /// message (<c>config/commands.py:660-664</c>). Canger quit regardless, which is a quiet way
    /// to lose half a file.
    /// </remarks>
    internal static void Quit(IFileManager fileManager, bool force, string command)
    {
        ArgumentNullException.ThrowIfNull(fileManager);

        if (!force && fileManager.Tasks.HasWork)
        {
            fileManager.Notify(
                $"Not quitting: Tasks in progress: Use `{command}!` to force quit", isError: true);

            return;
        }

        fileManager.Quit();
    }
}

/// <summary>Changes a setting for one directory only.</summary>
[Command("setlocal", Summary = "Change a setting for a particular directory.")]
public sealed class SetLocalCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        // Scoped settings are stored per path pattern, and applying one at run time needs the
        // same parsing the configuration reader does. Until that is shared, say so plainly.
        FileManager.Notify("setlocal is only available in the configuration file so far",
                           isError: true);
}

/// <summary>Shows a message in the status bar.</summary>
[Command("echo", Summary = "Show a message in the status bar.")]
public sealed class EchoCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.Notify(Rest(1));
}

/// <summary>Changes a setting.</summary>
[Command("set", Summary = "Change a setting: set name value, set name=value, or set name!")]
public sealed class SetCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string text = Rest(1).Trim();

        if (text.Length == 0)
        {
            FileManager.Notify("set: which setting?", isError: true);
            return;
        }

        try
        {
            // A trailing bang toggles rather than assigns, which is what makes a single key able
            // to flip a setting back and forth.
            if (text.EndsWith('!') && !text.Contains('=', StringComparison.Ordinal))
            {
                FileManager.Settings.Raw.Toggle(text[..^1].Trim());
                return;
            }

            int split = text.IndexOfAny([' ', '=']);
            if (split < 0)
            {
                FileManager.Settings.Raw.SetFromText(text, string.Empty);
                return;
            }

            FileManager.Settings.Raw.SetFromText(text[..split], text[(split + 1)..].Trim());
        }
        catch (SettingValueException e)
        {
            FileManager.Notify(e.Message, isError: true);
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction)
    {
        string typed = Rest(1);

        return
        [
            .. SettingsCatalog.Names
                .Where(name => name.StartsWith(typed, StringComparison.Ordinal))
                .Select(name => "set " + name),
        ];
    }
}

/// <summary>Opens the command line, optionally pre-filled.</summary>
/// <remarks>
/// Macros <em>are</em> expanded here. Configuration lines are split on whitespace, so a binding
/// that wants to leave a trailing space writes <c>%space</c>, and one that wants a literal per
/// cent on the line writes <c>%%</c> — which expands to <c>%</c> now and is resolved again when
/// the finished command runs.
/// </remarks>
[Command("console", Summary = "Open the command line, optionally pre-filled.")]
public sealed class ConsoleCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        (string flags, string rest) = Line.ParseFlags();

        // -pN puts the cursor at position N, so a binding can open a partly typed command with
        // the cursor where the user needs to continue.
        int cursor = -1;
        int position = flags.IndexOf('p', StringComparison.Ordinal);
        if (position >= 0 && position + 1 < flags.Length &&
            int.TryParse(flags.AsSpan(position + 1), out int parsed))
        {
            cursor = parsed;
        }

        FileManager.OpenConsole(rest, cursor);
    }
}

/// <summary>Marks or unmarks entries.</summary>
[Command("mark_files", Summary = "Mark or unmark entries.")]
public sealed class MarkFilesCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Dictionary<string, string> named = new(StringComparer.Ordinal);
        for (int i = 1; i < Line.Count; i++)
        {
            string word = Argument(i);
            int equals = word.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                named[word[..equals]] = word[(equals + 1)..];
            }
        }

        bool all = IsTrue(named.GetValueOrDefault("all"));
        bool toggle = IsTrue(named.GetValueOrDefault("toggle"));
        bool value = !named.TryGetValue("val", out string? raw) || IsTrue(raw);

        DirectoryNode directory = FileManager.CurrentDirectory;

        if (all)
        {
            if (toggle)
            {
                directory.ToggleAllMarked();
            }
            else
            {
                directory.SetAllMarked(value);
            }

            // Acting on the whole listing ends a visual selection, because the selection was
            // the thing being replaced (ranger, `core/actions.py:761-763`). Without this `uv`
            // could not unmark at all: it cleared the marks, and the still-running visual range
            // put them straight back on the next redraw.
            FileManager.ChangeMode("normal");

            return;
        }

        if (directory.Cursor.Current is { } entry)
        {
            entry.IsMarked = toggle ? !entry.IsMarked : value;

            // Marking advances, so a run of files can be marked by holding one key.
            FileManager.CurrentTab.MoveCursor(directory.Cursor.Index + 1);
        }
    }

    private static bool IsTrue(string? text) =>
        text is not null &&
        (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1");
}

/// <summary>Runs several commands separated by semicolons.</summary>
[Command("chain", ResolveMacros = false, Summary = "Run several commands in sequence.")]
public sealed class ChainCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        foreach (string part in Rest(1).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string command = part.Trim();
            if (command.Length > 0)
            {
                FileManager.Execute(command, Quantifier);
            }
        }
    }
}

/// <summary>Runs a shell command.</summary>
[Command("shell", EscapeMacrosForShell = true, Summary = "Run a shell command.")]
public sealed class ShellCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        (string flags, string command) = Line.ParseFlags();

        if (command.Trim().Length == 0)
        {
            FileManager.Notify("shell: nothing to run", isError: true);
            return;
        }

        // `-q` puts it on the task queue rather than in front of the interface: the browser stays
        // usable, the job shows in the task view with the spinner, and it can be cancelled from
        // there. Canger's own flag — ranger has nowhere to run a shell command except the
        // foreground, which is why a long conversion there blanks the screen until it is done.
        if (new ProcessFlags(flags).Queued)
        {
            FileManager.RunInBackground(Describe(command), command,
                                        finished: _ => FileManager.ReloadCurrentDirectory());
            return;
        }

        FileManager.RunProgram(command, flags);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Completes the program name against the PATH, which is what ranger's <c>shell</c> does with
    /// <c>get_executables()</c> (<c>config/commands.py</c>). Only while the program itself is
    /// being named: once there is an argument after it, the user is writing the command line and
    /// a list of every binary on the machine would be noise.
    /// </remarks>
    public override IReadOnlyList<string> Complete(int direction)
    {
        (string flags, string command) = Line.ParseFlags();

        // Anything past the first word means the program has been named already.
        if (command.Contains(' ', StringComparison.Ordinal) ||
            command.Contains('\t', StringComparison.Ordinal))
        {
            return [];
        }

        string prefix = Line.Word(0) + (flags.Length > 0 ? " -" + flags : string.Empty) + " ";

        return [.. Executables.Matching(command).Select(program => prefix + program)];
    }

    /// <summary>Names a queued command for the task view.</summary>
    /// <param name="command">The command line.</param>
    /// <returns>Something short enough to read in a list.</returns>
    /// <remarks>
    /// The first word and no more. A command line carrying a selection can run to hundreds of
    /// characters, and the task view has one row for it.
    /// </remarks>
    private static string Describe(string command)
    {
        string trimmed = command.TrimStart();
        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? trimmed : trimmed[..space];
    }
}
