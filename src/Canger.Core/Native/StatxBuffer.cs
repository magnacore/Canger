// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Canger.Core.Native;

/// <summary>A <c>struct statx_timestamp</c> as returned by <c>statx(2)</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StatxTimestamp
{
    /// <summary>Seconds since the Unix epoch.</summary>
    internal long Seconds;

    /// <summary>Nanoseconds within the second.</summary>
    internal uint Nanoseconds;

    /// <summary>Reserved by the kernel; always zero.</summary>
    internal int Reserved;

    /// <summary>Converts this timestamp to a UTC <see cref="DateTimeOffset"/>.</summary>
    internal readonly DateTimeOffset ToDateTimeOffset() =>
        DateTimeOffset.FromUnixTimeSeconds(Seconds).AddTicks(Nanoseconds / 100);
}

/// <summary>
/// A <c>struct statx</c> as returned by <c>statx(2)</c>. The layout is a stable kernel ABI
/// and is 256 bytes on every architecture.
/// </summary>
/// <remarks>
/// Canger uses <c>statx</c> rather than <c>stat</c> because glibc's <c>stat</c> symbol has been
/// versioned across releases, whereas <c>statx</c> has had one fixed layout since Linux 4.11.
/// It also reports the device as split major/minor numbers, which is what the reflink
/// same-filesystem check needs.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 256)]
internal struct StatxBuffer
{
    /// <summary>Bitmask of which fields the kernel actually filled in.</summary>
    internal uint Mask;

    /// <summary>Preferred I/O block size.</summary>
    internal uint BlockSize;

    /// <summary>Extra file attributes (compressed, encrypted, immutable, ...).</summary>
    internal ulong Attributes;

    /// <summary>Number of hard links.</summary>
    internal uint HardLinkCount;

    /// <summary>Owning user id.</summary>
    internal uint Uid;

    /// <summary>Owning group id.</summary>
    internal uint Gid;

    /// <summary>File type and permission bits.</summary>
    internal ushort Mode;

    // Kernel padding between Mode and Inode. Written by the kernel, never read here.
#pragma warning disable CS0649
    private readonly ushort _spare0;
#pragma warning restore CS0649

    /// <summary>Inode number.</summary>
    internal ulong Inode;

    /// <summary>Size in bytes.</summary>
    internal ulong Size;

    /// <summary>Allocated size in 512-byte blocks.</summary>
    internal ulong Blocks;

    /// <summary>Which bits of <see cref="Attributes"/> are supported here.</summary>
    internal ulong AttributesMask;

    /// <summary>Last access time.</summary>
    internal StatxTimestamp AccessTime;

    /// <summary>Creation ("birth") time, where the filesystem records one.</summary>
    internal StatxTimestamp BirthTime;

    /// <summary>Last inode change time.</summary>
    internal StatxTimestamp ChangeTime;

    /// <summary>Last modification time.</summary>
    internal StatxTimestamp ModifyTime;

    /// <summary>Major number of the device this file represents, for device nodes.</summary>
    internal uint RepresentedDeviceMajor;

    /// <summary>Minor number of the device this file represents, for device nodes.</summary>
    internal uint RepresentedDeviceMinor;

    /// <summary>Major number of the device the file resides on.</summary>
    internal uint ContainingDeviceMajor;

    /// <summary>Minor number of the device the file resides on.</summary>
    internal uint ContainingDeviceMinor;

    /// <summary>
    /// The device the file resides on, packed into a single value so two files can be compared
    /// for "same filesystem" — the precondition for a reflink copy.
    /// </summary>
    internal readonly ulong ContainingDevice =>
        ((ulong)ContainingDeviceMajor << 32) | ContainingDeviceMinor;
}
