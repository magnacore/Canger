// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Previews;

/// <summary>
/// The state of a file at the moment a preview of it was made.
/// </summary>
/// <remarks>
/// <para>
/// Modification time and size together, which is what every tool that has to notice a file
/// changing settles on. Neither alone is enough: an edit that keeps a file the same length leaves
/// the size untouched, and a copy that preserves timestamps leaves the time untouched.
/// </para>
/// <para>
/// Not a content hash. Reading a file to decide whether to read it is no saving at all, and the
/// preview is regenerated from the file in any case — the stamp only has to be different when the
/// file is different, not to prove that it is the same.
/// </para>
/// </remarks>
/// <param name="ModifiedTicks">When the file was last written.</param>
/// <param name="Size">How large it was.</param>
public readonly record struct PreviewStamp(long ModifiedTicks, long Size)
{
    /// <summary>Stands for a file that could not be examined.</summary>
    /// <remarks>
    /// Never equal to a stamp taken from a real file, so a preview is not served for something
    /// that has since been deleted or become unreadable.
    /// </remarks>
    public static PreviewStamp Unknown => new(long.MinValue, long.MinValue);

    /// <summary>Takes a stamp from a file's metadata.</summary>
    /// <param name="status">The file, or <see langword="null"/> when it could not be examined.</param>
    /// <returns>The stamp.</returns>
    public static PreviewStamp Of(FileStatus? status) =>
        status is { } present
            ? new PreviewStamp(present.ModifyTime.UtcTicks, present.Size)
            : Unknown;
}
