// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Model;

/// <summary>
/// A file in the browser: everything <see cref="FsNode"/> provides, plus what is needed to decide
/// whether and how it can be previewed.
/// </summary>
public sealed class FileNode : FsNode
{
    /// <summary>How much of a file is read when deciding whether it is binary.</summary>
    /// <remarks>
    /// Ranger reads the same 256 bytes (<c>container/file.py:16</c>). It is enough to catch the
    /// control characters that mark a binary file without reading anything large.
    /// </remarks>
    public const int SniffLength = 256;

    private readonly IFileSystem _fileSystem;
    private bool? _isBinary;

    /// <summary>Creates a file node.</summary>
    /// <param name="fileSystem">Used to sniff content when a preview is considered.</param>
    /// <param name="path">Absolute path.</param>
    /// <param name="status">Metadata following symbolic links.</param>
    /// <param name="linkStatus">Metadata of the link itself, when reached through one.</param>
    /// <param name="relativeToPath">The directory the display path is measured from.</param>
    public FileNode(IFileSystem fileSystem, string path, FileStatus? status,
                    FileStatus? linkStatus = null, string? relativeToPath = null)
        : base(path, status, linkStatus, relativeToPath) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>
    /// Whether the file looks like binary content rather than text.
    /// </summary>
    /// <remarks>
    /// Judged the way ranger judges it (<c>container/file.py:66-69</c>): a file is binary if its
    /// first bytes contain a control character outside the handful that appear in ordinary text.
    /// The result is computed once and kept.
    /// </remarks>
    public bool IsBinary => _isBinary ??= SniffIsBinary();

    /// <summary>
    /// Whether this file is eligible for a preview at all.
    /// </summary>
    /// <remarks>
    /// This is the cheap part of the decision, made before any preview script runs: special files
    /// have nothing to show, unreadable files cannot be read, and oversized files are skipped so
    /// that moving the cursor over a huge file does not stall.
    /// </remarks>
    /// <param name="maximumSize">
    /// Largest file to consider, from the <c>preview_max_size</c> setting. Zero means no limit.
    /// </param>
    /// <returns><see langword="true"/> when a preview may be attempted.</returns>
    public bool CanPreview(long maximumSize = 0)
    {
        if (!IsAccessible || Status is not { Kind: FileKind.Regular })
        {
            return false;
        }

        return maximumSize <= 0 || Size <= maximumSize;
    }

    private bool SniffIsBinary()
    {
        byte[] prefix = _fileSystem.ReadFilePrefix(Path, SniffLength);

        foreach (byte value in prefix)
        {
            // Tab, newline, carriage return and form feed are ordinary in text; the rest of the
            // low control range is not.
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0C or 0x0D))
            {
                return true;
            }
        }

        return false;
    }
}
