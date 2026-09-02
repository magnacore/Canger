// SPDX-License-Identifier: GPL-3.0-or-later
// The GPG key identifiers below are placeholders. This file is a fixture: it exists so the tests
// compile a real ported configuration and prove the plugin API still carries it. Whose keys they
// are is beside that point, and the originals belonged to a person rather than to the project.
// Substitute your own before using these commands.
// Manuj's ranger commands, ported to C#.
//
// 48 commands: 29 generated from ranger-settings/commands.py by
// tools/port-ranger-commands.py, and 19 written by hand below because they do more than run one
// shell command — the choosers, the tab and selection work, anything whose arguments are computed
// rather than typed, and anything that decides *which* files the tool runs on.
//
// Canger compiles this file at startup, so editing it and restarting is all that is needed: the
// same loop as ranger's commands.py, in the language the rest of Canger is written in.
//
// Regenerating (after changing commands.py) rewrites only the generated half:
//
//   python3 tools/port-ranger-commands.py ranger-settings/commands.py \
//           Canger/artifacts/commands.cs.generated
//   cat Canger/artifacts/commands.cs.generated Canger/artifacts/commands.hand.cs \
//       > ~/.config/canger/commands.cs
//
// Three of the ranger commands are deliberately absent: mark_tag, unmark_tag and paste_ext are
// built into Canger already, so a copy here would only shadow the real thing.



/// <summary>Quotes one argument for the shell these commands run through.</summary>
internal static class ShellWord
{
    /// <summary>Wraps a word so it survives both the shell and a second macro expansion.</summary>
    /// <remarks>
    /// Every use here builds a line and hands it to <c>FileManager.Execute</c>, which expands
    /// macros over the whole line — including the part just quoted. Quoting alone is therefore
    /// not enough: a per cent in the name is read as the start of a macro, and the substitution
    /// brings its own quotes, which close the quoting around the name and leave the rest bare. A
    /// file called <c>My%20Docs</c> was enough to break these commands; one called
    /// <c>x%sy.txt</c> beside one called <c>;id;.txt</c> was enough to make them run something.
    ///
    /// Canger provides the correct pair, so this is now its name rather than its own attempt:
    /// <c>ShellQuote</c> for a command going straight to a runner, this one for a line going to
    /// <c>Execute</c>.
    /// </remarks>
    internal static string Quote(string value) =>
        Canger.Core.Commands.MacroExpander.QuoteForCommandLine(value);
}

/// <summary>Resize images.</summary>
[Command("image_convert", Summary = "Resize images. Usage: image_convert [<dimension>]")]
public sealed class ImageConvertCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string dimension = Rest(1).Trim() is { Length: > 0 } given
            ? given
            : "1080";

        FileManager.Execute($"shell image-convert-tui {dimension} %s");
    }
}

/// <summary>Creates detached signatures for a file using gpg If only one file is highlighted, it will be treated as a single selection.</summary>
[Command("gpg_detached_sign", Summary = "Creates detached signatures for a file using gpg If only one file is highlighted, it will be treated as a single selection.")]
public sealed class GpgDetachedSignCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        foreach (FsNode entry in FileManager.Selection)
        {
            FileManager.Execute($"shell -f gpg --detach-sign -u DEADBEEFDEADBEEF {ShellWord.Quote(entry.Basename)} ");
        }

        FileManager.ChangeMode("normal");
    }
}

/// <summary>Encrypts a file using gpg If only one file is highlighted, it will be treated as a single selection.</summary>
[Command("gpg_encrypt_file", Summary = "Encrypts a file using gpg If only one file is highlighted, it will be treated as a single selection.")]
public sealed class GpgEncryptFileCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        foreach (FsNode entry in FileManager.Selection)
        {
            FileManager.Execute($"shell -f gpg -e -u CAFEBABECAFEBABE -r CAFEBABECAFEBABE {ShellWord.Quote(entry.Basename)} ");
        }

        FileManager.ChangeMode("normal");
    }
}

/// <summary>tag files.</summary>
[Command("files_tag", Summary = "tag files. Usage: files_tag [<tag>]")]
public sealed class FilesTagCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string tag = Rest(1).Trim() is { Length: > 0 } given
            ? given
            : "noTag";

        FileManager.Execute($"shell file-tag {tag} %s");
    }
}

/// <summary>remove file tags.</summary>
[Command("files_tag_remove", Summary = "remove file tags. Usage: files_tag_remove [<tag>]")]
public sealed class FilesTagRemoveCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string tag = Rest(1).Trim() is { Length: > 0 } given
            ? given
            : "noTag";

        FileManager.Execute($"shell file-tag-remove {tag} %s");
    }
}

/// <summary>Converts file/folder names to text files.</summary>
[Command("file_convert_text", Summary = "Converts file/folder names to text files.")]
public sealed class FileConvertTextCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-convert-text %s");
    }
}

/// <summary>convert documents using pandoc.</summary>
[Command("document_convert", Summary = "convert documents using pandoc. Usage: document_convert [<extension>]")]
public sealed class DocumentConvertCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string extension = Rest(1).Trim() is { Length: > 0 } given
            ? given
            : "pdf";

        FileManager.Execute($"shell document-convert {extension} %s");
    }
}

/// <summary>Converts images to text If only one file is highlighted, it will be treated as a single selection.</summary>
[Command("ocr", Summary = "Converts images to text If only one file is highlighted, it will be treated as a single selection.")]
public sealed class OcrCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell image-convert-text %s");
    }
}

/// <summary>Extract audio from a video without conversion.</summary>
[Command("video_convert_audio", Summary = "Extract audio from a video without conversion.")]
public sealed class VideoConvertAudioCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell video-convert-audio-tui %s");
    }
}

/// <summary>Combine similar media files.</summary>
[Command("media_combine", Summary = "Combine similar media files.")]
public sealed class MediaCombineCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell media-combine-tui %s");
    }
}

/// <summary>Set valid filenames.</summary>
[Command("file_rename_valid", Summary = "Set valid filenames.")]
public sealed class FileRenameValidCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-rename-valid %s");
    }
}

/// <summary>Combine pdf files.</summary>
[Command("pdf_combine", Summary = "Combine pdf files.")]
public sealed class PdfCombineCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell pdf-combine %s");
    }
}

/// <summary>Combine images into pdf files.</summary>
[Command("image_combine_pdf", Summary = "Combine images into pdf files.")]
public sealed class ImageCombinePdfCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell image-combine-pdf %s");
    }
}

/// <summary>Converts text to speech using Google If only one file is highlighted, it will be treated as a single selection.</summary>
[Command("text_to_speech", Summary = "Converts text to speech using Google If only one file is highlighted, it will be treated as a single selection.")]
public sealed class TextToSpeechCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell text-to-speech %s");
    }
}

/// <summary>Convert PDF to text.</summary>
[Command("pdf_convert_text", Summary = "Convert PDF to text.")]
public sealed class PdfConvertTextCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell pdf-convert-text %s");
    }
}

/// <summary>Interactively extract tracks from mkv/a without conversion.</summary>
[Command("mkv_extract_track", Summary = "Interactively extract tracks from mkv/a without conversion.")]
public sealed class MkvExtractTrackCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell mkv-extract-track-tui %s");
    }
}

/// <summary>Embed a subtitle in a video file.</summary>
[Command("embed_subtitle", Summary = "Embed a subtitle in a video file.")]
public sealed class EmbedSubtitleCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell embed-subtitle %s");
    }
}

/// <summary>epub_convert_multiple Converts epub to text or pdf If only one file is highlighted, it will be treated as a single selection.</summary>
[Command("epub_convert_multiple_tui", Summary = "epub_convert_multiple Converts epub to text or pdf If only one file is highlighted, it will be treated as a single selection.")]
public sealed class EpubConvertMultipleTuiCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell epub-convert-multiple-tui %s");
    }
}

/// <summary>tag files with percentage.</summary>
[Command("files_tag_percentage", Summary = "tag files with percentage.")]
public sealed class FilesTagPercentageCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-tag-percentage %s");
    }
}

/// <summary>remove files tags with percentage.</summary>
[Command("files_tag_remove_percentage", Summary = "remove files tags with percentage.")]
public sealed class FilesTagRemovePercentageCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-tag-remove-percentage %s");
    }
}

/// <summary>remove numbers from the start of filenames.</summary>
[Command("file_number_remove", Summary = "remove numbers from the start of filenames.")]
public sealed class FileNumberRemoveCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-number-remove %s");
    }
}

/// <summary>Add background music to audio file.</summary>
[Command("audio_add_music", Summary = "Add background music to audio file.")]
public sealed class AudioAddMusicCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell audio-add-music-tui %s");
    }
}

/// <summary>Change volume of audio : audio_process.</summary>
[Command("audio_process", Summary = "Change volume of audio : audio_process.")]
public sealed class AudioProcessCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell audio-process-tui %s");
    }
}

/// <summary>Calculate length of videos in subdirectores of selected folders : with a specific tag : Demonstrates how commands can be executed on selected folders.</summary>
[Command("media_length_tag", Summary = "Calculate length of videos in subdirectores of selected folders : with a specific tag : Demonstrates how commands can be executed on selected folders.")]
public sealed class MediaLengthTagCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        foreach (FsNode entry in FileManager.Selection)
        {
            FileManager.Execute($"shell -w media-length-tag-tui {ShellWord.Quote(entry.Basename)} ");
        }

        FileManager.ChangeMode("normal");
    }
}

/// <summary>Set filenames to title case.</summary>
[Command("file_rename_title", Summary = "Set filenames to title case.")]
public sealed class FileRenameTitleCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-rename-title %s");
    }
}

/// <summary>Change filename extension.</summary>
[Command("file_rename_extension", Summary = "Change filename extension.")]
public sealed class FileRenameExtensionCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell file-rename-extension-tui %s");
    }
}

/// <summary>Watermark images.</summary>
[Command("image_watermark", Summary = "Watermark images.")]
public sealed class ImageWatermarkCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute("shell image-watermark-tui %s");
    }
}

// ---------------------------------------------------------------------------------------------
// Hand-ported. Each of these does more than run one shell command, so the generator refuses them
// rather than guessing — it would rather leave a name on a to-do list than emit something that
// looks right and runs the tool on nothing.
// ---------------------------------------------------------------------------------------------

/// <summary>Finds a file anywhere below here with fzf.</summary>
/// <remarks>
/// A quantifier restricts the search to directories, so <c>1fd</c> jumps to a folder. fd is used
/// when it is installed and <c>find</c> otherwise, exactly as the ranger version did — the
/// difference matters because fd honours .gitignore and is very much faster on a large tree.
/// </remarks>
[Command("fzf_select", Summary = "Find a file below here with fzf.")]
public sealed class FzfSelectCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!Executables.Exists("fzf"))
        {
            FileManager.Notify("Could not find fzf in the PATH.", isError: true);
            return;
        }

        bool directoriesOnly = Quantifier is > 0;
        bool hidden = FileManager.Settings.ShowHidden;
        string finder = Executables.Exists("fdfind") ? "fdfind"
                      : Executables.Exists("fd") ? "fd"
                      : string.Empty;

        string source = finder.Length > 0
            ? string.Join(' ',
                finder,
                "--follow",
                hidden ? "--hidden" : string.Empty,
                "--no-ignore-vcs --exclude '.git' --exclude '*.py[co]' --exclude '__pycache__'",
                directoriesOnly ? "--type directory" : string.Empty,
                "--color=always")
            : string.Join(' ',
                "find -L . -mindepth 1",
                hidden ? "-false" : @"-path '*/\.*' -prune",
                @"-o \( -name '\.git' -o -iname '\.*py[co]' -o -fstype 'dev' -o -fstype 'proc' \) -prune",
                directoriesOnly ? "-o -type d" : string.Empty,
                "-o -print | cut -b3-");

        // The environment goes in as a prefix rather than through the process's environment
        // block, because these commands run through a shell anyway and one string is easier to
        // read in a log than a dictionary.
        string command =
            $"FZF_DEFAULT_COMMAND={ShellWord.Quote(source)} "
            + "FZF_DEFAULT_OPTS='--height=100% --layout=reverse --ansi --preview=\"pistol {}\"' "
            + "fzf --no-multi";

        Chooser.Jump(FileManager, command);
    }
}

/// <summary>Finds a file anywhere on the system with locate and fzf.</summary>
[Command("fzf_locate", Summary = "Find a file anywhere with locate and fzf.")]
public sealed class FzfLocateCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!Executables.Exists("fzf"))
        {
            FileManager.Notify("Could not find fzf in the PATH.", isError: true);
            return;
        }

        Chooser.Jump(FileManager, "locate home | fzf -e -i");
    }
}

/// <summary>Shared ending for the choosers: run it, then go where it points.</summary>
internal static class Chooser
{
    /// <summary>Runs a chooser and lands on whatever path it printed.</summary>
    /// <param name="fileManager">Where to move the cursor.</param>
    /// <param name="command">The command line to run.</param>
    internal static void Jump(IFileManager fileManager, string command)
    {
        ProcessResult result = fileManager.Runner.RunCapturingOutput(
            new ProcessRequest(command, ProcessFlags.None, fileManager.CurrentDirectory.Path));

        if (result.Error is { } error)
        {
            fileManager.Notify(error, isError: true);
            return;
        }

        // A non-zero exit is the user pressing Escape, which is not a failure and deserves no
        // message: fzf exits 130 when it is abandoned.
        if (result.ExitCode is not 0)
        {
            return;
        }

        string choice = result.Output.Trim();

        if (choice.Length == 0)
        {
            return;
        }

        // Relative to where the chooser ran, since fd prints relative paths.
        string path = Path.GetFullPath(choice, fileManager.CurrentDirectory.Path);

        if (Directory.Exists(path))
        {
            fileManager.CurrentTab.Enter(path);
            fileManager.ReloadCurrentDirectory();
            return;
        }

        if (!fileManager.SelectPath(path))
        {
            fileManager.Notify($"{path}: not found", isError: true);
        }
    }
}

/// <summary>Runs fd here and lands on the first match, remembering the rest.</summary>
/// <remarks>
/// <c>:fd_search [-d&lt;depth&gt;] &lt;query&gt;</c>. Depth defaults to one level, so the plain
/// form searches only this directory. <c>&lt;A-n&gt;</c> and <c>&lt;A-p&gt;</c> then walk the
/// results without searching again.
/// </remarks>
[Command("fd_search", Summary = "Search with fd: fd_search [-d<depth>] <query>")]
public sealed class FdSearchCommand : CangerCommand
{
    /// <summary>The last search's results, shared with fd_next and fd_prev.</summary>
    /// <remarks>
    /// Static because the results outlive the command object — the whole point is that the next
    /// keystroke walks them. Ranger keeps them in a class attribute for the same reason.
    /// </remarks>
    internal static readonly List<string> Results = [];

    /// <summary>Which result the cursor is on.</summary>
    internal static int Position;

    /// <inheritdoc />
    public override void Execute()
    {
        Results.Clear();
        Position = 0;

        string finder = Executables.Exists("fdfind") ? "fdfind"
                      : Executables.Exists("fd") ? "fd"
                      : string.Empty;

        if (finder.Length == 0)
        {
            FileManager.Notify("Couldn't find fd in the PATH.", isError: true);
            return;
        }

        string first = Argument(1);

        if (first.Length == 0)
        {
            FileManager.Notify(":fd_search needs a query.", isError: true);
            return;
        }

        // `-d3 something` sets the depth; anything else is the whole query at depth one.
        bool depthGiven = first.StartsWith("-d", StringComparison.Ordinal);
        string depth = depthGiven ? first : "-d1";
        string query = depthGiven ? Rest(2) : Rest(1);

        if (query.Trim().Length == 0)
        {
            FileManager.Notify(":fd_search needs a query.", isError: true);
            return;
        }

        string hidden = FileManager.Settings.ShowHidden ? "--hidden" : string.Empty;

        string command = string.Join(' ',
            finder, "--follow", depth, hidden,
            "--no-ignore-vcs --exclude '.git' --exclude '*.py[co]' --exclude '__pycache__'",
            "--print0", ShellWord.Quote(query.Trim()));

        ProcessResult result = FileManager.Runner.RunCapturingOutput(
            new ProcessRequest(command, ProcessFlags.None, FileManager.CurrentDirectory.Path));

        if (result.Error is { } error)
        {
            FileManager.Notify(error, isError: true);
            return;
        }

        // --print0, so a newline in a filename cannot be mistaken for a separator.
        string root = FileManager.CurrentDirectory.Path;

        Results.AddRange(
            result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                  .Select(match => Path.GetFullPath(match, root))
                  .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        if (Results.Count == 0)
        {
            FileManager.Notify("No results found.");
            return;
        }

        FileManager.Notify($"Found {Results.Count} result{(Results.Count > 1 ? "s" : "")}.");
        FileManager.SelectPath(Results[0]);
    }
}

/// <summary>Goes to the next result from the last <c>:fd_search</c>.</summary>
[Command("fd_next", Summary = "Go to the next fd_search result.")]
public sealed class FdNextCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FdWalk.Step(FileManager, 1);
}

/// <summary>Goes to the previous result from the last <c>:fd_search</c>.</summary>
[Command("fd_prev", Summary = "Go to the previous fd_search result.")]
public sealed class FdPrevCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FdWalk.Step(FileManager, -1);
}

/// <summary>Walking the results of the last fd search.</summary>
internal static class FdWalk
{
    /// <summary>Moves through the results, wrapping at either end.</summary>
    internal static void Step(IFileManager fileManager, int direction)
    {
        List<string> results = FdSearchCommand.Results;

        if (results.Count == 0)
        {
            fileManager.Notify("no fd_search results; run :fd_search first", isError: true);
            return;
        }

        FdSearchCommand.Position =
            ((FdSearchCommand.Position + direction) % results.Count + results.Count)
            % results.Count;

        string path = results[FdSearchCommand.Position];

        if (!fileManager.SelectPath(path))
        {
            fileManager.Notify($"{Path.GetFileName(path)}: gone", isError: true);
            return;
        }

        fileManager.Notify($"{FdSearchCommand.Position + 1}/{results.Count} {Path.GetFileName(path)}");
    }
}

/// <summary>Makes a directory and moves the selection into it.</summary>
/// <remarks>
/// <c>:mkdirmv &lt;target&gt;</c>. The target is created if it does not exist, including any
/// parents, and a name that would collide gains a suffix rather than overwriting anything.
/// </remarks>
[Command("mkdirmv", Summary = "Make a directory and move the selection into it: mkdirmv <target>")]
public sealed class MkdirmvCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        IReadOnlyList<FsNode> selection = FileManager.Selection;

        if (selection.Count == 0)
        {
            FileManager.Notify("Error: no file(s) selected", isError: true);
            return;
        }

        string requested = Rest(1).Trim();

        if (requested.Length == 0)
        {
            FileManager.Notify("Error: target directory not specified", isError: true);
            return;
        }

        // Relative to here, and ~ means home — the bindings in cc.conf all use ~ paths.
        string target = Path.GetFullPath(
            Expand(requested), FileManager.CurrentDirectory.Path);

        int moved = 0;

        try
        {
            Directory.CreateDirectory(target);

            foreach (FsNode entry in selection)
            {
                string destination = Unique(Path.Join(target, entry.RelativePath));

                if (Path.GetDirectoryName(destination) is { Length: > 0 } parent)
                {
                    Directory.CreateDirectory(parent);
                }

                FileManager.FileSystem.Rename(entry.Path, destination);
                FileManager.Tags.MovePath(entry.Path, destination);
                moved++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileManager.Notify($"mkdirmv: {e.Message}", isError: true);
        }

        FileManager.ChangeMode("normal");
        FileManager.ReloadCurrentDirectory();
        FileManager.Notify($"Moved {moved} to {target}");
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectories();

    /// <summary>Expands a leading tilde.</summary>
    private static string Expand(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;

    /// <summary>
    /// A path nothing is using, by the same rule ranger's <c>make_safe_path</c> follows.
    /// </summary>
    /// <remarks>
    /// An underscore first, then numbers: <c>report.pdf</c>, <c>report_.pdf</c>,
    /// <c>report_0.pdf</c>. The extension is kept so the file still opens in the right program.
    /// </remarks>
    private static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        string candidate = Path.Join(directory, stem + "_" + extension);

        if (!stem.EndsWith('_') && !File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        for (int n = 0; ; n++)
        {
            candidate = Path.Join(directory, stem + "_" + n.ToString() + extension);

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}

/// <summary>Folds every subdirectory into this listing, or unfolds it again.</summary>
[Command("toggle_flat", Summary = "Flatten or unflatten the listing.")]
public sealed class ToggleFlatCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        bool flatten = !FileManager.CurrentDirectory.IsFlat;

        // -1 is every level; 0 is off.
        FileManager.CurrentDirectory.SetFlatLevel(flatten ? -1 : 0);
        FileManager.Notify(flatten ? "Flattened." : "Un-flattened.");
    }
}

/// <summary>Copies the marked files into the directory under the cursor.</summary>
[Command("copy_selected_to_highlight",
         Summary = "Copy the selection into the highlighted directory.")]
public sealed class CopySelectedToHighlightCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (Highlighted.Directory(FileManager) is not { } target)
        {
            return;
        }

        // The marks are the selection; taking them now matters because the cursor is on the
        // destination, which must not become one of the things being copied.
        IReadOnlyList<FsNode> sources =
            [.. FileManager.CurrentDirectory.MarkedEntries.Where(
                e => !string.Equals(e.Path, target, StringComparison.Ordinal))];

        if (sources.Count == 0)
        {
            FileManager.Notify("nothing marked to copy", isError: true);
            return;
        }

        FileManager.SetCopyBuffer(sources, cut: false);
        FileManager.Execute($"paste dest={target}");
        FileManager.Notify($"Copying {sources.Count} to {target}");
    }
}

/// <summary>Numbers the subdirectories of the directory under the cursor.</summary>
[Command("directories_number_highlight",
         Summary = "Number the subdirectories of the highlighted directory.")]
public sealed class DirectoriesNumberHighlightCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (Highlighted.Directory(FileManager) is not { } target)
        {
            return;
        }

        FileManager.Execute($"shell -f directory-number {ShellWord.Quote(target)}");
        FileManager.Notify("Numbering directories.");
    }
}

/// <summary>The directory under the cursor, for the commands that act on one.</summary>
internal static class Highlighted
{
    /// <summary>The absolute path of the highlighted entry, or null with a complaint.</summary>
    internal static string? Directory(IFileManager fileManager)
    {
        if (fileManager.CurrentTab.Selected is not { } selected)
        {
            fileManager.Notify("Error: target directory not highlighted", isError: true);
            return null;
        }

        if (!selected.IsDirectory)
        {
            fileManager.Notify($"{selected.Basename} is not a directory", isError: true);
            return null;
        }

        return selected.Path;
    }
}

/// <summary>Opens each selected folder in a tab of its own.</summary>
[Command("open_in_tabs", Summary = "Open the selected folders in new tabs.")]
public sealed class OpenInTabsCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FsNode[] directories = [.. FileManager.Selection.Where(e => e.IsDirectory)];

        if (directories.Length == 0)
        {
            FileManager.Notify("Error: no folder(s) selected", isError: true);
            return;
        }

        foreach (FsNode directory in directories)
        {
            int number = 1;

            while (FileManager.Tabs.ContainsKey(number))
            {
                number++;
            }

            FileManager.OpenTab(number, directory.Path);

            // The tab is named after the folder, which is the whole reason for opening several:
            // otherwise they are indistinguishable numbers.
            FileManager.CurrentTab.Label = directory.RelativePath;
        }

        FileManager.ChangeMode("normal");
        FileManager.Notify($"Opened {directories.Length} tab(s)");
    }
}

/// <summary>Decrypts the selection with gpg.</summary>
/// <remarks>
/// One run per file, each writing to the name with the <c>.gpg</c> (or whatever) extension
/// removed — which is why the generator cannot do this one: the output name is computed.
/// </remarks>
[Command("gpg_decrypt_file", Summary = "Decrypt the selection with gpg.")]
public sealed class GpgDecryptFileCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        foreach (FsNode entry in FileManager.Selection)
        {
            string plain = Path.GetFileNameWithoutExtension(entry.RelativePath);

            FileManager.Execute(
                $"shell -f gpg -o {ShellWord.Quote(plain)} -d {ShellWord.Quote(entry.Basename)}");
        }

        FileManager.ChangeMode("normal");
    }
}

/// <summary>Numbers the selected files from a starting point.</summary>
/// <remarks><c>:file_number [&lt;start&gt; [&lt;padding&gt;]]</c>, defaulting to 1 and 3.</remarks>
[Command("file_number", Summary = "Number the selection: file_number [<start> [<padding>]]")]
public sealed class FileNumberCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string start = Argument(1) is { Length: > 0 } given ? given : "1";
        string padding = Argument(2) is { Length: > 0 } width ? width : "3";

        FileManager.Execute($"shell file-number {start} {padding} %s");
        FileManager.ChangeMode("normal");
    }
}

/// <summary>Marks every file whose name shares this one's common part.</summary>
/// <remarks>
/// The pattern strips a progress suffix — <c>-12-34r-56p</c>, <c>-part-1-2r-3p</c>, or a
/// <c>#tag</c> — so a set of files belonging together can be marked in one keystroke however far
/// through them the naming has got.
/// </remarks>
[Command("file_select_similar", Summary = "Mark the files sharing this one's name.")]
public sealed class FileSelectSimilarCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (SimilarName.Of(FileManager, Rest(1)) is not { } stem)
        {
            return;
        }

        // Anchored, so `Series 01` does not also mark `Another Series 01`.
        FileManager.Execute($"scout -m ^{stem}");
        FileManager.Notify($"Marked files matching {stem}");
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectoryContent();
}

/// <summary>Gathers the files sharing a name into a folder of that name.</summary>
[Command("file_copy_similar", Summary = "Copy the files sharing this one's name into a folder.")]
public sealed class FileCopySimilarCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (SimilarName.Of(FileManager, Rest(1)) is not { } stem)
        {
            return;
        }

        FileManager.Execute($"scout -m {stem}");

        string target = Path.Join(FileManager.CurrentDirectory.Path, stem);

        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileManager.Notify($"file_copy_similar: {e.Message}", isError: true);
            return;
        }

        // Through cp rather than Canger's own copy engine, because the ranger version did and
        // --reflink=auto is what makes it instant on this filesystem.
        FileManager.Execute(
            $"shell cp -rv --reflink=auto --preserve=timestamps %s {ShellWord.Quote(target)}");

        FileManager.ReloadCurrentDirectory();
        FileManager.Execute($"scout -m {stem}");
        FileManager.Execute("files_tag seen");

        FileManager.ChangeMode("normal");
        FileManager.Notify($"Copied to {target}");
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectoryContent();
}

/// <summary>The part of a filename that a set of related files has in common.</summary>
internal static class SimilarName
{
    /// <summary>
    /// Strips the progress suffix and extension from a name.
    /// </summary>
    /// <param name="fileManager">Where a complaint goes if there is no name to work from.</param>
    /// <param name="typed">The name, usually supplied by the <c>%f</c> macro.</param>
    /// <returns>The common part, or null when there was nothing to work from.</returns>
    internal static string? Of(IFileManager fileManager, string typed)
    {
        string name = typed.Trim();

        // The binding passes %f, but falling back to the cursor means `:file_select_similar`
        // typed on its own still does the obvious thing.
        if (name.Length == 0)
        {
            name = fileManager.CurrentTab.Selected?.RelativePath ?? string.Empty;
        }

        if (name.Length == 0)
        {
            fileManager.Notify("Error: No file highlighted", isError: true);
            return null;
        }

        string stripped = Progress().Replace(name, string.Empty).Trim();
        string stem = Path.GetFileNameWithoutExtension(stripped);

        if (stem.Length == 0)
        {
            fileManager.Notify($"nothing left of {name} to match on", isError: true);
            return null;
        }

        return stem;
    }

    /// <summary>
    /// A trailing progress marker: <c>-12-34r-56p</c>, optionally <c>-part-</c>, or <c> #tag</c>.
    /// </summary>
    /// <remarks>
    /// Built at runtime rather than with <c>[GeneratedRegex]</c>: this file is compiled by Canger's
    /// own Roslyn host, which runs no source generators, so the attribute would leave the partial
    /// method with no body. Compiled once into a static field, which is the next best thing.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex ProgressPattern =
        new(@"(-?(-part-)?\d{1,4}-\d{1,4}r-\d{1,4}p[^.]*|\s+#\w+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The pattern that strips a progress marker.</summary>
    private static System.Text.RegularExpressions.Regex Progress() => ProgressPattern;
}

/// <summary>Shows the mount menu, and goes wherever it mounted something.</summary>
[Command("mount", Summary = "Mount and unmount devices, then go there.")]
public sealed class MountCommand : CangerCommand
{
    /// <summary>The menu program.</summary>
    private const string Menu = "mmtui";

    /// <inheritdoc />
    public override void Execute()
    {
        if (!Executables.Exists(Menu))
        {
            FileManager.Notify($"Could not find {Menu} in the PATH.", isError: true);
            return;
        }

        // The menu draws on the terminal and prints the chosen mount point on standard output,
        // which is exactly the arrangement RunCapturingOutput exists for. Ranger routed it
        // through a temporary file because it had no such thing.
        ProcessResult result = FileManager.Runner.RunCapturingOutput(
            new ProcessRequest(Menu, ProcessFlags.None, FileManager.CurrentDirectory.Path));

        if (result.Error is { } error)
        {
            FileManager.Notify(error, isError: true);
            return;
        }

        string chosen = result.Output.Split('\n', 2)[0].Trim();

        if (chosen.Length > 0 && Directory.Exists(chosen))
        {
            FileManager.CurrentTab.Enter(chosen);
            FileManager.ReloadCurrentDirectory();
        }
    }
}


/// <summary>Verifies the selected GPG signatures.</summary>
/// <remarks>
/// Only files ending <c>.sig</c> are passed to gpg, which is why the generator refuses this one:
/// it runs the tool per file *and* filters which files, and dropping the filter would hand gpg
/// every selected file instead of only the signatures.
/// </remarks>
[Command("gpg_signature_verify", Summary = "Verify the selected GPG signatures.")]
public sealed class GpgSignatureVerifyCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        int verified = 0;

        foreach (FsNode entry in FileManager.Selection)
        {
            if (!string.Equals(Path.GetExtension(entry.RelativePath), ".sig",
                               StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // -w so gpg's verdict can be read: a good or bad signature is the entire point and
            // it is reported on stderr.
            FileManager.Execute($"shell -w gpg --verify {ShellWord.Quote(entry.Basename)}");
            verified++;
        }

        FileManager.ChangeMode("normal");

        if (verified == 0)
        {
            FileManager.Notify("no .sig files selected", isError: true);
        }
    }
}

/// <summary>Converts audio to a free format.</summary>
/// <remarks>
/// With a bitrate — <c>:audio_convert_foss 32</c> — it goes straight to the command-line tool,
/// which honours it. Without one, the <c>-tui</c> wrapper offers the use-case presets including
/// lossless FLAC, which is what the <c>eac</c> binding wants.
/// </remarks>
[Command("audio_convert_foss",
         Summary = "Convert audio to a free format: audio_convert_foss [<bitrate>]")]
public sealed class AudioConvertFossCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute(Rest(1).Trim() is { Length: > 0 } bitrate
            ? $"shell audio-convert-foss {bitrate} %s"
            : "shell audio-convert-foss-tui %s");

        FileManager.ChangeMode("normal");
    }
}

/// <summary>Splits a PDF.</summary>
/// <remarks>
/// With a page count typed it goes straight to the tool; without one the <c>-tui</c> wrapper
/// asks, which is what <c>eps</c> relies on.
/// </remarks>
[Command("pdf_split", Summary = "Split a PDF: pdf_split [<pages>]")]
public sealed class PdfSplitCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute(Rest(1).Trim() is { Length: > 0 } after
            ? $"shell pdf-split {after} %s"
            : "shell pdf-split-tui %s");

        FileManager.ChangeMode("normal");
    }
}

/// <summary>Splits a text file.</summary>
/// <remarks>Same two shapes as <c>:pdf_split</c>: a typed size, or the wrapper that asks.</remarks>
[Command("text_split", Summary = "Split a text file: text_split [<lines>]")]
public sealed class TextSplitCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        FileManager.Execute(Rest(1).Trim() is { Length: > 0 } after
            ? $"shell text-split {after} %s"
            : "shell text-split-tui %s");

        FileManager.ChangeMode("normal");
    }
}
