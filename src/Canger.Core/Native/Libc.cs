// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Canger.Core.Native;

/// <summary>
/// Direct bindings to the C library calls Canger needs but the base class library does not expose.
/// </summary>
/// <remarks>
/// <para>
/// Three capabilities require going native:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>statx</c> — the BCL surfaces size, times and mode, but not uid, gid, inode, link count
///     or containing device, all of which Canger's status bar and copy engine need.
///   </description></item>
///   <item><description>
///     <c>ioctl(FICLONE)</c> — reflinks a file on a copy-on-write filesystem (btrfs, XFS,
///     bcachefs) in constant time. This is what makes a "copy" of a large file instant.
///   </description></item>
///   <item><description>
///     <c>copy_file_range</c> — an in-kernel copy that avoids a userspace round trip, used when
///     a reflink is not possible.
///   </description></item>
/// </list>
/// <para>
/// Every symbol used here is exported by glibc (verified against 2.41) and has been stable for
/// many releases. Callers must treat a negative return as failure and read
/// <see cref="Marshal.GetLastPInvokeError"/> for the reason.
/// </para>
/// </remarks>
internal static partial class Libc
{
    private const string LibraryName = "libc";

    /// <summary>
    /// Retrieves extended file status. See <c>statx(2)</c>.
    /// </summary>
    /// <param name="dirFd">Directory descriptor for relative paths, or <c>AT_FDCWD</c>.</param>
    /// <param name="pathname">Path to query.</param>
    /// <param name="flags">Resolution flags such as <c>AT_SYMLINK_NOFOLLOW</c>.</param>
    /// <param name="mask">Which fields to request.</param>
    /// <param name="buffer">Receives the result.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial int Statx(int dirFd, string pathname, int flags, uint mask,
                                      out StatxBuffer buffer);

    /// <summary>
    /// Issues a device control request. Canger uses this only for <c>FICLONE</c>, whose third
    /// argument is the source file descriptor passed by value.
    /// </summary>
    /// <param name="fd">Descriptor of the destination file.</param>
    /// <param name="request">The request code, for example <c>FICLONE</c>.</param>
    /// <param name="argument">The source file descriptor.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, ulong request, int argument);

    /// <summary>
    /// Copies a range of bytes between two descriptors entirely inside the kernel.
    /// See <c>copy_file_range(2)</c>.
    /// </summary>
    /// <param name="fdIn">Source descriptor.</param>
    /// <param name="offsetIn">Source offset, or <see cref="IntPtr.Zero"/> to use the file position.</param>
    /// <param name="fdOut">Destination descriptor.</param>
    /// <param name="offsetOut">Destination offset, or <see cref="IntPtr.Zero"/>.</param>
    /// <param name="length">Maximum number of bytes to copy.</param>
    /// <param name="flags">Reserved; must be zero.</param>
    /// <returns>Bytes copied, zero at end of input, or -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "copy_file_range", SetLastError = true)]
    internal static partial nint CopyFileRange(int fdIn, nint offsetIn, int fdOut, nint offsetOut,
                                              nuint length, uint flags);

    /// <summary>
    /// Reports filesystem statistics for the filesystem containing a path. See <c>statvfs(3)</c>.
    /// </summary>
    /// <param name="path">Any path on the filesystem of interest.</param>
    /// <param name="buffer">Receives the result.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "statvfs", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial int Statvfs(string path, out StatvfsBuffer buffer);

    /// <summary>Creates a hard link. See <c>link(2)</c>.</summary>
    /// <param name="existingPath">Path of the existing file.</param>
    /// <param name="newPath">Path of the link to create.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial int Link(string existingPath, string newPath);

    /// <summary>Changes a file's owner and group. See <c>chown(2)</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="owner">The user id, or -1 to leave it alone.</param>
    /// <param name="group">The group id, or -1 to leave it alone.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "chown", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial int Chown(string path, uint owner, uint group);

    /// <summary>
    /// Changes a file's owner and group without following a symbolic link. See <c>lchown(2)</c>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="owner">The user id, or -1 to leave it alone.</param>
    /// <param name="group">The group id, or -1 to leave it alone.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "lchown", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial int Lchown(string path, uint owner, uint group);

    /// <summary>Reads an extended attribute's value. See <c>lgetxattr(2)</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">Receives the value.</param>
    /// <param name="size">How much room <paramref name="value"/> has.</param>
    /// <returns>The value's length, or -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "lgetxattr", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial nint GetExtendedAttribute(string path, string name, byte[]? value,
                                                      nuint size);

    /// <summary>Sets an extended attribute. See <c>lsetxattr(2)</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    /// <param name="size">The value's length.</param>
    /// <param name="flags">Reserved; pass zero.</param>
    /// <returns>Zero on success, -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "lsetxattr", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial int SetExtendedAttribute(string path, string name, byte[] value,
                                                     nuint size, int flags);

    /// <summary>Lists a file's extended attribute names. See <c>llistxattr(2)</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="list">Receives the names, separated by NUL bytes.</param>
    /// <param name="size">How much room <paramref name="list"/> has.</param>
    /// <returns>The list's length, or -1 on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "llistxattr", StringMarshalling = StringMarshalling.Utf8,
                   SetLastError = true)]
    internal static partial nint ListExtendedAttributes(string path, byte[]? list, nuint size);

    /// <summary>Looks up the login name for a user id, or <see langword="null"/> if unknown.</summary>
    /// <param name="uid">The user id.</param>
    /// <returns>The user name, or <see langword="null"/>.</returns>
    internal static string? GetUserName(uint uid) => PasswdLookup.UserName(uid);

    /// <summary>Looks up the group name for a group id, or <see langword="null"/> if unknown.</summary>
    /// <param name="gid">The group id.</param>
    /// <returns>The group name, or <see langword="null"/>.</returns>
    internal static string? GetGroupName(uint gid) => PasswdLookup.GroupName(gid);
}
