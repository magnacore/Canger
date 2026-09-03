// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Tasks;

/// <summary>
/// Watches the file a command is writing.
/// </summary>
/// <remarks>
/// <para>
/// The fallback for an archiver that reports nothing — <c>7z</c>, <c>zip</c>, <c>rar</c>. One
/// <c>stat</c> per queue slice, which is less than the browser already spends re-reading the rows
/// on screen.
/// </para>
/// <para>
/// <see cref="Total"/> is deliberately <see langword="null"/>. What can be seen is the archive
/// growing, and how large it will end up depends on a compression ratio nobody knows in advance;
/// dividing by the size of the input would give a bar that stops short of the end on every
/// ordinary archive and overshoots on an incompressible one. So this reports a byte count and no
/// percentage, and the task view says "142 MB" rather than drawing a bar it cannot justify.
/// </para>
/// </remarks>
/// <param name="fileSystem">Used to stat the file.</param>
/// <param name="path">The archive being written.</param>
public sealed class GrowingFileProgress(IFileSystem fileSystem, string path) : ICommandProgress
{
    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly string _path =
        string.IsNullOrEmpty(path) ? throw new ArgumentException("A path is required.", nameof(path))
                                   : path;

    private long? _completed;

    /// <inheritdoc />
    public long? Total => null;

    /// <inheritdoc />
    public long? Completed => _completed;

    /// <inheritdoc />
    public void Update(string reported)
    {
        // The file is the source; what the program says is not read.
        long? size = _fileSystem.GetStatus(_path, followSymbolicLinks: true)?.Size;

        // Never backwards: an archiver that truncates and rewrites its output would otherwise
        // make the figure jump about.
        if (size is { } bytes && bytes >= (_completed ?? 0))
        {
            _completed = bytes;
        }
    }
}
