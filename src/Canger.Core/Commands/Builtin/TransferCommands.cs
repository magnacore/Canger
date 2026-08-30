// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Model;
using Canger.Core.Tasks;

namespace Canger.Core.Commands.Builtin;

/// <summary>Copies or moves whatever was marked for transfer into the current directory.</summary>
[Command("paste", Summary = "Paste the copied or cut files into the current directory.")]
public sealed class PasteCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Paste(FileManager, Line, ClashPolicy.Rename);

    /// <summary>Queues the transfer described by the clipboard and the command's flags.</summary>
    internal static void Paste(IFileManager fileManager, CommandLine line, ClashPolicy defaultPolicy)
    {
        if (fileManager.CopyBuffer.Count == 0)
        {
            fileManager.Notify("nothing to paste", isError: true);
            return;
        }

        Dictionary<string, string> named = new(StringComparer.Ordinal);
        for (int i = 1; i < line.Count; i++)
        {
            string word = line.Word(i);
            int equals = word.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                named[word[..equals]] = word[(equals + 1)..];
            }
        }

        ClashPolicy policy = IsTrue(named.GetValueOrDefault("overwrite"))
            ? ClashPolicy.Overwrite
            : defaultPolicy;

        string destination = named.TryGetValue("dest", out string? dest) && dest.Length > 0
            ? dest
            : fileManager.CurrentDirectory.Path;

        if (!fileManager.FileSystem.DirectoryExists(destination))
        {
            fileManager.Notify($"paste: not a directory: {destination}", isError: true);
            return;
        }

        CopyJob job = new(
            fileManager.FileSystem,
            [.. fileManager.CopyBuffer.Select(f => f.Path)],
            destination,
            fileManager.IsCutPending ? TransferKind.Move : TransferKind.Copy,
            policy);

        // "append" puts the transfer behind whatever is already queued; by default a paste the
        // user just asked for starts straight away.
        fileManager.Tasks.Add(job, atFront: !IsTrue(named.GetValueOrDefault("append")));

        // A move consumes the clipboard, as it does everywhere else; a copy can be repeated.
        if (fileManager.IsCutPending)
        {
            fileManager.SetCopyBuffer([], cut: false);
        }
    }

    private static bool IsTrue(string? text) =>
        text is not null &&
        (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1");
}

/// <summary>Pastes, renaming clashes so the extension stays on the end.</summary>
[Command("paste_ext",
         Summary = "Paste, renaming clashes as name_.ext rather than name.ext_.")]
public sealed class PasteKeepingExtensionCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        PasteCommand.Paste(FileManager, Line, ClashPolicy.RenameKeepingExtension);
}

/// <summary>Deletes the selection.</summary>
[Command("delete", AllowAbbreviation = false, Summary = "Delete the selection permanently.")]
public sealed class DeleteCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // `dD` opens the console on `:delete `, which invites typing a name — and the name was
        // then discarded and the *selection* deleted instead. The confirmation does list the real
        // targets, but someone who has just typed a filename reads that as agreement, and with
        // `confirm_on_delete` at its default a single selection is removed without any prompt at
        // all. Refusing is the safe reading of an instruction Canger cannot carry out. (Ranger
        // does support the argument; adding that here means shell-splitting a line inside the one
        // command that cannot be undone, which is a change to make deliberately and not in
        // passing.)
        if (Rest(1).Trim().Length > 0)
        {
            FileManager.Notify(
                $"{Line.Word(0)}: takes no arguments — it acts on the selection", isError: true);

            return;
        }

        IReadOnlyList<FsNode> selection = FileManager.Selection;

        if (selection.Count == 0)
        {
            FileManager.Notify("nothing selected", isError: true);
            return;
        }

        // Deletion cannot be undone, so it is confirmed unless the setting says otherwise.
        DeletionConfirmation.Confirm(FileManager, selection, FileManager.Settings.ConfirmOnDelete,
                                     () => Delete(selection));
    }

    /// <summary>Removes the entries, reporting what could not be removed.</summary>
    private void Delete(IReadOnlyList<FsNode> selection)
    {
        int deleted = 0;
        List<string> failures = [];
        List<string> removed = [];

        foreach (FsNode entry in selection)
        {
            try
            {
                // A link to a directory is removed as a link, not descended into.
                if (entry.IsDirectory && !entry.IsSymbolicLink)
                {
                    FileManager.FileSystem.DeleteRecursive(entry.Path);
                }
                else
                {
                    FileManager.FileSystem.Delete(entry.Path);
                }

                deleted++;

                // Both spellings: a tag is filed under the file's real path, and what was
                // deleted here is the name it was reached by. For a symbolic link those are two
                // different things, and only one of them has gone.
                removed.Add(entry.Path);
                removed.Add(entry.RealPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{entry.RelativePath}: {e.Message}");
            }
        }

        // After the fact rather than before it, so that a delete which failed leaves the tag
        // alone. Ranger untags first and loses the tag either way; a tag is something the user
        // put there by hand, and throwing it away for a file that is still on disc is a small
        // loss of their work.
        FileManager.Tags.RemoveUnder(removed);

        FileManager.ReloadCurrentDirectory();

        FileManager.Notify(
            failures.Count == 0
                ? $"deleted {deleted}"
                : $"deleted {deleted}, failed {failures.Count}: {failures[0]}",
            isError: failures.Count > 0);
    }
}

/// <summary>Moves the selection to the trash.</summary>
[Command("trash", AllowAbbreviation = false, Summary = "Move the selection to the trash.")]
public sealed class TrashCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // `dD` opens the console on `:delete `, which invites typing a name — and the name was
        // then discarded and the *selection* deleted instead. The confirmation does list the real
        // targets, but someone who has just typed a filename reads that as agreement, and with
        // `confirm_on_delete` at its default a single selection is removed without any prompt at
        // all. Refusing is the safe reading of an instruction Canger cannot carry out. (Ranger
        // does support the argument; adding that here means shell-splitting a line inside the one
        // command that cannot be undone, which is a change to make deliberately and not in
        // passing.)
        if (Rest(1).Trim().Length > 0)
        {
            FileManager.Notify(
                $"{Line.Word(0)}: takes no arguments — it acts on the selection", isError: true);

            return;
        }

        IReadOnlyList<FsNode> selection = FileManager.Selection;

        if (selection.Count == 0)
        {
            FileManager.Notify("nothing selected", isError: true);
            return;
        }

        // "like_delete" defers to confirm_on_delete, so one setting governs both by default.
        string confirm = FileManager.Settings.ConfirmOnTrash;

        if (confirm is "like_delete")
        {
            confirm = FileManager.Settings.ConfirmOnDelete;
        }

        DeletionConfirmation.Confirm(FileManager, selection, confirm, () => Trash(selection));
    }

    /// <summary>Moves the entries to the desktop trash.</summary>
    /// <remarks>
    /// Delegated to whatever the system provides rather than reimplemented, because the desktop
    /// trash has its own layout and metadata that other tools expect to find.
    /// </remarks>
    private void Trash(IReadOnlyList<FsNode> selection)
    {
        string paths = string.Join(" ", selection.Select(e => MacroExpander.ShellQuote(e.Path)));
        FileManager.RunProgram($"trash-put -- {paths}", "s");

        // Whatever actually went. `trash-put` reports its own failures and this cannot see them,
        // so the check is whether the path is still there — which is the same question a tag
        // pointing at it would be asking.
        FileManager.Tags.RemoveUnder(
            selection.Where(e => !FileManager.FileSystem.ExistsNoFollow(e.Path))
                     .SelectMany(e => new[] { e.Path, e.RealPath }));

        FileManager.ReloadCurrentDirectory();
    }
}
