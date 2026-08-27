// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers;
using System.Runtime.InteropServices;
using Canger.Core.FileSystem;
using Canger.Core.Native;
using Microsoft.Win32.SafeHandles;

namespace Canger.Core.FileOperations;

/// <summary>How a single file was handled, and what it cost.</summary>
/// <param name="Strategy">How it was copied.</param>
/// <param name="TransferredBytes">Bytes that actually moved. Zero for a reflink or a rename.</param>
/// <param name="Error">Why it failed, when it did.</param>
public readonly record struct FileCopyResult(
    CopyStrategy Strategy,
    long TransferredBytes,
    string? Error = null)
{
    /// <summary>Whether the file was copied.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Copies file data, choosing the cheapest method the filesystem will allow.
/// </summary>
/// <remarks>
/// <para>
/// Three methods are tried in turn, and the difference between them is large. A reflink asks the
/// filesystem to share the underlying data, so copying a ten-gigabyte file takes no time and no
/// extra space until one copy is written to; btrfs, XFS and bcachefs all support it.
/// <c>copy_file_range</c> keeps the data in the kernel, avoiding a round trip through this
/// process. Only when neither is available is the data read and written by hand.
/// </para>
/// <para>
/// Falling back is expected rather than exceptional — a reflink cannot cross filesystems, and
/// most filesystems do not support one at all — so a failure at one level moves quietly to the
/// next.
/// </para>
/// </remarks>
public sealed class CopyEngine(IFileSystem fileSystem)
{
    /// <summary>
    /// How much is read at a time when copying by hand.
    /// </summary>
    /// <remarks>
    /// Also how often progress is reported, so it bounds how long a copy can appear stalled.
    /// Ranger uses the same size.
    /// </remarks>
    public const int BlockSize = 16 * 1024;

    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>Whether to try reflinking at all.</summary>
    /// <remarks>Exposed so a test can force the fallback paths on a filesystem that supports it.</remarks>
    public bool AllowReflink { get; set; } = true;

    /// <summary>Whether to try the in-kernel copy.</summary>
    public bool AllowKernelCopy { get; set; } = true;

    /// <summary>
    /// Copies one file's data, reporting progress as it goes.
    /// </summary>
    /// <param name="source">The file to copy.</param>
    /// <param name="destination">Where to put it. Anything there already is replaced.</param>
    /// <param name="onProgress">
    /// Called with each block's size as data moves. Not called at all for a reflink, because no
    /// data moves.
    /// </param>
    /// <param name="cancellationToken">Abandons the copy.</param>
    /// <returns>How it was copied, and what it cost.</returns>
    public FileCopyResult CopyFile(string source, string destination,
                                   Action<long>? onProgress = null,
                                   CancellationToken cancellationToken = default)
    {
        // Drains the resumable form. Everything that used to be here now lives there, so the two
        // cannot drift: this is the same copy, run without pausing.
        FileCopyResult? last = null;

        foreach (FileCopyResult? step in CopyFileSteps(source, destination, onProgress,
                                                       cancellationToken))
        {
            if (step is { } finished)
            {
                last = finished;
            }
        }

        return last ?? new FileCopyResult(CopyStrategy.None, 0, "the copy produced no result");
    }

    /// <summary>
    /// Copies a file a piece at a time, handing control back between pieces.
    /// </summary>
    /// <param name="source">The file to read.</param>
    /// <param name="destination">The file to write.</param>
    /// <param name="onProgress">Called with the bytes moved by each piece.</param>
    /// <param name="cancellationToken">Abandons the copy.</param>
    /// <returns>
    /// <see langword="null"/> for each piece moved, then the outcome as the final element.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The whole point is the pause. Copying a file was one indivisible call, so the task queue's
    /// time slice only ever fell <em>between</em> files: a single large file from a slow disc took
    /// the main loop with it for the whole transfer — no redraw, no keys, no cancelling, and no
    /// progress bar at the one moment there was progress to show.
    /// </para>
    /// <para>
    /// Ranger's copy is a generator that yields inside its byte loop for the same reason
    /// (<c>core/loader.py:120-160</c>).
    /// </para>
    /// <para>
    /// Abandoning the enumeration is a supported way to stop: the cleanup lives in a
    /// <see langword="finally"/>, so a partly written destination is removed whether the copy
    /// failed, was cancelled, or was simply not enumerated to the end. That is a state the old
    /// shape could not reach, and it is the one that had to be got right.
    /// </para>
    /// </remarks>
    public IEnumerable<FileCopyResult?> CopyFileSteps(string source, string destination,
                                                      Action<long>? onProgress = null,
                                                      CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        FileStatus? sourceStatus = _fileSystem.GetStatus(source, followSymbolicLinks: false);

        // Whether the destination is ours to remove if this is abandoned part-way. Taken before
        // anything is opened, because opening it for writing is what truncates it.
        bool destinationIsOurs = !_fileSystem.Exists(destination);

        // A symbolic link is recreated rather than followed, so copying a tree of links does not
        // silently turn them into copies of whatever they pointed at.
        if (sourceStatus is { IsSymbolicLink: true })
        {
            yield return CopySymbolicLink(source, destination);
            yield break;
        }

        // Refusing before anything is opened, as ranger does
        // (`ext/shutil_generatorized.py:133-136`). Reached only for a real file copy: a symbolic
        // link source is recreated above and never reads its own target.
        //
        // On a stock runtime this is belt and braces — .NET takes an inode-scoped advisory lock,
        // so opening the same file for reading and for truncating writing collides and the copy
        // fails anyway. Measured: a self-copy, a copy onto a hard link, and a copy onto a symlink
        // to the source all leave the file intact. But that protection is an implementation
        // detail rather than a decision, it disappears if `System.IO.DisableFileLocking` is ever
        // set, and the error it produces — "used by another process" — tells the user nothing
        // true. A file manager should not rely on an accident for this.
        if (IsSameFile(source, destination))
        {
            yield return new FileCopyResult(
                CopyStrategy.None, 0,
                $"'{Path.GetFileName(source)}' and '{Path.GetFileName(destination)}' "
                + "are the same file");
            yield break;
        }

        // The fast paths need real file descriptors, which only a real filesystem has. Anything
        // else — a test double, or a future virtual filesystem — copies through streams, which
        // works everywhere and is what the fallback does in any case.
        Transfer transfer = _fileSystem is LocalFileSystem
            ? new HandleTransfer(this, source, destination, destinationIsOurs, onProgress)
            : new StreamTransfer(this, source, destination, destinationIsOurs, onProgress);

        try
        {
            while (true)
            {
                // One bounded piece. Everything that can go wrong is caught inside, except
                // cancellation, which is left to unwind through the `finally` below.
                FileCopyResult? outcome = transfer.Advance(cancellationToken);

                if (outcome is { } finished)
                {
                    yield return finished;
                    yield break;
                }

                yield return null;
            }
        }
        finally
        {
            transfer.Dispose();
        }
    }

    /// <summary>How long one piece of a copy may spend moving bytes before pausing.</summary>
    /// <remarks>
    /// Time rather than a byte count, because the point is responsiveness and the same number of
    /// bytes takes wildly different times on an SSD and on a USB disc. Short enough that the
    /// interface stays live, long enough that the pausing costs nothing measurable.
    /// </remarks>
    internal TimeSpan StepBudget { get; set; } = TimeSpan.FromMilliseconds(12);

    /// <summary>
    /// The largest run handed to the kernel at once.
    /// </summary>
    /// <remarks>
    /// <c>copy_file_range</c> was asked for the whole remainder, and it does not return until it
    /// has moved it — so on a slow disc one call was the entire freeze. Asking for a bounded run
    /// gives the loop somewhere to pause.
    /// </remarks>
    internal long KernelRun { get; set; } = 8L * 1024 * 1024;

    /// <summary>One file copy in progress, able to stop and carry on.</summary>
    /// <remarks>
    /// Holds what the old loops held in locals — the handles, the offset, how much has moved —
    /// so that the work can be put down between pieces and picked up again. The safety rule is
    /// in <see cref="Dispose"/>: unless the copy finished and succeeded, a destination this
    /// engine created is removed. Failure, cancellation and simple abandonment all arrive there.
    /// </remarks>
    private abstract class Transfer(CopyEngine engine, string destination, bool destinationIsOurs)
        : IDisposable
    {
        protected CopyEngine Engine { get; } = engine;

        /// <summary>Set once the copy is over, successfully or not.</summary>
        protected FileCopyResult? Outcome { get; set; }

        /// <summary>Moves what it can within the step budget.</summary>
        /// <param name="cancellationToken">Abandons the copy.</param>
        /// <returns>The outcome once it is over, otherwise <see langword="null"/>.</returns>
        public abstract FileCopyResult? Advance(CancellationToken cancellationToken);

        /// <summary>Releases the handles and removes a destination that was left unfinished.</summary>
        public void Dispose()
        {
            Release();

            // The single place that decides. `Outcome` is null when the enumeration was
            // abandoned, and carries an error when the copy failed; either way what was written
            // is not a copy of anything and must not be left looking like one.
            if (Outcome is not { } outcome || !outcome.Succeeded)
            {
                Engine.DiscardPartial(destination, destinationIsOurs);
            }
        }

        /// <summary>Closes whatever was opened.</summary>
        protected abstract void Release();
    }

    /// <summary>A copy between real file descriptors, with the fast paths available.</summary>
    private sealed class HandleTransfer : Transfer
    {
        private readonly string _source;
        private readonly string _destination;
        private readonly Action<long>? _onProgress;

        private SafeFileHandle? _sourceHandle;
        private SafeFileHandle? _destinationHandle;
        private byte[]? _buffer;

        private long _length;
        private long _copied;
        private bool _started;
        private bool _kernel;

        public HandleTransfer(CopyEngine engine, string source, string destination,
                              bool destinationIsOurs, Action<long>? onProgress)
            : base(engine, destination, destinationIsOurs)
        {
            _source = source;
            _destination = destination;
            _onProgress = onProgress;
        }

        /// <inheritdoc />
        public override FileCopyResult? Advance(CancellationToken cancellationToken)
        {
            try
            {
                return Move(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Left to unwind: the caller's `finally` disposes this, which discards the part
                // that was written. Rethrown rather than turned into a result because a cancelled
                // copy is not a failed one, and the queue tells them apart.
                throw;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Outcome = new FileCopyResult(CopyStrategy.None, 0, e.Message);
                return Outcome;
            }
        }

        private FileCopyResult? Move(CancellationToken cancellationToken)
        {
            if (!_started)
            {
                _started = true;

                _sourceHandle = File.OpenHandle(_source, FileMode.Open, FileAccess.Read,
                                                FileShare.Read);
                _destinationHandle = File.OpenHandle(_destination, FileMode.Create,
                                                     FileAccess.Write, FileShare.None);

                _length = RandomAccess.GetLength(_sourceHandle);

                // A reflink moves nothing and cannot be interrupted, so it finishes here.
                if (Engine.AllowReflink &&
                    Engine.TryReflink(_sourceHandle, _destinationHandle, _source, _destination))
                {
                    Outcome = new FileCopyResult(CopyStrategy.Reflink, 0);
                    return Outcome;
                }

                _kernel = Engine.AllowKernelCopy;
            }

            // `do`, not `while`: a piece always moves something. A budget already spent would
            // otherwise hand back control having done nothing, for ever.
            long deadline = Environment.TickCount64 + (long)Engine.StepBudget.TotalMilliseconds;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_kernel)
                {
                    switch (KernelRun_(cancellationToken))
                    {
                        case KernelOutcome.Done:
                            Outcome = new FileCopyResult(CopyStrategy.KernelCopy, _copied);
                            return Outcome;

                        case KernelOutcome.Unsupported:
                            // Nothing has moved, so the buffered path can start cleanly.
                            _kernel = false;
                            continue;

                        default:
                            continue;
                    }
                }

                if (ByHand(cancellationToken))
                {
                    Outcome = new FileCopyResult(CopyStrategy.Buffered, _copied);
                    return Outcome;
                }
            }
            while (Environment.TickCount64 < deadline);

            return null;
        }

        private enum KernelOutcome
        {
            Moved,
            Done,
            Unsupported,
        }

        /// <summary>Asks the kernel to move one bounded run.</summary>
        private KernelOutcome KernelRun_(CancellationToken cancellationToken)
        {
            if (_length == 0 || _copied >= _length)
            {
                return KernelOutcome.Done;
            }

            int sourceFd = (int)_sourceHandle!.DangerousGetHandle();
            int destinationFd = (int)_destinationHandle!.DangerousGetHandle();

            // Bounded, so a slow disc cannot disappear into a single call.
            nuint want = (nuint)Math.Min(Engine.KernelRun, _length - _copied);
            nint moved = Libc.CopyFileRange(sourceFd, 0, destinationFd, 0, want, 0);

            if (moved < 0)
            {
                int error = Marshal.GetLastPInvokeError();

                if (error == NativeConstants.Eintr)
                {
                    return KernelOutcome.Moved;
                }

                // Not supported here, or not between these two files. If nothing has moved the
                // caller can fall back cleanly; if something has, the destination is already
                // partly written and the buffered path would duplicate it.
                if (_copied == 0)
                {
                    return KernelOutcome.Unsupported;
                }

                throw new IOException(
                    $"copy_file_range failed after {_copied} bytes (errno {error})");
            }

            if (moved == 0)
            {
                return KernelOutcome.Done;
            }

            _copied += moved;
            _onProgress?.Invoke(moved);

            return _copied >= _length ? KernelOutcome.Done : KernelOutcome.Moved;
        }

        /// <summary>Moves one block by hand.</summary>
        /// <returns>Whether the end of the file was reached.</returns>
        private bool ByHand(CancellationToken cancellationToken)
        {
            _buffer ??= ArrayPool<byte>.Shared.Rent(BlockSize);

            int read = RandomAccess.Read(_sourceHandle!, _buffer.AsSpan(0, BlockSize), _copied);
            if (read == 0)
            {
                return true;
            }

            RandomAccess.Write(_destinationHandle!, _buffer.AsSpan(0, read), _copied);
            _copied += read;
            _onProgress?.Invoke(read);

            return false;
        }

        /// <inheritdoc />
        protected override void Release()
        {
            _sourceHandle?.Dispose();
            _destinationHandle?.Dispose();

            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }
    }

    /// <summary>A copy through streams, for a filesystem that has no descriptors.</summary>
    private sealed class StreamTransfer : Transfer
    {
        private readonly CopyEngine _engine;
        private readonly string _source;
        private readonly string _destination;
        private readonly Action<long>? _onProgress;

        private Stream? _input;
        private Stream? _output;
        private byte[]? _buffer;
        private long _copied;
        private bool _started;

        public StreamTransfer(CopyEngine engine, string source, string destination,
                              bool destinationIsOurs, Action<long>? onProgress)
            : base(engine, destination, destinationIsOurs)
        {
            _engine = engine;
            _source = source;
            _destination = destination;
            _onProgress = onProgress;
        }

        /// <inheritdoc />
        public override FileCopyResult? Advance(CancellationToken cancellationToken)
        {
            try
            {
                return Move(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Outcome = new FileCopyResult(CopyStrategy.None, 0, e.Message);
                return Outcome;
            }
        }

        private FileCopyResult? Move(CancellationToken cancellationToken)
        {
            if (!_started)
            {
                _started = true;
                _input = _engine._fileSystem.OpenRead(_source);
                _output = _engine._fileSystem.OpenWrite(_destination);
                _buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            }

            long deadline = Environment.TickCount64 + (long)_engine.StepBudget.TotalMilliseconds;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = _input!.Read(_buffer!, 0, BlockSize);
                if (read == 0)
                {
                    Outcome = new FileCopyResult(CopyStrategy.Buffered, _copied);
                    return Outcome;
                }

                _output!.Write(_buffer!, 0, read);
                _copied += read;
                _onProgress?.Invoke(read);
            }
            while (Environment.TickCount64 < deadline);

            return null;
        }

        /// <inheritdoc />
        protected override void Release()
        {
            _input?.Dispose();
            _output?.Dispose();

            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }
    }

    /// <summary>Whether two paths lead to the same bytes.</summary>
    /// <param name="source">The file being read.</param>
    /// <param name="destination">The file about to be written.</param>
    /// <returns><see langword="true"/> when they are one file under two names.</returns>
    /// <remarks>
    /// By device and inode, not by path, because the interesting cases are the ones where the
    /// paths differ: a hard link, a symbolic link to the source, or the same name in a different
    /// case on a filesystem that does not distinguish them. Links are followed on both sides,
    /// which is what <c>os.path.samefile</c> does and therefore what ranger's check does.
    ///
    /// A destination that does not exist yet has no status, and is not the source.
    /// </remarks>
    private bool IsSameFile(string source, string destination)
    {
        FileStatus? from = _fileSystem.GetStatus(source, followSymbolicLinks: true);
        FileStatus? to = _fileSystem.GetStatus(destination, followSymbolicLinks: true);

        return from is { } a && to is { } b && a.Device == b.Device && a.Inode == b.Inode;
    }

    /// <summary>Removes a half-written destination, when it was this copy that created it.</summary>
    /// <param name="destination">The file being written.</param>
    /// <param name="isOurs">Whether nothing was there before this copy started.</param>
    /// <remarks>
    /// <para>
    /// A cancelled copy used to leave the bytes it had managed so far sitting at the destination,
    /// under the right name, with nothing to say it was a fragment. <c>cp</c> does the same on
    /// Ctrl-C, but a file manager with a cancel key in its task view is a different proposition:
    /// the user pressed something that says stop, and is entitled to assume nothing was left.
    /// </para>
    /// <para>
    /// Only when the destination did not exist beforehand. Overwriting one that did — which is
    /// what <c>po</c> asks for — has already truncated it by the time any of this runs, and
    /// deleting it as well would turn a damaged file into a missing one.
    /// </para>
    /// </remarks>
    private void DiscardPartial(string destination, bool isOurs)
    {
        if (!isOurs)
        {
            return;
        }

        try
        {
            if (_fileSystem.Exists(destination))
            {
                _fileSystem.Delete(destination);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do about it, and the copy is already being abandoned.
        }
    }

    /// <summary>
    /// Asks the filesystem to share the data rather than duplicate it.
    /// </summary>
    /// <remarks>
    /// Only attempted when both files are on the same filesystem, since a reflink cannot cross
    /// one. Even then it may be refused — the filesystem may not support it — which is an
    /// ordinary outcome, not an error.
    /// </remarks>
    private bool TryReflink(SafeFileHandle source, SafeFileHandle destination,
                            string sourcePath, string destinationPath)
    {
        // Resolve first: a relative path has no directory component, and asking for the status
        // of an empty string is an error rather than a miss.
        string destinationDirectory =
            Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ?? "/";

        FileStatus? sourceStatus = _fileSystem.GetStatus(sourcePath, followSymbolicLinks: true);
        FileStatus? destinationStatus = _fileSystem.GetStatus(
            destinationDirectory, followSymbolicLinks: true);

        if (sourceStatus is not { } from || destinationStatus is not { } to ||
            !from.IsOnSameDeviceAs(to))
        {
            return false;
        }

        int result = Libc.Ioctl(
            (int)destination.DangerousGetHandle(),
            NativeConstants.Ficlone,
            (int)source.DangerousGetHandle());

        return result == 0;
    }

    /// <summary>Recreates a symbolic link rather than copying what it points at.</summary>
    private FileCopyResult CopySymbolicLink(string source, string destination)
    {
        try
        {
            string? target = _fileSystem.ReadSymbolicLinkTarget(source);
            if (target is null)
            {
                return new FileCopyResult(CopyStrategy.None, 0, "could not read the link");
            }

            // Built beside and moved over, rather than deleted and recreated. The old shape had
            // a moment with nothing at the destination at all: if creating the link then failed —
            // a read-only directory, no inodes left — the user's file was gone and nothing
            // replaced it.
            //
            // `ExistsNoFollow`, because a broken link occupies the name while `Exists` reports it
            // absent; the old test skipped the delete and the create then failed with EEXIST.
            if (_fileSystem.ExistsNoFollow(destination))
            {
                string temporary = destination + ".canger-new";

                if (_fileSystem.ExistsNoFollow(temporary))
                {
                    _fileSystem.Delete(temporary);
                }

                _fileSystem.CreateSymbolicLink(temporary, target);
                _fileSystem.Replace(temporary, destination);

                return new FileCopyResult(CopyStrategy.Symlink, 0);
            }

            _fileSystem.CreateSymbolicLink(destination, target);
            return new FileCopyResult(CopyStrategy.Symlink, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileCopyResult(CopyStrategy.None, 0, e.Message);
        }
    }
}
