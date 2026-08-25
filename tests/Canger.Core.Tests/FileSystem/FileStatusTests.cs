// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileSystem;

/// <summary>
/// Decoding of raw Unix mode bits. These constants come from <c>man 7 inode</c> and are the same
/// ones ranger compares against in <c>container/fsobject.py:307-336</c>.
/// </summary>
public class FileStatusTests
{
    [Theory]
    [InlineData(0x8000u, FileKind.Regular)]         // S_IFREG
    [InlineData(0x4000u, FileKind.Directory)]       // S_IFDIR
    [InlineData(0xA000u, FileKind.SymbolicLink)]    // S_IFLNK
    [InlineData(0x1000u, FileKind.Fifo)]            // S_IFIFO
    [InlineData(0xC000u, FileKind.Socket)]          // S_IFSOCK
    [InlineData(0x2000u, FileKind.CharacterDevice)] // S_IFCHR
    [InlineData(0x6000u, FileKind.BlockDevice)]     // S_IFBLK
    [InlineData(0x0000u, FileKind.Unknown)]
    public void DecodeKind_MapsFileTypeBits(uint mode, FileKind expected) =>
        Assert.Equal(expected, FileStatus.DecodeKind(mode));

    [Fact]
    public void DecodeKind_IgnoresPermissionBits()
    {
        Assert.Equal(FileKind.Regular, FileStatus.DecodeKind(0x8000u | 0x1A4 /* 0644 octal */));
        Assert.Equal(FileKind.Directory, FileStatus.DecodeKind(0x4000u | 0x1ED /* 0755 octal */));
    }

    [Fact]
    public void Permissions_MasksOffFileTypeBits()
    {
        FileStatus status = StatusWithMode(0x8000u | 0x1A4 /* 0644 octal */);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            status.Permissions);
    }

    [Fact]
    public void IsExecutableByOwner_ReadsTheOwnerExecuteBit()
    {
        Assert.True(StatusWithMode(0x8000u | 0x1ED /* 0755 octal */).IsExecutableByOwner);
        Assert.False(StatusWithMode(0x8000u | 0x1A4 /* 0644 octal */).IsExecutableByOwner);
    }

    [Fact]
    public void IsOnSameDeviceAs_ComparesTheContainingDevice()
    {
        // Same device is the precondition for both a reflink clone and a rename-based move,
        // so this comparison gates the copy engine's fast paths.
        Assert.True(StatusWithDevice(42).IsOnSameDeviceAs(StatusWithDevice(42)));
        Assert.False(StatusWithDevice(42).IsOnSameDeviceAs(StatusWithDevice(43)));
    }

    private static FileStatus StatusWithMode(uint mode) => new(
        FileStatus.DecodeKind(mode), mode, Size: 0, HardLinkCount: 1, Uid: 0, Gid: 0,
        Inode: 1, Device: 1, default, default, default);

    private static FileStatus StatusWithDevice(ulong device) => new(
        FileKind.Regular, 0x8000u, Size: 0, HardLinkCount: 1, Uid: 0, Gid: 0,
        Inode: 1, Device: device, default, default, default);
}
