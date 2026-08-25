// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.FileOperations;

/// <summary>How a file was copied.</summary>
/// <remarks>
/// Worth distinguishing because the strategies differ by orders of magnitude. A reflink is
/// instant whatever the file's size; a buffered copy moves every byte through userspace. The
/// progress display and the time estimate both depend on knowing which happened.
/// </remarks>
public enum CopyStrategy
{
    /// <summary>Nothing was copied.</summary>
    None,

    /// <summary>
    /// The filesystem was asked to share the data rather than duplicate it, so the copy took
    /// constant time and no additional space until one side is written to.
    /// </summary>
    Reflink,

    /// <summary>The kernel copied the data without it passing through this process.</summary>
    KernelCopy,

    /// <summary>The data was read and written through a buffer.</summary>
    Buffered,

    /// <summary>The file was renamed, which moves it without copying anything.</summary>
    Rename,

    /// <summary>A symbolic link was recreated rather than its target duplicated.</summary>
    Symlink,
}
