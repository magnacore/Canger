// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.FileSystem;

/// <summary>
/// The kind of filesystem object, decoded from the file-type bits of the Unix mode.
/// </summary>
/// <remarks>
/// Canger renders each kind differently and rifle matches on some of them, so the distinction
/// is kept explicit rather than collapsed into "file or directory".
/// </remarks>
public enum FileKind
{
    /// <summary>The kind could not be determined, typically because the file is inaccessible.</summary>
    Unknown = 0,

    /// <summary>A regular file.</summary>
    Regular,

    /// <summary>A directory.</summary>
    Directory,

    /// <summary>A symbolic link. Whether it resolves is reported separately.</summary>
    SymbolicLink,

    /// <summary>A named pipe (FIFO).</summary>
    Fifo,

    /// <summary>A Unix domain socket.</summary>
    Socket,

    /// <summary>A character device such as a terminal.</summary>
    CharacterDevice,

    /// <summary>A block device such as a disk.</summary>
    BlockDevice,
}
