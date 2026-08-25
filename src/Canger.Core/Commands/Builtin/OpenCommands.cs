// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Processes;

namespace Canger.Core.Commands.Builtin;

/// <summary>Opens the selection with whichever program the rules choose.</summary>
[Command("open", Summary = "Open the selection with the configured program.")]
public sealed class OpenCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // Entering a directory is the same gesture as opening a file, so one command does both.
        // The decision is made on the selection rather than the cursor: with files marked, the
        // user has asked for those files, even if the cursor happens to rest on a directory.
        if (FileManager.Selection is [{ IsDirectory: true } directory])
        {
            FileManager.CurrentTab.MoveCursorTo(directory);
            FileManager.CurrentTab.EnterSelected();
            return;
        }

        OpenSelection(FileManager, number: Quantifier ?? 0);
    }

    /// <summary>Opens the selection, reporting anything that goes wrong.</summary>
    /// <param name="fileManager">What to act on.</param>
    /// <param name="number">Which alternative to use.</param>
    /// <param name="label">Use the alternative with this name instead of counting.</param>
    /// <param name="flags">Extra flags for the runner.</param>
    internal static void OpenSelection(IFileManager fileManager, int number = 0,
                                       string? label = null, string flags = "")
    {
        string[] paths = [.. fileManager.Selection.Select(f => f.Path)];

        if (paths.Length == 0)
        {
            fileManager.Notify("nothing selected", isError: true);
            return;
        }

        OpenResult result = fileManager.Opener.Open(paths, number, label, flags);

        if (result.NeedsUserChoice)
        {
            // The rules deliberately decline to choose, so the question is handed to the user
            // rather than guessed at.
            fileManager.OpenConsole("open_with ");
            return;
        }

        if (!result.Succeeded)
        {
            fileManager.Notify(result.Message ?? "could not open", isError: true);
        }
    }
}

/// <summary>Opens the selection with a named or numbered alternative.</summary>
[Command("open_with", Summary = "Open the selection with a particular program, by name or number.")]
public sealed class OpenWithCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string argument = Rest(1).Trim();

        if (argument.Length == 0)
        {
            OpenCommand.OpenSelection(FileManager);
            return;
        }

        // The argument may name the program, number the alternative, or carry runner flags. They
        // are told apart by shape, so ":open_with 2", ":open_with editor" and ":open_with f" all
        // do what they look like.
        string? label = null;
        int number = 0;
        System.Text.StringBuilder flags = new();

        foreach (string part in argument.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int parsed))
            {
                number = parsed;
            }
            else if (part.Length <= 4 && part.All(c => "sfpwrtcSFPWRTC".Contains(c, StringComparison.Ordinal)))
            {
                flags.Append(part);
            }
            else
            {
                label = part;
            }
        }

        OpenCommand.OpenSelection(FileManager, number, label, flags.ToString());
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction)
    {
        if (FileManager.CurrentFile is not { } file)
        {
            return [];
        }

        return
        [
            .. FileManager.Opener.Alternatives(file.Path)
                .Where(a => a.Label is { Length: > 0 })
                .Select(a => "open_with " + a.Label),
        ];
    }
}

/// <summary>Lists the ways the selection could be opened.</summary>
[Command("draw_possible_programs", Summary = "List the programs that could open the selection.")]
public sealed class DrawPossibleProgramsCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.CurrentFile is not { } file)
        {
            return;
        }

        IReadOnlyList<OpenAlternative> alternatives = FileManager.Opener.Alternatives(file.Path);

        FileManager.Notify(alternatives.Count == 0
            ? "nothing can open this"
            : string.Join("  ", alternatives.Select(
                a => $"{a.Number}:{a.Label ?? Summarise(a.Command)}")));
    }

    /// <summary>The first word of a command, which is usually the program's name.</summary>
    private static string Summarise(string command) =>
        command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries) is [string first, ..]
            ? first
            : command;
}

/// <summary>Opens a shell in the current directory.</summary>
[Command("terminal", Summary = "Open a terminal in the current directory.")]
public sealed class TerminalCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
        FileManager.RunProgram(shell, "f");
    }
}
