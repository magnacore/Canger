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
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        FileStatus? sourceStatus = _fileSystem.GetStatus(source, followSymbolicLinks: false);

        // A symbolic link is recreated rather than followed, so copying a tree of links does not
        // silently turn them into copies of whatever they pointed at.
        if (sourceStatus is { IsSymbolicLink: true })
        {
            return CopySymbolicLink(source, destination);
        }

        // The fast paths need real file descriptors, which only a real filesystem has. Anything
        // else — a test double, or a future virtual filesystem — copies through streams, which
        // works everywhere and is what the fallback does in any case.
        if (_fileSystem is not LocalFileSystem)
        {
            return CopyThroughStreams(source, destination, onProgress, cancellationToken);
        }

        try
        {
            using SafeFileHandle sourceHandle = File.OpenHandle(
                source, FileMode.Open, FileAccess.Read, FileShare.Read);

            using SafeFileHandle destinationHandle = File.OpenHandle(
                destination, FileMode.Create, FileAccess.Write, FileShare.None);

            long length = RandomAccess.GetLength(sourceHandle);

            if (AllowReflink && TryReflink(sourceHandle, destinationHandle, source, destination))
            {
                return new FileCopyResult(CopyStrategy.Reflink, 0);
            }

            if (AllowKernelCopy &&
                TryKernelCopy(sourceHandle, destinationHandle, length, onProgress,
                              cancellationToken, out long kernelCopied))
            {
                return new FileCopyResult(CopyStrategy.KernelCopy, kernelCopied);
            }

            long copied = CopyByHand(sourceHandle, destinationHandle, onProgress,
                                     cancellationToken);

            return new FileCopyResult(CopyStrategy.Buffered, copied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileCopyResult(CopyStrategy.None, 0, e.Message);
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

    /// <summary>Asks the kernel to move the data without it passing through this process.</summary>
    private static bool TryKernelCopy(SafeFileHandle source, SafeFileHandle destination,
                                      long length, Action<long>? onProgress,
                                      CancellationToken cancellationToken, out long copied)
    {
        copied = 0;

        if (length == 0)
        {
            return true;
        }

        int sourceFd = (int)source.DangerousGetHandle();
        int destinationFd = (int)destination.DangerousGetHandle();

        while (copied < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            nint moved = Libc.CopyFileRange(
                sourceFd, 0, destinationFd, 0, (nuint)(length - copied), 0);

            if (moved < 0)
            {
                int error = Marshal.GetLastPInvokeError();

                if (error == NativeConstants.Eintr)
                {
                    continue;
                }

                // Not supported here, or not between these two files. If nothing has moved the
                // caller can fall back cleanly; if something has, the destination is already
                // partly written and the buffered path would duplicate it.
                if (copied == 0)
                {
                    return false;
                }

                throw new IOException(
                    $"copy_file_range failed after {copied} bytes (errno {error})");
            }

            if (moved == 0)
            {
                break;
            }

            copied += moved;
            onProgress?.Invoke(moved);
        }

        return true;
    }

    /// <summary>Reads and writes the data a block at a time.</summary>
    private static long CopyByHand(SafeFileHandle source, SafeFileHandle destination,
                                   Action<long>? onProgress, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockSize);

        try
        {
            long offset = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = RandomAccess.Read(source, buffer.AsSpan(0, BlockSize), offset);
                if (read == 0)
                {
                    break;
                }

                RandomAccess.Write(destination, buffer.AsSpan(0, read), offset);
                offset += read;
                onProgress?.Invoke(read);
            }

            return offset;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Copies a file's data through streams, without needing descriptors.</summary>
    private FileCopyResult CopyThroughStreams(string source, string destination,
                                              Action<long>? onProgress,
                                              CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockSize);

        try
        {
            using Stream input = _fileSystem.OpenRead(source);
            using Stream output = _fileSystem.OpenWrite(destination);

            long copied = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = input.Read(buffer, 0, BlockSize);
                if (read == 0)
                {
                    break;
                }

                output.Write(buffer, 0, read);
                copied += read;
                onProgress?.Invoke(read);
            }

            return new FileCopyResult(CopyStrategy.Buffered, copied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileCopyResult(CopyStrategy.None, 0, e.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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

            if (_fileSystem.Exists(destination))
            {
                _fileSystem.Delete(destination);
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
