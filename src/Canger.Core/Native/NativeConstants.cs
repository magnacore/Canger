// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Native;

/// <summary>
/// Constants from the Linux kernel and glibc headers that Canger depends on.
/// </summary>
/// <remarks>
/// These are part of the stable kernel ABI. Where a value is derived from a macro
/// (such as <c>_IOW</c>) the derivation is shown so it can be re-checked against
/// the header rather than taken on trust.
/// </remarks>
internal static class NativeConstants
{
    /// <summary>Special <c>dirfd</c> meaning "resolve relative paths against the CWD".</summary>
    internal const int AtFdCwd = -100;

    /// <summary>Do not follow the final symbolic link (the <c>lstat</c> behaviour).</summary>
    internal const int AtSymlinkNoFollow = 0x100;

    /// <summary>Do not trigger automounts while resolving the path.</summary>
    internal const int AtNoAutomount = 0x800;

    /// <summary>Request the fields a classic <c>stat</c> would return.</summary>
    internal const uint StatxBasicStats = 0x0000_07ff;

    // File type bits: st_mode & S_IFMT selects one of the S_IF* values below.

    /// <summary>Mask selecting the file-type bits out of <c>st_mode</c>.</summary>
    internal const uint SIfmt = 0xF000;

    /// <summary>Socket.</summary>
    internal const uint SIfsock = 0xC000;

    /// <summary>Symbolic link.</summary>
    internal const uint SIflnk = 0xA000;

    /// <summary>Regular file.</summary>
    internal const uint SIfreg = 0x8000;

    /// <summary>Block device.</summary>
    internal const uint SIfblk = 0x6000;

    /// <summary>Directory.</summary>
    internal const uint SIfdir = 0x4000;

    /// <summary>Character device.</summary>
    internal const uint SIfchr = 0x2000;

    /// <summary>FIFO / named pipe.</summary>
    internal const uint SIffifo = 0x1000;

    /// <summary>
    /// <c>FICLONE</c> — reflink (copy-on-write clone) an entire file, from
    /// <c>linux/fs.h</c>: <c>_IOW(0x94, 9, int)</c>.
    /// </summary>
    /// <remarks>
    /// <c>_IOW(type, nr, size)</c> expands to
    /// <c>(_IOC_WRITE &lt;&lt; 30) | (size &lt;&lt; 16) | (type &lt;&lt; 8) | nr</c>, so with
    /// <c>_IOC_WRITE == 1</c>, <c>size == sizeof(int) == 4</c>, <c>type == 0x94</c> and
    /// <c>nr == 9</c> this is
    /// <c>0x4000_0000 | 0x0004_0000 | 0x0000_9400 | 0x9 == 0x4004_9409</c>.
    /// </remarks>
    internal const ulong Ficlone = 0x4004_9409;

    /// <summary>Operation not supported — the filesystem cannot reflink.</summary>
    internal const int Eopnotsupp = 95;

    /// <summary>Invalid cross-device link — source and destination are on different filesystems.</summary>
    internal const int Exdev = 18;

    /// <summary>Interrupted system call; the caller should retry.</summary>
    internal const int Eintr = 4;
}
