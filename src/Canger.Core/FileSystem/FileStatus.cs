// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Native;

namespace Canger.Core.FileSystem;

/// <summary>
/// A snapshot of a filesystem object's metadata, equivalent to the parts of <c>struct stat</c>
/// that Canger actually uses.
/// </summary>
/// <remarks>
/// This is deliberately a value-like record: a listing produces thousands of these, and treating
/// them as immutable snapshots means a background scan can hand results to the UI thread without
/// any further synchronisation.
/// </remarks>
/// <param name="Kind">The decoded file type.</param>
/// <param name="Mode">Raw mode bits, including both the file type and the permission bits.</param>
/// <param name="Size">Size in bytes. Meaningful for regular files and symlinks.</param>
/// <param name="HardLinkCount">Number of hard links to the underlying inode.</param>
/// <param name="Uid">Owning user id.</param>
/// <param name="Gid">Owning group id.</param>
/// <param name="Inode">Inode number, unique within <paramref name="Device"/>.</param>
/// <param name="Device">Identifier of the filesystem holding the file.</param>
/// <param name="AccessTime">Last access time.</param>
/// <param name="ModifyTime">Last content modification time.</param>
/// <param name="ChangeTime">Last inode change time.</param>
public readonly record struct FileStatus(
    FileKind Kind,
    uint Mode,
    long Size,
    uint HardLinkCount,
    uint Uid,
    uint Gid,
    ulong Inode,
    ulong Device,
    DateTimeOffset AccessTime,
    DateTimeOffset ModifyTime,
    DateTimeOffset ChangeTime)
{
    /// <summary>The permission bits, with the file-type bits masked off.</summary>
    public UnixFileMode Permissions => (UnixFileMode)(Mode & 0xFFF);

    /// <summary>Whether this is a directory.</summary>
    public bool IsDirectory => Kind == FileKind.Directory;

    /// <summary>Whether this is a symbolic link.</summary>
    public bool IsSymbolicLink => Kind == FileKind.SymbolicLink;

    /// <summary>Whether this is a character or block device node.</summary>
    public bool IsDevice => Kind is FileKind.CharacterDevice or FileKind.BlockDevice;

    /// <summary>Whether the owner has the execute bit set.</summary>
    public bool IsExecutableByOwner => (Permissions & UnixFileMode.UserExecute) != 0;

    /// <summary>
    /// Whether this file lives on the same filesystem as <paramref name="other"/>, which is the
    /// precondition for a reflink clone or a rename-based move.
    /// </summary>
    /// <param name="other">The status to compare against.</param>
    /// <returns><see langword="true"/> when both are on the same device.</returns>
    public bool IsOnSameDeviceAs(FileStatus other) => Device == other.Device;

    /// <summary>
    /// Builds a <see cref="FileStatus"/> from a raw <c>statx</c> result.
    /// </summary>
    /// <param name="buffer">The kernel-populated buffer.</param>
    /// <returns>The decoded status.</returns>
    internal static FileStatus FromStatx(in StatxBuffer buffer) => new(
        Kind: DecodeKind(buffer.Mode),
        Mode: buffer.Mode,
        Size: (long)buffer.Size,
        HardLinkCount: buffer.HardLinkCount,
        Uid: buffer.Uid,
        Gid: buffer.Gid,
        Inode: buffer.Inode,
        Device: buffer.ContainingDevice,
        AccessTime: buffer.AccessTime.ToDateTimeOffset(),
        ModifyTime: buffer.ModifyTime.ToDateTimeOffset(),
        ChangeTime: buffer.ChangeTime.ToDateTimeOffset());

    /// <summary>Decodes the file-type bits of a Unix mode into a <see cref="FileKind"/>.</summary>
    /// <remarks>
    /// Public because anything holding a raw mode — a preview script's output, an archive
    /// listing, a test fixture — needs the same decoding, and duplicating the bit patterns
    /// elsewhere would invite them to drift.
    /// </remarks>
    /// <param name="mode">The raw mode bits.</param>
    /// <returns>The corresponding kind, or <see cref="FileKind.Unknown"/>.</returns>
    public static FileKind DecodeKind(uint mode) => (mode & NativeConstants.SIfmt) switch
    {
        NativeConstants.SIfreg => FileKind.Regular,
        NativeConstants.SIfdir => FileKind.Directory,
        NativeConstants.SIflnk => FileKind.SymbolicLink,
        NativeConstants.SIffifo => FileKind.Fifo,
        NativeConstants.SIfsock => FileKind.Socket,
        NativeConstants.SIfchr => FileKind.CharacterDevice,
        NativeConstants.SIfblk => FileKind.BlockDevice,
        _ => FileKind.Unknown,
    };
}
