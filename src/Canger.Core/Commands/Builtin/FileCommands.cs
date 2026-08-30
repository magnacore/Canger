// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Processes;

namespace Canger.Core.Commands.Builtin;

/// <summary>Records the selection to be copied.</summary>
[Command("copy", Summary = "Mark the selection to be copied: copy [mode=set|add|remove|toggle] "
                         + "[<direction>]")]
public sealed class CopyCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        ClipboardOperation.Run(FileManager, NamedArguments(), Quantifier, cut: false);
}

/// <summary>Records the selection to be moved.</summary>
[Command("cut", Summary = "Mark the selection to be moved: cut [mode=set|add|remove|toggle] "
                        + "[<direction>]")]
public sealed class CutCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        ClipboardOperation.Run(FileManager, NamedArguments(), Quantifier, cut: true);
}

/// <summary>Forgets what was going to be copied or moved.</summary>
[Command("uncut", Summary = "Forget what was marked for copying or moving.")]
public sealed class UncutCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.SetCopyBuffer([], cut: false);
        FileManager.Notify("clipboard cleared");
    }
}

/// <summary>Creates a directory.</summary>
[Command("mkdir", Summary = "Create a directory.")]
public sealed class MakeDirectoryCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string name = Rest(1).Trim();

        if (name.Length == 0)
        {
            FileManager.Notify("mkdir: what should it be called?", isError: true);
            return;
        }

        string path = Path.IsPathRooted(name)
            ? name
            : Path.Join(FileManager.CurrentDirectory.Path, name);

        if (FileManager.FileSystem.Exists(path))
        {
            FileManager.Notify($"mkdir: already exists: {name}", isError: true);
            return;
        }

        try
        {
            FileManager.FileSystem.CreateDirectory(path);
            FileManager.ReloadCurrentDirectory();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileManager.Notify($"mkdir: {e.Message}", isError: true);
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectories();
}

/// <summary>Creates an empty file.</summary>
[Command("touch", Summary = "Create an empty file, or update its timestamp.")]
public sealed class TouchCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string name = Rest(1).Trim();

        if (name.Length == 0)
        {
            FileManager.Notify("touch: what should it be called?", isError: true);
            return;
        }

        string path = Path.IsPathRooted(name)
            ? name
            : Path.Join(FileManager.CurrentDirectory.Path, name);

        try
        {
            FileManager.FileSystem.Touch(path);
            FileManager.ReloadCurrentDirectory();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileManager.Notify($"touch: {e.Message}", isError: true);
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectoryContent();
}

/// <summary>Renames the file under the cursor.</summary>
[Command("rename", Summary = "Rename the file under the cursor.")]
public sealed class RenameCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.CurrentFile is not { } file)
        {
            FileManager.Notify("rename: nothing selected", isError: true);
            return;
        }

        string name = Rest(1).Trim();

        if (name.Length == 0)
        {
            FileManager.Notify("rename: what should it be called?", isError: true);
            return;
        }

        string destination = Path.IsPathRooted(name)
            ? name
            : Path.Join(FileManager.CurrentDirectory.Path, name);

        // Renaming onto an existing file would destroy it silently, so it is refused.
        if (FileManager.FileSystem.Exists(destination))
        {
            FileManager.Notify($"rename: already exists: {name}", isError: true);
            return;
        }

        try
        {
            FileManager.FileSystem.Rename(file.Path, destination);

            // The tag follows the file, and so do the tags of everything inside a renamed
            // directory. Ranger does the same (`config/commands.py:1139`), and `bulkrename` here
            // already did — so renaming one file was the one way to leave a tag pointing at a
            // name that no longer existed.
            FileManager.Tags.MovePath(file.RealPath, destination);

            FileManager.ReloadCurrentDirectory();
            FileManager.CurrentTab.MoveCursorTo(
                FileManager.CurrentDirectory.Entries.FirstOrDefault(
                    e => string.Equals(e.Path, destination, StringComparison.Ordinal)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileManager.Notify($"rename: {e.Message}", isError: true);
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectoryContent();
}

/// <summary>
/// Opens the rename prompt pre-filled with the current name, cursor where the edit is likely.
/// </summary>
/// <remarks>
/// Three keys share this command because they differ only in where the cursor lands: <c>a</c>
/// before the extension, <c>A</c> at the end, <c>I</c> at the start of the name. Ranger writes
/// the latter two as <c>eval fm.open_console(...)</c> with a hard-coded offset of 7 — the length
/// of <c>"rename "</c> — which naming the position avoids.
/// </remarks>
[Command("rename_append",
         Summary = "Open the rename prompt: rename_append [where=extension|end|start]")]
public sealed class RenameAppendCommand : CangerCommand
{
    private const string Prefix = "rename ";

    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.CurrentFile is not { } file)
        {
            return;
        }

        // Doubled so a per cent in the name is not read as the start of a macro when the line is
        // expanded again on its way to running. This is unquoted on purpose — the name goes into
        // the console for the user to edit, not to a shell — so only the doubling applies.
        string name = file.RelativePath.Replace("%", "%%", StringComparison.Ordinal);
        string line = Prefix + name;

        string where = NamedArguments().TryGetValue("where", out string? value)
            ? value
            : "extension";

        int cursor = where switch
        {
            "end" => line.Length,
            "start" => Prefix.Length,

            // Before the extension is where a rename usually needs to happen: changing the name
            // while keeping the type. The separator is found in the escaped name rather than
            // measured from the end, so a per cent anywhere in it cannot shift the cursor.
            _ => file.Extension.Length > 0 && name.LastIndexOf('.') > 0
                ? Prefix.Length + name.LastIndexOf('.')
                : line.Length,
        };

        FileManager.OpenConsole(line, cursor);
    }
}

/// <summary>Opens the file under the cursor in an editor.</summary>
[Command("edit", Summary = "Open the selection in an editor.")]
public sealed class EditCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string target = Rest(1).Trim();

        if (target.Length == 0)
        {
            if (FileManager.CurrentFile is not { } file)
            {
                FileManager.Notify("edit: nothing selected", isError: true);
                return;
            }

            target = file.RelativePath;
        }

        string editor = Environment.GetEnvironmentVariable("VISUAL")
                        ?? Environment.GetEnvironmentVariable("EDITOR")
                        ?? "vi";

        // `--` because quoting stops the shell, not the program. Without it a file called
        // `+!rm -rf ~/Documents` is read by vim as a command to run at startup, and pressing `E`
        // on it is enough. Canger's own shipped rifle.conf has the `--` on its editor rules;
        // this command bypasses rifle and had dropped it, where ranger routes `:edit` through
        // rifle precisely so as not to.
        FileManager.RunProgram($"{editor} -- {MacroExpander.ShellQuote(target)}");
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectoryContent();
}

/// <summary>Copies something about the selection to the clipboard.</summary>
/// <remarks>
/// <para>
/// <c>:yank [name|dir|path|name_without_extension]</c>, bound by default to <c>yn</c>, <c>yd</c>,
/// <c>yp</c> and <c>y.</c>. One line per selected entry, so yanking a marked set gives a list
/// that can be pasted straight into a shell or an editor.
/// </para>
/// <para>
/// This reported the value on the status line and stopped there for a long while, with a comment
/// saying the clipboard would follow "when the process runner lands". The runner landed; the
/// clipboard did not. Nothing was ever copied.
/// </para>
/// </remarks>
[Command("yank", Summary = "Copy the selection's name, path or directory to the clipboard.")]
public sealed class YankCommand : CangerCommand
{
    /// <summary>What each mode name yanks.</summary>
    /// <remarks>
    /// The names, and the empty default, are ranger's (<c>config/commands.py:2049-2055</c>), so
    /// a binding written for ranger means the same thing here.
    /// </remarks>
    private static readonly string[] Modes = ["dir", "name", "name_without_extension", "path"];

    /// <inheritdoc />
    public override void Execute()
    {
        // Each entry answers for itself rather than the mode being read off the current
        // directory: under `:flat` a listing spans several directories, so "the directory" is
        // not one answer, and ranger asks each file too.
        IEnumerable<string> values = Argument(1) switch
        {
            "path" => FileManager.Selection.Select(e => e.Path),
            "dir" => FileManager.Selection.Select(
                e => Path.GetDirectoryName(e.Path) ?? e.Path),
            "name_without_extension" => FileManager.Selection.Select(
                e => e.BasenameWithoutExtension),

            // Ranger's default and its `name` are both the basename. Not the display path: under
            // `:flat` that carries directories the user did not ask to copy.
            _ => FileManager.Selection.Select(e => e.Basename),
        };

        string text = string.Join("\n", values);

        if (text.Length == 0)
        {
            return;
        }

        // Ranger fails silently when no helper is installed, which is indistinguishable from the
        // yank having worked. Saying so is the difference between a missing package and a bug.
        if (FileManager.Clipboard.Copy(text) is not { } helper)
        {
            FileManager.Notify(
                "yank: no clipboard program found (tried "
                + string.Join(", ", SystemClipboard.KnownHelpers) + ")",
                isError: true);

            return;
        }

        // Confirming what went to the clipboard, on one line, because a yank is otherwise
        // completely invisible until something is pasted somewhere else.
        string summary = text.Replace('\n', ' ');

        FileManager.Notify($"yanked to clipboard ({helper}): {summary}");
    }

    /// <inheritdoc />
    /// <remarks>Offers the mode names, as ranger's <c>tab</c> does.</remarks>
    public override IReadOnlyList<string> Complete(int direction)
    {
        string typed = Rest(1);
        string prefix = Line.Word(0) + " ";

        return
        [
            .. Modes.Where(m => m.StartsWith(typed, StringComparison.Ordinal))
                    .Select(m => prefix + m),
        ];
    }
}
