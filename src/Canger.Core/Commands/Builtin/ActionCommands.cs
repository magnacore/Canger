// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.FileOperations;
using Canger.Core.Model;
using Canger.Core.Settings;
using Canger.Core.State;

namespace Canger.Core.Commands.Builtin;

/// <summary>Leaves Canger.</summary>
/// <remarks>
/// Ranger reaches this by raising <c>SystemExit</c> (<c>core/actions.py:57</c>), so it closes
/// every tab without asking. <c>:quit</c> closes one tab; this is the other thing.
/// </remarks>
[Command("exit", Summary = "Exit Canger, whatever is open.")]
public sealed class ExitCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.Quit();
}

/// <summary>Shows the manual, the bindings, the commands or the settings.</summary>
/// <remarks>
/// Ranger asks which of the four is wanted rather than guessing
/// (<c>config/commands.py:1364-1387</c>), because <c>?</c> is pressed by someone who does not yet
/// know what they are looking for.
/// </remarks>
[Command("help", Summary = "Show the manual, key bindings, commands or settings.")]
public sealed class HelpCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        FileManager.Ask(
            "View [m]an page, [k]ey bindings, [c]ommands or [s]ettings? (press q to abort)",
            Answer,
            ['m', 'q', 'k', 'c', 's']);

    private void Answer(char answer)
    {
        switch (answer)
        {
            case 'm':
                ShowManual();
                break;

            case 'k':
                FileManager.ShowInExternalPager(DescribeBindings());
                break;

            case 'c':
                FileManager.ShowInExternalPager(DescribeCommands());
                break;

            case 's':
                FileManager.ShowInExternalPager(DescribeSettings());
                break;

            default:
                // 'q', or anything else: the question is abandoned.
                break;
        }
    }

    /// <summary>
    /// Shows the manual, formatted and paged by <c>man</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was <c>man canger</c>, which asks the system for an <em>installed</em> page. Canger is
    /// normally run from wherever it was unpacked or built, nothing installs a page there, and
    /// <c>man</c> answers with exit 16 — its code for "no such page". So the one key whose whole
    /// job is to explain the program failed for anybody who had not packaged it.
    /// </para>
    /// <para>
    /// Canger writes its own manual instead, with <c>--man</c>, and hands that to <c>man</c> to
    /// format. The page is generated from the same binary that is running, so it describes the
    /// bindings and settings actually in force rather than whichever version was installed last —
    /// and it needs no installation at all.
    /// </para>
    /// <para>
    /// Through a file rather than a pipe: <c>man -l -</c> reads standard input on man-db but not
    /// everywhere, while <c>man -l FILE</c> is understood by every implementation. The file is
    /// named <c>canger.1</c> so that the header reads <c>CANGER(1)</c> rather than a temporary
    /// name, and the directory holding it goes as soon as the pager exits.
    /// </para>
    /// </remarks>
    private void ShowManual() =>
        // The running binary, whatever it is called and wherever it was unpacked. Falling back to
        // the name only if the runtime cannot say, in which case the PATH is the best guess left.
        FileManager.RunProgram(ManualCommand(Environment.ProcessPath ?? "canger"));

    /// <summary>Builds the line that renders and pages the manual.</summary>
    /// <param name="executable">The Canger binary to ask for the page.</param>
    /// <returns>A shell command.</returns>
    internal static string ManualCommand(string executable)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);

        string self = MacroExpander.ShellQuote(executable);

        return $"d=$(mktemp -d) && {self} --man > \"$d/canger.1\" && man -l \"$d/canger.1\"; "
               + "rm -rf \"$d\"";
    }

    private string DescribeBindings()
    {
        List<string> lines = [];

        foreach ((string context, Input.KeyMap map) in new[]
                 {
                     ("browser", FileManager.KeyMaps.Browser),
                     ("console", FileManager.KeyMaps.Console),
                     ("pager", FileManager.KeyMaps.Pager),
                     ("taskview", FileManager.KeyMaps.TaskView),
                 })
        {
            lines.Add($"--- {context} ---");

            lines.AddRange(
                map.Enumerate()
                   .Select(b => (Keys: string.Concat(b.Keys.Select(Input.KeyCodes.ToDisplayString)),
                                 b.Command))
                   .OrderBy(b => b.Keys, StringComparer.Ordinal)
                   .Select(b => $"  {b.Keys,-16} {b.Command}"));

            lines.Add(string.Empty);
        }

        return string.Join("\n", lines);
    }

    private string DescribeCommands() =>
        string.Join("\n",
                    FileManager.Commands.Names
                        .Select(n => (Name: n, FileManager.Commands.Find(n)?.Summary))
                        .Select(c => $"{c.Name,-28} {c.Summary}"));

    private string DescribeSettings() =>
        string.Join("\n",
                    SettingsCatalog.Names
                        .Select(n => $"{n,-34} {FileManager.Settings.Raw.Get<object?>(n)}"));
}

/// <summary>Shows every message this session, most recent last.</summary>
[Command("display_log", Summary = "Show the message log.")]
public sealed class DisplayLogCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        IReadOnlyList<string> log = FileManager.MessageLog;

        FileManager.ShowInPager(log.Count > 0
            ? "Message Log:\n" + string.Join("\n", log)
            : "Message Log:\nNo messages!");
    }
}

/// <summary>Works out how much a directory actually holds, and shows it.</summary>
/// <remarks>
/// Not done automatically because it means walking the whole tree; <c>dc</c> is how a user asks
/// for the answer on one directory when they want it.
/// </remarks>
[Command("get_cumulative_size", Summary = "Measure the selection, including everything under it.")]
public sealed class GetCumulativeSizeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        IReadOnlyList<FsNode> selection = FileManager.Selection;

        if (selection.Count == 0)
        {
            return;
        }

        foreach (FsNode node in selection)
        {
            node.CumulativeSize = node.IsDirectory
                ? DirectorySize.Measure(FileManager.FileSystem, node.Path)
                : node.Size ?? 0;

            // The figure has just been taken, so the `?` that says it might be out of date has
            // to go with it. Without this the marker was permanent: a listing re-read after a
            // measurement set the flag and nothing ever cleared it, so `dc` on a directory that
            // had changed reported the new size still wearing the doubt about the old one.
            // Ranger clears it by rewriting the infostring with a plain separator
            // (`container/directory.py:582-585`).
            node.CumulativeSizeStale = false;
        }

        // Nothing is announced. Each measured row shows its own size in the size column, and the
        // total across a multiple selection appears on the right of the status bar beside the
        // count — which is where ranger puts it (`gui/widgets/statusbar.py:284-291`), and where
        // this used to differ: a message on the left said the total once and then went away.
        FileManager.Redraw();
    }
}

/// <summary>Jumps between directories and files.</summary>
/// <remarks>
/// From a directory it goes to the next file, and from a file to the next directory
/// (<c>config/commands.py:864-903</c>). <c>-r</c> searches upwards, <c>-w</c> wraps around.
/// </remarks>
[Command("jump_non", Summary = "Jump to the next entry of the other kind: jump_non [-rw]")]
public sealed class JumpNonCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        (string flags, _) = Line.ParseFlags();

        if (FileManager.CurrentTab.Selected is not { } current)
        {
            return;
        }

        bool reverse = flags.Contains('r', StringComparison.Ordinal);
        bool wrap = flags.Contains('w', StringComparison.Ordinal);
        bool wantDirectory = !current.IsDirectory;

        IReadOnlyList<FsNode> entries = FileManager.CurrentDirectory.Entries;
        int cursor = FileManager.CurrentDirectory.Cursor.Index;

        // Walked in the direction asked for, remembering the first match on the far side so the
        // wrapping case needs no second pass.
        int step = reverse ? -1 : 1;
        FsNode? after = null;
        FsNode? before = null;

        for (int offset = 1; offset < entries.Count; offset++)
        {
            int index = cursor + (offset * step);
            bool wrapped = index < 0 || index >= entries.Count;
            index = ((index % entries.Count) + entries.Count) % entries.Count;

            if (entries[index].IsDirectory != wantDirectory)
            {
                continue;
            }

            if (!wrapped)
            {
                after = entries[index];
                break;
            }

            before ??= entries[index];
        }

        FsNode? target = after ?? (wrap ? before : null);

        if (target is not null)
        {
            FileManager.CurrentTab.MoveCursorTo(target);
        }
    }
}

/// <summary>Scrolls the preview without moving the cursor.</summary>
[Command("scroll_preview", Summary = "Scroll the preview: scroll_preview <lines>")]
public sealed class ScrollPreviewCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // A quantifier wins over the argument, as ranger's does, so `5<C-e>` scrolls five lines
        // regardless of what the binding asked for.
        int lines = Quantifier
                    ?? (int.TryParse(Argument(1), CultureInfo.InvariantCulture, out int parsed)
                        ? parsed
                        : 1);

        FileManager.ScrollPreview(lines);
        FileManager.Redraw();
    }
}

/// <summary>Switches between normal and visual mode.</summary>
[Command("change_mode", Summary = "Change mode: change_mode <normal|visual>")]
public sealed class ChangeModeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string mode = Argument(1);

        if (mode.Length == 0)
        {
            FileManager.Notify("Syntax: change_mode <mode>", isError: true);
            return;
        }

        FileManager.ChangeMode(mode);
    }
}

/// <summary>Turns visual mode on, or off if it is already on.</summary>
/// <remarks>
/// <c>reverse=True</c> makes moving unmark rather than mark, which is what <c>uV</c> is for. A
/// quantifier marks that many entries outright instead of starting a selection.
/// </remarks>
[Command("toggle_visual_mode", Summary = "Toggle visual (selection) mode.")]
public sealed class ToggleVisualModeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.IsVisualMode)
        {
            FileManager.ChangeMode("normal");
            return;
        }

        bool reverse = NamedArguments().TryGetValue("reverse", out string? raw) && IsTrue(raw);

        if (Quantifier is { } count)
        {
            MarkRun(count, !reverse);
        }

        FileManager.ChangeMode(reverse ? "visual-reverse" : "visual");
    }

    /// <summary>Marks a run of entries starting at the cursor.</summary>
    private void MarkRun(int count, bool marked)
    {
        IReadOnlyList<FsNode> entries = FileManager.CurrentDirectory.Entries;
        int start = FileManager.CurrentDirectory.Cursor.Index;

        for (int i = start; i < Math.Min(start + count, entries.Count); i++)
        {
            entries[i].IsMarked = marked;
        }
    }

    /// <summary>Reads ranger's spelling of a boolean argument.</summary>
    private static bool IsTrue(string? value) =>
        value is null or "" ||
        value is "True" or "true" or "1" or "yes" or "on";
}

/// <summary>Moves to the next entry matching the current search.</summary>
/// <remarks>
/// The order can be the last text searched for, a tag, or one of the sortable attributes — which
/// is what makes <c>cm</c> then <c>n</c> walk a directory newest-first without re-sorting it
/// (<c>core/actions.py:803-856</c>).
/// </remarks>
[Command("search_next", Summary = "Repeat the search: search_next [order=<order>] [forward=<bool>]")]
public sealed class SearchNextCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Dictionary<string, string> named = NamedArguments();
        bool forward = !named.TryGetValue("forward", out string? raw) ||
                       raw is "True" or "true" or "1";

        if (named.TryGetValue("order", out string? order) && order.Length > 0)
        {
            FileManager.SearchMethod = order;
        }
        else
        {
            order = FileManager.SearchMethod;
        }

        DirectoryNode directory = FileManager.CurrentDirectory;

        if (Step(directory, order, forward, Repeat) is { } found)
        {
            FileManager.CurrentTab.MoveCursorTo(found);
            return;
        }

        FileManager.Notify($"search_next: nothing further by {order}");
    }

    /// <summary>Finds the entry to move to, or null when there is none.</summary>
    private FsNode? Step(DirectoryNode directory, string order, bool forward, int times)
    {
        // 'search' and 'tag' step through the listing as it stands, testing each entry. The
        // attribute orders instead walk a separately sorted sequence, so `n` can visit files by
        // size while the listing stays in the user's chosen order.
        Func<FsNode, bool>? matches = order switch
        {
            "search" when FileManager.CurrentTab.LastSearch is { Length: > 0 } text =>
                node => node.Basename.Contains(text, StringComparison.OrdinalIgnoreCase),
            "tag" => node => FileManager.Tags.Contains(node.RealPath),
            _ => null,
        };

        if (matches is not null)
        {
            return Cycle(directory.Entries, directory.Cursor.Index, forward, times, matches);
        }

        IReadOnlyList<FsNode>? ordered = Ordered(directory.Entries, order);

        if (ordered is null)
        {
            return null;
        }

        int position = ordered.ToList().FindIndex(
            e => string.Equals(e.Path, FileManager.CurrentTab.Selected?.Path,
                               StringComparison.Ordinal));

        if (position < 0 || ordered.Count == 0)
        {
            return ordered.Count > 0 ? ordered[0] : null;
        }

        int next = position + (forward ? times : -times);

        return ordered[((next % ordered.Count) + ordered.Count) % ordered.Count];
    }

    /// <summary>The entries sorted by an attribute, or null when the order is not one.</summary>
    private static IReadOnlyList<FsNode>? Ordered(IReadOnlyList<FsNode> entries, string order) =>
        order switch
        {
            // Descending, matching ranger's negated keys: the interesting end of every one of
            // these is the large or the recent one.
            "size" => [.. entries.OrderByDescending(e => e.Size ?? 0)],
            "mimetype" => [.. entries.OrderBy(e => e.Extension, StringComparer.Ordinal)],
            "ctime" => [.. entries.OrderByDescending(e => e.Status?.ChangeTime)],
            "mtime" => [.. entries.OrderByDescending(e => e.Status?.ModifyTime)],
            "atime" => [.. entries.OrderByDescending(e => e.Status?.AccessTime)],
            _ => null,
        };

    /// <summary>Steps through a listing, wrapping, until a match is found.</summary>
    private static FsNode? Cycle(IReadOnlyList<FsNode> entries, int cursor, bool forward,
                                int times, Func<FsNode, bool> matches)
    {
        FsNode? found = null;
        int at = cursor;

        for (int repeat = 0; repeat < times; repeat++)
        {
            found = null;

            for (int offset = 1; offset < entries.Count; offset++)
            {
                int index = at + (forward ? offset : -offset);
                index = ((index % entries.Count) + entries.Count) % entries.Count;

                if (matches(entries[index]))
                {
                    found = entries[index];
                    at = index;
                    break;
                }
            }

            if (found is null)
            {
                return null;
            }
        }

        return found;
    }
}

/// <summary>Walks the whole tree, one entry at a time.</summary>
/// <remarks>
/// Enters a directory, or moves down; at the end of a listing it climbs out and continues in the
/// parent (<c>core/actions.py:616-634</c>). Repeatedly pressing it visits every file beneath
/// where it started.
/// </remarks>
[Command("traverse", Summary = "Move to the next entry, entering and leaving directories.")]
public sealed class TraverseCommand : CangerCommand
{
    /// <summary>
    /// How many climbs out are allowed before giving up.
    /// </summary>
    /// <remarks>
    /// Ranger recurses instead, and reaching the root is its base case. A bound is cheaper to
    /// reason about than trusting that the filesystem is a tree, which a symbolic link is
    /// enough to make untrue.
    /// </remarks>
    private const int MaximumClimbs = 256;

    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.ChangeMode("normal");

        for (int guard = 0; guard < MaximumClimbs; guard++)
        {
            DirectoryNode directory = FileManager.CurrentDirectory;

            if (FileManager.CurrentTab.Selected is { IsDirectory: true } selected)
            {
                FileManager.CurrentTab.Enter(selected.Path);
                return;
            }

            if (directory.Cursor.Index < directory.Count - 1)
            {
                FileManager.CurrentTab.MoveCursor(directory.Cursor.Index + 1);
                return;
            }

            // At the end of this listing: climb out and try again from the parent.
            if (directory.Path == "/" || !FileManager.CurrentTab.GoUp())
            {
                return;
            }
        }
    }
}

/// <summary>Walks the tree backwards, the reverse of <c>:traverse</c>.</summary>
[Command("traverse_backwards", Summary = "Move to the previous entry, entering directories.")]
public sealed class TraverseBackwardsCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.ChangeMode("normal");

        if (FileManager.CurrentDirectory.Cursor.Index == 0)
        {
            FileManager.CurrentTab.GoUp();
            return;
        }

        FileManager.CurrentTab.MoveCursor(FileManager.CurrentDirectory.Cursor.Index - 1);

        // Having stepped back onto a directory, descend into it and go to its last entry, which
        // is what "the previous thing" means when walking a tree.
        for (int guard = 0; guard < 256; guard++)
        {
            if (FileManager.CurrentTab.Selected is not { IsDirectory: true } selected ||
                !FileManager.CurrentTab.Enter(selected.Path))
            {
                return;
            }

            DirectoryNode entered = FileManager.CurrentDirectory;

            if (entered.Count == 0)
            {
                return;
            }

            FileManager.CurrentTab.MoveCursor(entered.Count - 1);
        }
    }
}

/// <summary>Sets the permissions of the selection.</summary>
/// <remarks>
/// The argument is octal, as <c>chmod(1)</c> takes it, and a quantifier will do instead — which
/// is what makes <c>644=</c> work (<c>config/commands.py:1190-1227</c>).
/// </remarks>
[Command("chmod", Summary = "Set permissions: chmod <octal>")]
public sealed class ChmodCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string text = Rest(1).Trim();

        if (text.Length == 0)
        {
            if (Quantifier is not { } quantifier)
            {
                FileManager.Notify("Syntax: chmod <octal number> or specify a quantifier",
                                   isError: true);
                return;
            }

            // The quantifier is typed in decimal but means octal digits: `644=` is 0644, so the
            // digits are read back out as written rather than converted.
            text = quantifier.ToString(CultureInfo.InvariantCulture);
        }

        if (!TryParseOctal(text, out UnixFileMode mode))
        {
            FileManager.Notify("Need an octal number between 0 and 777!", isError: true);
            return;
        }

        int changed = 0;

        foreach (FsNode node in FileManager.Selection)
        {
            try
            {
                File.SetUnixFileMode(node.Path, mode);
                changed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                           or PlatformNotSupportedException)
            {
                FileManager.Notify($"chmod {node.Basename}: {e.Message}", isError: true);
            }
        }

        if (changed > 0)
        {
            FileManager.Notify($"chmod {text}: {changed} item(s)");
        }

        FileManager.ReloadCurrentDirectory();
    }

    /// <summary>Reads an octal permission number.</summary>
    private static bool TryParseOctal(string text, out UnixFileMode mode)
    {
        mode = default;

        if (text.Length is 0 or > 3 || !text.All(c => c is >= '0' and <= '7'))
        {
            return false;
        }

        int bits = Convert.ToInt32(text, 8);
        mode = (UnixFileMode)bits;

        return true;
    }
}

/// <summary>Renames the selection by editing a list of names in an editor.</summary>
/// <remarks>
/// <para>
/// The names are written to a file, the editor is opened on it, and the differences become the
/// renames. Ranger then shows the generated shell script for review before running it
/// (<c>config/commands.py:1230-1324</c>); Canger performs the renames itself, which is the same
/// two-step review — edit, then confirm — without handing a generated script to a shell.
/// </para>
/// <para>
/// Tags follow the files, because the whole point of a tag is that it survives the file being
/// moved.
/// </para>
/// </remarks>
[Command("bulkrename", Summary = "Rename the selection by editing a list in an editor.")]
public sealed class BulkRenameCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        IReadOnlyList<FsNode> selection = FileManager.Selection;

        if (selection.Count == 0)
        {
            FileManager.Notify("bulkrename: nothing selected", isError: true);
            return;
        }

        string[] before = [.. selection.Select(e => e.RelativePath)];
        string listFile = Path.Join(Path.GetTempPath(), "canger-bulkrename-"
                                                       + Path.GetRandomFileName());

        try
        {
            File.WriteAllLines(listFile, before);

            string editor = Environment.GetEnvironmentVariable("VISUAL")
                            ?? Environment.GetEnvironmentVariable("EDITOR")
                            ?? "vi";

            // `--` for the same reason as `:edit`, though the path here is one Canger generated
            // and so is not attacker-controlled. Consistency is worth more than the argument
            // that this particular call is safe.
            FileManager.RunProgram($"{editor} -- {MacroExpander.ShellQuote(listFile)}");

            string[] after = File.ReadAllLines(listFile);

            Apply(before, after);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileManager.Notify($"bulkrename: {e.Message}", isError: true);
        }
        finally
        {
            try
            {
                File.Delete(listFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover file in the temporary directory is not worth reporting.
            }
        }
    }

    /// <summary>Performs the renames the edit describes.</summary>
    private void Apply(string[] before, string[] after)
    {
        if (after.Length != before.Length)
        {
            FileManager.Notify(
                $"bulkrename: {before.Length} names went out and {after.Length} came back; "
                + "nothing renamed", isError: true);

            return;
        }

        List<(string From, string To)> renames =
        [
            .. before.Zip(after)
                     .Where(p => !string.Equals(p.First, p.Second, StringComparison.Ordinal)
                                 && p.Second.Trim().Length > 0)
                     .Select(p => (p.First, p.Second)),
        ];

        if (renames.Count == 0)
        {
            FileManager.Notify("No renaming to be done!");
            return;
        }

        string root = FileManager.CurrentDirectory.Path;
        int done = 0;

        foreach ((string from, string to) in renames)
        {
            string source = Path.Join(root, from);
            string destination = Path.Join(root, to);

            try
            {
                // A rename may introduce a directory, exactly as ranger's generated script does
                // with `mkdir -vp`.
                if (Path.GetDirectoryName(destination) is { Length: > 0 } parent)
                {
                    Directory.CreateDirectory(parent);
                }

                if (Directory.Exists(source))
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    File.Move(source, destination, overwrite: false);
                }

                FileManager.Tags.MovePath(source, destination);
                done++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                FileManager.Notify($"bulkrename {from}: {e.Message}", isError: true);
            }
        }

        FileManager.Notify($"bulkrename: {done} of {renames.Count} renamed");
        FileManager.ReloadCurrentDirectory();
    }
}

/// <summary>Puts symbolic links to the copied files here.</summary>
/// <remarks><c>relative=True</c> writes a relative target, which survives the tree moving.</remarks>
[Command("paste_symlink", Summary = "Symlink the copy buffer here: paste_symlink [relative=<bool>]")]
public sealed class PasteSymlinkCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        bool relative = NamedArguments().TryGetValue("relative", out string? raw)
                        && raw is "True" or "true" or "1";

        LinkPaster.Paste(FileManager, (source, target) =>
        {
            string linkTarget = relative
                ? Path.GetRelativePath(Path.GetDirectoryName(target) ?? ".", source)
                : source;

            if (Directory.Exists(source))
            {
                Directory.CreateSymbolicLink(target, linkTarget);
            }
            else
            {
                File.CreateSymbolicLink(target, linkTarget);
            }
        }, "symlink");
    }
}

/// <summary>Puts hard links to the copied files here.</summary>
[Command("paste_hardlink", Summary = "Hard-link the copy buffer here.")]
public sealed class PasteHardlinkCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        LinkPaster.Paste(FileManager,
                         (source, target) => FileManager.FileSystem.CreateHardLink(target, source),
                         "hardlink");
}

/// <summary>
/// Recreates the copied directories here, hard-linking every file inside them.
/// </summary>
/// <remarks>
/// The directories are real and the files are shared, so the copy costs nothing but can be
/// rearranged independently.
/// </remarks>
[Command("paste_hardlinked_subtree", Summary = "Recreate the copy buffer here, hard-linking files.")]
public sealed class PasteHardlinkedSubtreeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        LinkPaster.Paste(FileManager,
                         (source, target) => Recurse(FileManager.FileSystem, source, target),
                         "hardlinked subtree", makeUnique: false);

    private static void Recurse(FileSystem.IFileSystem fileSystem, string source, string target)
    {
        if (!Directory.Exists(source))
        {
            if (!File.Exists(target))
            {
                fileSystem.CreateHardLink(target, source);
            }

            return;
        }

        Directory.CreateDirectory(target);

        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            Recurse(fileSystem, entry, Path.Join(target, Path.GetFileName(entry)));
        }
    }
}

/// <summary>Shared plumbing for the three linking pastes.</summary>
internal static class LinkPaster
{
    /// <summary>Links every file in the copy buffer into the current directory.</summary>
    /// <param name="fileManager">Where the buffer and the destination come from.</param>
    /// <param name="link">Creates one link.</param>
    /// <param name="what">What to call this in a message.</param>
    /// <param name="makeUnique">
    /// Whether a colliding name is given a suffix. False for the subtree paste, which merges
    /// into an existing directory rather than making a second one beside it.
    /// </param>
    internal static void Paste(IFileManager fileManager, Action<string, string> link, string what,
                               bool makeUnique = true)
    {
        IReadOnlyList<string> buffer = fileManager.CopyBuffer;

        if (buffer.Count == 0)
        {
            fileManager.Notify($"paste_{what}: nothing copied", isError: true);
            return;
        }

        string destination = fileManager.CurrentDirectory.Path;
        int done = 0;

        foreach (string source in buffer)
        {
            string basename = Path.GetFileName(source);

            // The same refusal `CopyJob` makes, which these had none of. Linking a directory
            // into itself walks the tree it is creating: `Recurse` lists the source while adding
            // directories underneath it, and descends until the path length or the disk runs
            // out. Nothing existing is destroyed, but a filled disk is its own kind of loss.
            if (PathRelation.IsSameOrInside(fileManager.FileSystem, destination, source))
            {
                fileManager.Notify($"paste {what} {basename}: cannot be pasted into itself",
                                   isError: true);
                continue;
            }

            string target = Path.Join(destination, basename);

            if (makeUnique)
            {
                target = SafePath.MakeUnique(fileManager.FileSystem, target);
            }

            try
            {
                link(source, target);
                done++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                fileManager.Notify($"paste {what} {basename}: {e.Message}", isError: true);
            }
        }

        fileManager.Notify($"{done} {what}(s) created");
        fileManager.ReloadCurrentDirectory();
    }
}
