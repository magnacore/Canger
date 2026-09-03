// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Tasks;

namespace Canger.Core.FileOperations;

/// <summary>Where one of a transfer's sources ended up.</summary>
/// <param name="Source">Where it was.</param>
/// <param name="Destination">Where it is now, after any renaming to avoid a clash.</param>
public readonly record struct Landing(string Source, string Destination);

/// <summary>Whether files are being copied or moved.</summary>
public enum TransferKind
{
    /// <summary>The originals stay where they are.</summary>
    Copy,

    /// <summary>The originals are removed once they arrive.</summary>
    Move,
}

/// <summary>How a name clash at the destination is resolved.</summary>
public enum ClashPolicy
{
    /// <summary>Rename the incoming file, appending an underscore and then numbers.</summary>
    Rename,

    /// <summary>Rename the incoming file, keeping its extension on the end.</summary>
    RenameKeepingExtension,

    /// <summary>Replace what is already there.</summary>
    Overwrite,
}

/// <summary>
/// Copies or moves files in the background, a step at a time.
/// </summary>
/// <remarks>
/// <para>
/// Expressed as a job the task queue advances rather than a method that runs to completion, so a
/// copy of ten thousand files can be paused, reordered or cancelled, and the interface stays
/// responsive throughout.
/// </para>
/// <para>
/// A failure on one file is recorded and the rest continue. Abandoning a directory copy because
/// one file could not be read would be worse than finishing and saying what was missed.
/// </para>
/// </remarks>
public sealed class CopyJob : ILoadable, ISizedWork
{
    private readonly IFileSystem _fileSystem;
    private readonly CopyEngine _engine;
    private readonly IReadOnlyList<string> _sources;
    private readonly string _destination;
    private readonly TransferKind _kind;
    private readonly ClashPolicy _clashPolicy;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _errors = [];
    private readonly List<Landing> _landings = [];

    /// <summary>The sizing walk, kept so it is resumed rather than restarted.</summary>
    private IEnumerator<Unit>? _sizing;

    /// <summary>Prepares a transfer.</summary>
    /// <param name="fileSystem">The filesystem to work against.</param>
    /// <param name="sources">What to copy or move.</param>
    /// <param name="destination">The directory to put it in.</param>
    /// <param name="kind">Whether to copy or move.</param>
    /// <param name="clashPolicy">What to do about a name already in use.</param>
    /// <param name="cancellationToken">Abandons the transfer.</param>
    public CopyJob(IFileSystem fileSystem, IReadOnlyList<string> sources, string destination,
                   TransferKind kind = TransferKind.Copy,
                   ClashPolicy clashPolicy = ClashPolicy.Rename,
                   CancellationToken cancellationToken = default)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        _kind = kind;
        _clashPolicy = clashPolicy;
        _cancellationToken = cancellationToken;

        _engine = new CopyEngine(fileSystem);
        Progress = new CopyProgress(0, sources.Count);
    }

    /// <summary>How far along the transfer is.</summary>
    public CopyProgress Progress { get; }

    /// <summary>What is being copied or moved.</summary>
    public IReadOnlyList<string> Sources => _sources;

    /// <summary>Whether the originals are being removed.</summary>
    public TransferKind Kind => _kind;

    /// <summary>
    /// Where each source actually ended up, for the ones that arrived whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not derivable from the source name and the destination directory, which is why it is
    /// recorded rather than reconstructed: the clash policy renames what it moves, so a file
    /// pasted beside one of the same name lands as <c>notes_0.md</c> and a caller guessing
    /// <c>notes.md</c> would be naming a file that is not there.
    /// </para>
    /// <para>
    /// Only sources that finished without any error beneath them appear. A directory that lost
    /// one file on the way is not somewhere its contents can be said to have arrived.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Landing> Landings => _landings;

    /// <summary>The directory it is going to.</summary>
    public string Destination => _destination;

    /// <summary>Files that could not be transferred, and why.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Whether the transfer finished.</summary>
    public bool IsFinished { get; private set; }

    /// <inheritdoc />
    public string Subject
    {
        get
        {
            string verb = _kind == TransferKind.Copy ? "copying" : "moving";
            string what = _sources.Count == 1
                ? Path.GetFileName(_sources[0])
                : $"{_sources.Count} items";

            return $"{verb} {what}";
        }
    }

    /// <inheritdoc />
    public string Description
    {
        get
        {
            string detail = Progress.Describe();
            string strategies = Progress.DescribeStrategies();

            return strategies.Length > 0
                ? $"{Subject}: {detail}  [{strategies}]"
                : $"{Subject}: {detail}";
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Implemented explicitly so that <see cref="Progress"/> can carry the full detail the task
    /// view shows, rather than being reduced to a single fraction.
    /// </remarks>
    double? ILoadable.Progress => Progress.Fraction;

    /// <inheritdoc />
    public bool IsSized { get; private set; }

    /// <inheritdoc />
    public long? TotalBytes => IsSized ? Progress.TotalBytes : null;

    /// <inheritdoc />
    /// <remarks>
    /// Measured the same way the transfer's own estimate is, so the figure the queue reports for
    /// everything and the figure a single transfer reports for itself cannot disagree.
    /// </remarks>
    public long? RemainingBytes =>
        IsSized ? Math.Max(Progress.TotalBytes - Progress.CompletedBytes, 0) : null;

    /// <inheritdoc />
    public double? BytesPerSecond => Progress.BytesPerSecond;

    /// <inheritdoc />
    public IEnumerator<Unit> SizingSteps() => _sizing ??= Size();

    /// <summary>
    /// Walks the sources and adds up what there is to transfer, a little at a time.
    /// </summary>
    /// <remarks>
    /// Only metadata is read, so this can run while another job is copying without the two
    /// competing for the disc — which is the whole reason it is separable from the transfer.
    /// Nothing here writes, renames or deletes anything.
    /// </remarks>
    /// <returns>One element per file examined.</returns>
    private IEnumerator<Unit> Size()
    {
        long total = 0;

        foreach (string source in _sources)
        {
            foreach (long measured in Measure(source))
            {
                total += measured;

                // Revised as it goes rather than only at the end, so a transfer that starts
                // before the walk finishes still has a percentage to show.
                Progress.ReviseTotal(total);
                yield return Unit.Value;
            }
        }

        Progress.ReviseTotal(total);
        IsSized = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The size is established first, so the percentage and the estimate mean something from the
    /// first byte — a percentage against an unknown total is a lie. The queue usually has this
    /// done already, having sized the job while something else was running, in which case the
    /// loop below finds nothing left to do and the transfer starts immediately. When nothing did
    /// it in advance, this does it here, which is what keeps the job correct when it is driven
    /// directly rather than through the queue.
    /// </remarks>
    public IEnumerator<Unit> Steps()
    {
        IEnumerator<Unit> sizing = SizingSteps();
        while (sizing.MoveNext())
        {
            yield return Unit.Value;
        }

        foreach (string source in _sources)
        {
            if (Refuse(source) is { } reason)
            {
                _errors.Add($"{Path.GetFileName(source)}: {reason}");
                continue;
            }

            string target = ResolveTarget(source);

            // Errors are recorded per file as the transfer walks a tree, so the count either
            // side of one source says whether everything under it arrived.
            int errorsBefore = _errors.Count;

            foreach (Unit step in TransferAny(source, target))
            {
                yield return step;
            }

            if (_errors.Count == errorsBefore)
            {
                _landings.Add(new Landing(source, target));
            }
        }

        IsFinished = true;
    }

    /// <summary>
    /// Says why a source cannot be transferred where it has been asked to go.
    /// </summary>
    /// <param name="source">What is being transferred.</param>
    /// <returns>The reason, or <see langword="null"/> when the transfer is fine.</returns>
    /// <remarks>
    /// Both cases here destroy data if they are allowed to proceed. Pasting a directory inside
    /// itself would move it under a path that is about to stop existing, and the source is
    /// removed afterwards either way; pasting into the directory a file already sits in would,
    /// for a move, rename it onto itself and then delete it. Neither is worth attempting, so
    /// both are refused before anything is touched.
    /// </remarks>
    private string? Refuse(string source)
    {
        // Resolved, not merely normalised. `Path.GetFullPath` expands `.` and `..` and leaves
        // symbolic links alone, so a destination reaching the source by another name — `~/work`
        // pointing at `/mnt/data/work` — compared as two unrelated paths and the recursion went
        // ahead.
        string from = _fileSystem.ResolvePath(source);
        string to = _fileSystem.ResolvePath(_destination);

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return "cannot be pasted into itself";
        }

        // Only a directory can contain the destination, and only then is the recursion a problem.
        if (_fileSystem.GetStatus(source, followSymbolicLinks: false)
            is { IsDirectory: true, IsSymbolicLink: false }
            && PathRelation.IsInside(to, from))
        {
            return "cannot be pasted into a directory inside itself";
        }

        // A move whose source already sits in the destination is a rename onto itself. A copy is
        // still meaningful, since the clash policy gives it a new name.
        return _kind == TransferKind.Move
               && string.Equals(Path.GetDirectoryName(from), to, StringComparison.Ordinal)
            ? "is already there"
            : null;
    }


    /// <summary>Adds up how much there is to transfer.</summary>
    /// <summary>Adds up what a source holds, handing control back between files.</summary>
    /// <remarks>
    /// The walk itself is <see cref="TreeSize"/>, shared with compressing, which needs the same
    /// answer for the same reason. Two walks that could disagree about what a tree holds would be
    /// worse than one.
    /// </remarks>
    private IEnumerable<long> Measure(string path) =>
        TreeSize.Sizes(_fileSystem, path, _cancellationToken);

    /// <summary>Transfers one thing, whatever it is.</summary>
    private IEnumerable<Unit> TransferAny(string source, string target)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        FileStatus? status = _fileSystem.GetStatus(source, followSymbolicLinks: false);

        if (status is null)
        {
            _errors.Add($"{Path.GetFileName(source)}: could not be read");
            yield break;
        }

        // A link to a directory is copied as a link, not descended into.
        if (status.Value.IsDirectory && !status.Value.IsSymbolicLink)
        {
            foreach (Unit step in TransferDirectory(source, target))
            {
                yield return step;
            }
        }
        else
        {
            foreach (Unit step in TransferFile(source, target, status.Value))
            {
                yield return step;
            }
        }
    }

    /// <summary>Transfers a single file.</summary>
    private IEnumerable<Unit> TransferFile(string source, string target, FileStatus status)
    {
        Progress.BeginFile(source);

        // Moving within one filesystem is a rename: no data moves at all, however large the file.
        if (_kind == TransferKind.Move && CanRename(source, target))
        {
            bool renamed = false;
            try
            {
                _fileSystem.Rename(source, target);
                renamed = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Fall through to copy-then-delete.
            }

            if (renamed)
            {
                Progress.CompleteWithoutTransfer(status.Size, CopyStrategy.Rename);
                Progress.CompleteFile(CopyStrategy.Rename);
                yield return Unit.Value;
                yield break;
            }
        }

        // A piece at a time, handing control back between pieces. Copying a file used to be one
        // call that returned when the file was done, so the queue's time slice only fell between
        // files: one large file from a slow disc took the whole interface with it — nothing drawn,
        // no keys read, no way to cancel, and no progress bar at the one moment there was
        // progress. Ranger's copy yields inside its byte loop for the same reason
        // (`core/loader.py:120-160`).
        FileCopyResult result = default;

        foreach (FileCopyResult? step in _engine.CopyFileSteps(
                     source, target, Progress.AdvanceTransferred, _cancellationToken))
        {
            if (step is { } finished)
            {
                result = finished;
                break;
            }

            yield return Unit.Value;
        }

        if (!result.Succeeded)
        {
            _errors.Add($"{Path.GetFileName(source)}: {result.Error}");
            yield return Unit.Value;
            yield break;
        }

        // A reflink moves no bytes, so the percentage has to be advanced explicitly or the copy
        // would appear stalled at whatever the last real transfer reached.
        if (result.TransferredBytes == 0 && result.Strategy != CopyStrategy.Symlink)
        {
            Progress.CompleteWithoutTransfer(status.Size, result.Strategy);
        }

        MetadataCopier.Copy(_fileSystem, source, target, followSymbolicLinks: false);
        Progress.CompleteFile(result.Strategy);

        if (_kind == TransferKind.Move)
        {
            TryDelete(source);
        }

        yield return Unit.Value;
    }

    /// <summary>Transfers a directory and everything under it.</summary>
    private IEnumerable<Unit> TransferDirectory(string source, string target)
    {
        try
        {
            _fileSystem.CreateDirectory(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _errors.Add($"{Path.GetFileName(source)}: {e.Message}");
            yield break;
        }

        IReadOnlyList<DirectoryEntry> entries;
        try
        {
            entries = _fileSystem.ListDirectory(source, _cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // One unreadable subdirectory should not abandon the whole transfer.
            _errors.Add($"{Path.GetFileName(source)}: {e.Message}");
            yield break;
        }

        // Taken before the children are transferred so that anything that fails beneath this
        // directory — at any depth — is visible here afterwards. See the guard below.
        int errorsBefore = _errors.Count;

        foreach (DirectoryEntry entry in entries)
        {
            foreach (Unit step in TransferAny(Join(source, entry.Name), Join(target, entry.Name)))
            {
                yield return step;
            }
        }

        MetadataCopier.Copy(_fileSystem, source, target);

        if (_kind != TransferKind.Move)
        {
            yield break;
        }

        // The source is removed only when everything under it arrived. Errors are deliberately
        // collected rather than thrown — one unreadable file should not abandon the rest of the
        // transfer — but a move that then deletes the source regardless destroys precisely the
        // files that failed to copy. A destination that ran out of space, a name the filesystem
        // will not accept, a file larger than FAT32 allows: any of them, and the originals were
        // gone.
        //
        // Ranger reaches the same rule from the other direction: `copytree` raises when its own
        // error list is non-empty, which puts `rmtree(src)` out of reach
        // (`ext/shutil_generatorized.py:277-279` and `:318-321`). The port kept the collecting
        // and dropped the consequence.
        //
        // Counting rather than flagging is what makes this propagate: a failure three levels
        // down is still counted here, so every ancestor keeps its source too.
        if (_errors.Count != errorsBefore)
        {
            _errors.Add($"{Path.GetFileName(source)}: kept, because not everything could be moved");
            yield break;
        }

        TryDeleteDirectory(source);
    }

    /// <summary>Names this job has already written to, so it cannot write to one twice.</summary>
    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

    /// <summary>Works out where something should land, avoiding a clash.</summary>
    /// <param name="source">What is being transferred.</param>
    /// <returns>Where to put it.</returns>
    /// <remarks>
    /// Overwriting is a policy about what is <em>already</em> at the destination, which is what
    /// the user asked for by pressing <c>po</c>. It is not permission for one selected file to
    /// destroy another: a flattened listing, or a copy buffer built up across directories, can
    /// hold <c>sub1/a.txt</c> and <c>sub2/a.txt</c>, and both resolve here to the same name. The
    /// second used to overwrite the first, and on a move then delete its own source — leaving one
    /// of the two files nowhere at all.
    ///
    /// So a target this job has already used is made unique whatever the policy says. Ranger has
    /// the same hole; that is a bug of its own rather than a behaviour to reproduce.
    /// </remarks>
    private string ResolveTarget(string source)
    {
        string target = Join(_destination, Path.GetFileName(source));

        string resolved = _clashPolicy switch
        {
            ClashPolicy.Overwrite => _claimed.Contains(target)
                ? SafePath.MakeUnique(_fileSystem, target)
                : target,
            ClashPolicy.RenameKeepingExtension =>
                SafePath.MakeUniqueKeepingExtension(_fileSystem, target),
            _ => SafePath.MakeUnique(_fileSystem, target),
        };

        _claimed.Add(resolved);
        return resolved;
    }

    /// <summary>Whether a move can be done by renaming, which requires one filesystem.</summary>
    private bool CanRename(string source, string target)
    {
        FileStatus? from = _fileSystem.GetStatus(source, followSymbolicLinks: false);
        FileStatus? to = _fileSystem.GetStatus(
            Path.GetDirectoryName(Path.GetFullPath(target)) ?? "/", followSymbolicLinks: true);

        return from is { } a && to is { } b && a.IsOnSameDeviceAs(b);
    }

    private void TryDelete(string path)
    {
        try
        {
            _fileSystem.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _errors.Add($"{Path.GetFileName(path)}: could not remove the original: {e.Message}");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            _fileSystem.DeleteRecursive(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _errors.Add($"{Path.GetFileName(path)}: could not remove the original: {e.Message}");
        }
    }

    private static string Join(string directory, string name) =>
        directory == "/" ? "/" + name : directory + "/" + name;
}
