// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Canger.Core.Native;

/// <summary>
/// A <c>struct statvfs</c> as returned by <c>statvfs(3)</c>, used for the free-space readout in
/// the status bar.
/// </summary>
/// <remarks>
/// <para>
/// This layout is the 64-bit one. glibc inserts a four-byte <c>__f_unused</c> field after
/// <c>f_fsid</c> only when <c>_STATVFSBUF_F_UNUSED</c> is defined, which happens only for a
/// 32-bit word size (<c>bits/statvfs.h:24-26</c>). Canger targets linux-x64 and linux-arm64, both
/// of which have <c>__WORDSIZE == 64</c>, so the field is absent.
/// </para>
/// <para>
/// The alternative — <see cref="System.IO.DriveInfo"/> — reports on a mount point rather than an
/// arbitrary path, so it would attribute every path to the root filesystem.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct StatvfsBuffer
{
    /// <summary>Filesystem block size.</summary>
    internal nuint BlockSize;

    /// <summary>Fragment size; the unit that the block counts below are expressed in.</summary>
    internal nuint FragmentSize;

    /// <summary>Total blocks on the filesystem.</summary>
    internal ulong TotalBlocks;

    /// <summary>Free blocks, including those reserved for the superuser.</summary>
    internal ulong FreeBlocks;

    /// <summary>Free blocks available to an unprivileged process.</summary>
    internal ulong AvailableBlocks;

    /// <summary>Total inodes.</summary>
    internal ulong TotalFileNodes;

    /// <summary>Free inodes.</summary>
    internal ulong FreeFileNodes;

    /// <summary>Free inodes available to an unprivileged process.</summary>
    internal ulong AvailableFileNodes;

    /// <summary>Filesystem identifier.</summary>
    internal nuint FileSystemId;

    /// <summary>Mount flags.</summary>
    internal nuint Flags;

    /// <summary>Maximum filename length.</summary>
    internal nuint MaximumNameLength;

    /// <summary>Filesystem type.</summary>
    internal uint Type;

    // Kernel padding. Written by the kernel, never read here.
#pragma warning disable CS0649
    private readonly int _spare0;
    private readonly int _spare1;
    private readonly int _spare2;
    private readonly int _spare3;
    private readonly int _spare4;
#pragma warning restore CS0649
}
