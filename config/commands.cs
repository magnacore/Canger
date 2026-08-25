// Canger user commands.
//
// This is Canger's equivalent of ranger's commands.py. It is compiled with Roslyn at startup, so
// it is real C# rather than a configuration format — but it is still a drop-in file: edit it and
// restart, no build step.
//
// Any class deriving from CangerCommand is registered automatically under its [Command] name and
// becomes available at the ':' prompt and in cc.conf key bindings. Canger's own commands live
// inside Canger; this file only adds to them. Registering a name Canger already uses replaces it.
//
// These namespaces are already in scope and need no using directive:
//   System, System.Collections.Generic, System.Linq,
//   Canger.Core.Commands, Canger.Core.Model, Canger.Plugins
//
// Porting a ranger commands.py? tools/port-ranger-commands.py translates every command whose body
// is a single shell call, which is most of them.

namespace Canger.UserCommands;

/// <summary>
/// Runs a program on the selection, as most custom commands do.
/// </summary>
/// <remarks>
/// <para>
/// The whole job is building a command line and handing it to <c>shell</c>, which is why a wrapper
/// this thin does not need C# at all — this line in cc.conf does the same thing:
/// </para>
/// <para><c>alias count_lines shell -p wc -l %s</c></para>
/// <para>
/// Reach for C# when a command needs a default value in the middle of a line, has to look at the
/// selection before deciding, or does more than one thing.
/// </para>
/// </remarks>
[Command("count_lines", Summary = "Count the lines in the selection.")]
public sealed class CountLines : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.Selection.Count == 0)
        {
            FileManager.Notify("count_lines: nothing selected", isError: true);
            return;
        }

        // %s expands to every selected file, each quoted. -p sends the output to the pager
        // rather than letting it scroll past.
        FileManager.Execute("shell -p wc -l %s");
    }
}

/// <summary>
/// Opens the selection in an editor, taking an optional editor name.
/// </summary>
/// <remarks>
/// Shows the two things an alias cannot do: an argument with a default, and completion. Try it
/// with <c>:edit_with nano</c>, or bind it — <c>map EW console edit_with%space</c>.
/// </remarks>
[Command("edit_with", Summary = "Open the selection in an editor: edit_with [<program>]")]
public sealed class EditWith : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // Rest(1) is everything after the command name, so an editor with arguments works too.
        string editor = Rest(1).Trim() is { Length: > 0 } given
            ? given
            : Environment.GetEnvironmentVariable("VISUAL")
              ?? Environment.GetEnvironmentVariable("EDITOR")
              ?? "vi";

        if (FileManager.CurrentFile is null)
        {
            FileManager.Notify("edit_with: nothing selected", isError: true);
            return;
        }

        FileManager.Execute($"shell {editor} %s");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called when Tab is pressed at the prompt. Returning the whole line, not just the word,
    /// is what lets a completion replace part of what was typed.
    /// </remarks>
    public override IReadOnlyList<string> Complete(int direction) =>
    [
        .. new[] { "vi", "vim", "nvim", "nano", "emacs", "helix" }
            .Where(name => name.StartsWith(Rest(1), StringComparison.Ordinal))
            .Select(name => $"{Argument(0)} {name}")
    ];
}
