// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;
// The ranger-archives plugin, ported.
//
// Four commands — compress, extract, extract_raw, extract_to_dirs — over a table of about twenty
// archive formats. The original expresses the table as a chain of twenty `elif search(regex)`
// branches, each rebuilding a command list; here it is a list of rules the two builders walk,
// which is the same information with the repetition taken out and, more to the point, is checkable
// by reading it.
//
// Each rule names the archivers that can do the job in order of preference, because the fast
// parallel ones — pbzip2, pigz, pixz — are worth using when installed and absent on most systems.

/// <summary>How one family of archive is made and unmade.</summary>
/// <param name="Pattern">Matches the archive's name.</param>
/// <param name="Programs">Archivers that can do it, best first.</param>
/// <param name="Kind">Which command shape to build.</param>
internal sealed record ArchiveRule(
    string Pattern,
    string[] Programs,
    ArchiveKind Kind);

/// <summary>The command shapes the archivers need.</summary>
internal enum ArchiveKind
{
    /// <summary>A tarball: tar drives the compressor through --use-compress-program.</summary>
    Tar,

    /// <summary>Plain tar, no compressor.</summary>
    TarOnly,

    /// <summary>A single-file compressor: gzip, xz, bzip2 and friends.</summary>
    Stream,

    /// <summary>7-zip style: <c>a</c> to add, <c>x</c> to extract, <c>-o</c> for the destination.</summary>
    SevenZip,

    /// <summary>rar style: <c>a</c>/<c>x</c>, destination as a trailing argument.</summary>
    Rar,

    /// <summary>zip and unzip, which are two different programs.</summary>
    Zip,
}

/// <summary>The formats, in the order the original tests them.</summary>
/// <remarks>
/// Order matters: <c>.tar.bz2</c> has to be tried before <c>.bz2</c>, or a tarball would be handed
/// to bzip2 alone and come out as a single undecompressed tar.
/// </remarks>
internal static class ArchiveFormats
{
    /// <summary>Every rule, most specific first.</summary>
    internal static readonly ArchiveRule[] All =
    [
        new(@"\.(tar\.|t)bz2*$", ["pbzip2", "lbzip2", "bzip2"], ArchiveKind.Tar),
        new(@"\.bz2*$", ["pbzip2", "lbzip2", "bzip2"], ArchiveKind.Stream),
        new(@"\.(tar\.(gz|z)|t(g|a)z)$", ["pigz", "gzip"], ArchiveKind.Tar),
        new(@"\.g*z$", ["pigz", "gzip"], ArchiveKind.Stream),
        new(@"\.tar\.lz4$", ["lz4"], ArchiveKind.Tar),
        new(@"\.lz4$", ["lz4"], ArchiveKind.Stream),
        new(@"\.tar\.lrz$", ["lrzip"], ArchiveKind.Tar),
        new(@"\.lrz$", ["lrzip"], ArchiveKind.Stream),
        new(@"\.tar\.lz$", ["plzip", "lzip"], ArchiveKind.Tar),
        new(@"\.lz$", ["plzip", "lzip"], ArchiveKind.Stream),
        new(@"\.(tar\.lzop|tzo)$", ["lzop"], ArchiveKind.Tar),
        new(@"\.lzop$", ["lzop"], ArchiveKind.Stream),
        new(@"\.(tar\.(xz|lzma)|t(xz|lz))$", ["pixz", "xz"], ArchiveKind.Tar),
        new(@"\.(xz|lzma)$", ["pixz", "xz"], ArchiveKind.Stream),
        new(@"\.tar\.zst$", ["zstd"], ArchiveKind.Tar),
        new(@"\.7z$", ["7z"], ArchiveKind.SevenZip),
        new(@"\.rar$", ["7z", "unrar", "rar"], ArchiveKind.Rar),
        new(@"\.zip$", ["zip", "7z"], ArchiveKind.Zip),
        new(@"\.zpaq$", ["zpaq"], ArchiveKind.SevenZip),
        new(@"\.l(zh|ha)$", ["lha"], ArchiveKind.Rar),
        new(@"\.tar$", ["tar", "7z"], ArchiveKind.TarOnly),
    ];

    /// <summary>The rule for an archive name, or null when no format matches.</summary>
    internal static (ArchiveRule Rule, string Program)? Match(string name)
    {
        foreach (ArchiveRule rule in All)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    name, rule.Pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                continue;
            }

            // The first installed archiver wins. A matching format with nothing to handle it
            // falls through to the caller's fallback rather than pretending to work.
            foreach (string program in rule.Programs)
            {
                if (Executables.Exists(program))
                {
                    return (rule, program);
                }
            }

            return null;
        }

        return null;
    }

    /// <summary>Quotes one word for the shell.</summary>
    internal static string Quote(string value) =>
        "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}

/// <summary>Builds the shell command that unpacks an archive.</summary>
internal static class Decompression
{
    /// <summary>The marker tar is asked to print, and that Canger reads back.</summary>
    internal const string CheckpointMarker = "canger-bytes:";

    /// <summary>Asks tar to report its progress in a form Canger reads.</summary>
    /// <remarks>
    /// The unit is blocks: with tar's default blocking factor every block is 10 240 bytes, so 200
    /// reports about every two megabytes.
    /// </remarks>
    internal const string Checkpoints =
        "--checkpoint=200 --checkpoint-action=echo='" + CheckpointMarker + "%{}T'";

    /// <summary>Whether this archive's extractor can be made to report as it goes.</summary>
    internal static bool SupportsCheckpoints(string archive) =>
        ArchiveFormats.Match(archive) is var (rule, _) &&
        rule.Kind is ArchiveKind.Tar or ArchiveKind.TarOnly;

    /// <summary>How much will come out, when that is knowable without reading the archive.</summary>
    /// <param name="archive">The archive being unpacked.</param>
    /// <returns>The uncompressed size, or <see langword="null"/> when it cannot be had cheaply.</returns>
    /// <remarks>
    /// <para>
    /// Only for a plain <c>.tar</c>, where the file on disk *is* the stream tar reads and the two
    /// numbers are the same. For a compressed one they are not: measured on a text tree, a
    /// 4 456-byte <c>.tar.lz</c> had tar reporting 26 603 520 bytes read. Using the file size there
    /// sent the bar to 100% almost immediately and left it there while the extraction ran on for
    /// seconds — which is how this was reported.
    /// </para>
    /// <para>
    /// The compressors can each say: <c>lzip -l</c>, <c>gzip -l</c>, <c>xz --robot --list</c>. Each
    /// prints a different shape, which is three parsers to keep working, and asking would mean
    /// starting a program while the interface is up. Returning nothing costs a percentage and
    /// keeps the byte count, which is always true — and a bar that lies is worse than a figure that
    /// admits what it does not know.
    /// </para>
    /// </remarks>
    internal static long? UncompressedSize(FsNode archive) =>
        ArchiveFormats.Match(archive.Basename) is var (rule, _) && rule.Kind is ArchiveKind.TarOnly
            ? archive.Status?.Size
            : null;

    /// <summary>The command line to extract an archive.</summary>
    /// <param name="archive">The archive's name, relative to where the command runs.</param>
    /// <param name="flags">Extra flags the user supplied.</param>
    /// <param name="into">Where to unpack, or null for the current directory.</param>
    /// <returns>A command line for the shell.</returns>
    internal static string Command(string archive, string flags, string? into)
    {
        string name = ArchiveFormats.Quote(archive);
        string extra = flags.Length > 0 ? " " + flags : string.Empty;
        string destination = into is { Length: > 0 } ? ArchiveFormats.Quote(into) : string.Empty;

        if (ArchiveFormats.Match(archive) is not var (rule, program))
        {
            // 7z reads a remarkable number of formats, so it is a better last resort than
            // refusing. The original falls back the same way.
            return destination.Length > 0
                ? $"7z x {name} -o{destination}"
                : $"7z x {name}";
        }

        return rule.Kind switch
        {
            // Checkpoints while unpacking too, and here the total is free: an archive's own size is
            // in the listing already, so extraction gets a real percentage in every case where
            // compressing a folder has to measure one first.
            ArchiveKind.Tar or ArchiveKind.TarOnly => destination.Length > 0
                ? $"tar -xf {name} {Checkpoints}{extra} -C {destination}"
                : $"tar -xf {name} {Checkpoints}{extra}",

            // A single-file compressor writes beside the archive; -d decompresses, -k keeps the
            // original, which is what someone extracting in a file manager expects.
            ArchiveKind.Stream => $"{program} -dk{extra} {name}",

            ArchiveKind.SevenZip => destination.Length > 0
                ? $"{program} x{extra} -o{destination} {name}"
                : $"{program} x{extra} {name}",

            ArchiveKind.Rar => destination.Length > 0
                ? $"{program} x{extra} {name} {destination}"
                : $"{program} x{extra} {name}",

            ArchiveKind.Zip => program == "zip"
                ? (destination.Length > 0
                    ? $"unzip{extra} {name} -d {destination}"
                    : $"unzip{extra} {name}")
                : (destination.Length > 0
                    ? $"7z x{extra} -o{destination} {name}"
                    : $"7z x{extra} {name}"),

            _ => $"7z x {name}",
        };
    }
}

/// <summary>Unpacks the selected archives here.</summary>
/// <remarks><c>:extract [&lt;directory&gt;]</c> — a named directory is created if needed.</remarks>
[Command("extract", Summary = "Extract the selected archives: extract [<directory>]")]
public sealed class ExtractCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => Extraction.Run(FileManager, Rest(1).Trim(), perArchive: false);

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectories();
}

/// <summary>Unpacks the selected archives here, passing flags straight through.</summary>
[Command("extract_raw", Summary = "Extract the selected archives with extra flags.")]
public sealed class ExtractRawCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        Extraction.Run(FileManager, target: null, perArchive: false, flags: Rest(1).Trim());
}

/// <summary>Unpacks each selected archive into a directory named after it.</summary>
/// <remarks>
/// What <c>ead</c> is bound to. Extracting several archives into one directory mixes their
/// contents together; this keeps each one's where it belongs.
/// </remarks>
[Command("extract_to_dirs", Summary = "Extract each archive into a directory of its own.")]
public sealed class ExtractToDirsCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        Extraction.Run(FileManager, target: null, perArchive: true, flags: Rest(1).Trim());
}

/// <summary>Shared body of the three extract commands.</summary>
internal static class Extraction
{
    /// <summary>Extracts the selection.</summary>
    /// <param name="fileManager">Where the selection and the destination come from.</param>
    /// <param name="target">One directory for everything, or null.</param>
    /// <param name="perArchive">Whether each archive gets a directory named after it.</param>
    /// <param name="flags">Extra flags for the archiver.</param>
    internal static void Run(IFileManager fileManager, string? target, bool perArchive,
                             string flags = "")
    {
        IReadOnlyList<FsNode> selection = fileManager.Selection;

        if (selection.Count == 0)
        {
            fileManager.Notify("extract: nothing selected", isError: true);
            return;
        }

        int started = 0;

        // Where the files will land, captured now: the queued work finishes long after this
        // returns, and by then the user may well be looking at somewhere else entirely.
        string workingDirectory = fileManager.CurrentDirectory.Path;

        foreach (FsNode archive in selection)
        {
            // Named after the part before the first dot, so `thing.tar.gz` unpacks into `thing`
            // rather than `thing.tar` — which is what the original's `(.*?)\.` captures.
            string? into = perArchive
                ? archive.Basename.Split('.', 2)[0]
                : target is { Length: > 0 } ? target : null;

            if (into is { Length: > 0 })
            {
                try
                {
                    Directory.CreateDirectory(Path.GetFullPath(into, workingDirectory));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    fileManager.Notify($"extract {archive.Basename}: {e.Message}", isError: true);
                    continue;
                }
            }

            string command = Decompression.Command(archive.Basename, flags, into);

            // Queued rather than run in front of the interface. Unpacking a large archive takes
            // long enough that taking the terminal away means staring at a blank screen until it
            // is done; this way it appears in the task view with the spinner turning, can be
            // cancelled from there, and the browser stays usable. Whatever the archiver says on
            // stderr — a wrong password, a corrupt file — is reported when it ends.
            //
            // One task per archive, as the original does (`ranger-archives/extract.py`), so a
            // single bad archive does not take the others down with it.
            fileManager.RunInBackground(
                $"Extracting: {archive.Basename}",
                command,
                workingDirectory,

                // The plugin's own `refresh` callback, bound to CommandLoader's `after` signal:
                // files that appeared out of nowhere are not otherwise noticed.
                _ => fileManager.ReloadDirectory(workingDirectory),

                // What tar counts while extracting is the *uncompressed* stream, not the file it
                // is reading. Measured: a 4 456-byte archive reported 26 603 520 bytes read. So
                // the archive's own size is a total only for a plain `.tar`, where the two are the
                // same thing; for a compressed one it is wrong by the compression ratio, and using
                // it made the bar reach 100% almost at once and sit there while the extraction ran
                // on. Without a total the task view shows the bytes, which is always true.
                Decompression.SupportsCheckpoints(archive.Basename)
                    ? new MarkerProgress(Decompression.CheckpointMarker,
                                         Decompression.UncompressedSize(archive))
                    : null);

            started++;
        }

        fileManager.Notify($"Extracting {started} archive(s)");
    }
}

/// <summary>Packs the selection into one archive.</summary>
/// <remarks>
/// <c>:compress [&lt;flags&gt;] [&lt;name.ext&gt;]</c>. The extension chooses the format; with no
/// name the archive is called after the current directory, as a zip.
/// </remarks>
[Command("compress", Summary = "Compress the selection: compress [<flags>] [<name.ext>]")]
public sealed class CompressCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        IReadOnlyList<FsNode> selection = FileManager.Selection;

        if (selection.Count == 0)
        {
            FileManager.Notify("compress: nothing selected", isError: true);
            return;
        }

        // The last word is the archive name when it looks like one — that is, when it has an
        // extension. Anything else is a flag for the archiver.
        string[] words = [.. Rest(1).Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        string? name = words.Length > 0 &&
                       System.Text.RegularExpressions.Regex.IsMatch(words[^1], @".+\.\w+$")
            ? words[^1]
            : null;

        string flags = string.Join(' ', name is null ? words : words[..^1]);

        name ??= Path.GetFileName(FileManager.CurrentDirectory.Path) + ".zip";

        string files = string.Join(
            ' ', selection.Select(e => ArchiveFormats.Quote(e.RelativePath)));

        string directory = FileManager.CurrentDirectory.Path;

        // Queued, exactly as `extract` above is. Compressing a tree of any size takes long enough
        // that running it in front of the interface means the terminal is taken away and the
        // screen sits blank until it finishes — and then asks for a keypress before giving it
        // back. This way it turns in the task view with the spinner, can be cancelled from there,
        // and the browser stays usable. Ranger's archive plugin queues both halves for the same
        // reason (`ranger-archives/compress.py`, through `CommandLoader`).
        string command = Build(name, flags, files);
        long? total = TotalBytesOf(selection);

        // Where the archiver can be made to count, it is; where it cannot, the archive is watched
        // growing. Either way the task view shows a figure instead of only a spinner.
        ICommandProgress progress = SupportsCheckpoints(name)
            ? Measured(new MarkerProgress(Decompression.CheckpointMarker, total), total, selection)
            : new GrowingFileProgress(FileManager.FileSystem, Path.Join(directory, name));

        FileManager.RunInBackground(
            $"Compressing: {name}",
            command,
            directory,

            // The archive appears out of nowhere when the archiver finishes; nothing else here
            // would notice, because writing a file does not change the directory's timestamp.
            _ => FileManager.ReloadDirectory(directory),

            progress);

        FileManager.Notify($"Compressing {selection.Count} into {name}");
    }

    /// <inheritdoc />
    /// <remarks>Offers the current directory's name with each format, as the original does.</remarks>
    public override IReadOnlyList<string> Complete(int direction)
    {
        string stem = Path.GetFileName(FileManager.CurrentDirectory.Path);

        return
        [
            .. new[] { ".7z", ".zip", ".tar.gz", ".tar.bz2", ".tar.xz" }
                .Select(extension => $"compress {stem}{extension}"),
        ];
    }


    /// <summary>Whether the archiver for this name can be made to report as it goes.</summary>
    /// <remarks>
    /// Only the tar family. <c>7z</c>, <c>zip</c> and <c>rar</c> print percentages of their own in
    /// formats that differ between versions, and scraping those would be a parser to maintain for
    /// each; watching the file they write costs one <c>stat</c> and cannot go out of date.
    /// </remarks>
    private static bool SupportsCheckpoints(string name) =>
        ArchiveFormats.Match(name) is var (rule, _) &&
        rule.Kind is ArchiveKind.Tar or ArchiveKind.TarOnly;

    /// <summary>Wraps a source so the queue measures what it was given.</summary>
    /// <remarks>
    /// Only when the total is not already known. A folder's size means walking it, which nothing
    /// should do on the spot — but the task queue measures a job while it waits, in
    /// three-millisecond slices, exactly as it does for a copy. Without this a folder showed
    /// <c>109 M</c> and no bar, because it knew how far it had got and not how far there was to go.
    /// </remarks>
    private ICommandProgress Measured(ICommandProgress inner, long? total,
                                      IReadOnlyList<FsNode> selection) =>
        total is null
            ? new MeasuredProgress(inner, FileManager.FileSystem,
                                   [.. selection.Select(entry => entry.Path)])
            : inner;

    /// <summary>What the selection amounts to, when that is already known.</summary>
    /// <remarks>
    /// <para>
    /// A file's size comes free from the listing. A directory's does not: measuring one means
    /// walking it, which <c>DirectorySize</c> exists to do and which its own documentation says
    /// nothing should pay for unasked. So a directory counts only if someone has already asked —
    /// <c>dc</c>, or <c>autoupdate_cumulative_size</c>.
    /// </para>
    /// <para>
    /// Returning <see langword="null"/> is not a failure. Without a total the task shows the bytes
    /// read so far rather than a percentage, which is the honest rendering; inventing one by
    /// guessing at a directory's contents would produce a bar that lies.
    /// </para>
    /// </remarks>
    private static long? TotalBytesOf(IReadOnlyList<FsNode> selection)
    {
        long total = 0;

        foreach (FsNode entry in selection)
        {
            long? bytes = entry.IsDirectory ? entry.CumulativeSize : entry.Status?.Size;

            if (bytes is not { } known)
            {
                return null;
            }

            total += known;
        }

        return total;
    }

    /// <summary>The command line that builds an archive.</summary>
    private static string Build(string name, string flags, string files)
    {
        string archive = ArchiveFormats.Quote(name);
        string extra = flags.Length > 0 ? " " + flags : string.Empty;

        if (ArchiveFormats.Match(name) is not var (rule, program))
        {
            return $"zip -r {ArchiveFormats.Quote(name + ".zip")} {files}";
        }

        return rule.Kind switch
        {
            // tar drives the compressor rather than piping into it, which is what lets the
            // parallel compressors use every core.
            // --checkpoint makes tar report the bytes it has read, to standard error. That is a
            // live count of the *input*, which paired with the size of the selection is a true
            // percentage rather than a guess from the compressed output.
            //
            // The unit is blocks, not records: with tar's default blocking factor of 20, every
            // block is 10 240 bytes, so --checkpoint=200 reports about every two megabytes. 1000
            // was tried first and reports every ten, which on a twenty-four megabyte archive
            // meant two readings and a bar that went 43%, 85%, done.
            ArchiveKind.Tar =>
                $"tar -cf {archive} --use-compress-program {program} {Decompression.Checkpoints}{extra} {files}",

            ArchiveKind.TarOnly => $"tar -cf {archive} {Decompression.Checkpoints}{extra} {files}",

            // -k keeps the original: losing the source to a keystroke would be unforgivable.
            ArchiveKind.Stream => $"{program} -k{extra} {files}",

            // -r to recurse, or a selected directory would be archived as an empty entry.
            ArchiveKind.SevenZip => $"{program} a -r{extra} {archive} {files}",

            ArchiveKind.Rar => $"{program} a{extra} {archive} {files}",

            ArchiveKind.Zip => program == "zip"
                ? $"zip -r{extra} {archive} {files}"
                : $"7z a -r{extra} {archive} {files}",

            _ => $"zip -r {archive} {files}",
        };
    }
}
