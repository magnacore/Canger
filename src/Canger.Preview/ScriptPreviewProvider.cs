// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Previews;

namespace Canger.Preview;

/// <summary>
/// Produces previews with the external script, remembering what it produced.
/// </summary>
/// <remarks>
/// The cheap decisions are made here rather than in the script: a directory is listed directly,
/// a file too large to be worth reading is skipped, and previews turned off cost nothing. Only
/// what is left reaches the script.
/// </remarks>
/// <param name="fileSystem">Where files are examined.</param>
/// <param name="runner">Runs the preview script.</param>
/// <param name="cache">Remembers what the script produced.</param>
/// <param name="onGenerated">
/// Called when a preview finishes on the worker, so the caller can redraw. When absent, previews
/// are produced on the calling thread — which is what the tests want and what a one-shot
/// listing wants, but not what an interactive browser wants.
/// </param>
public sealed class ScriptPreviewProvider(
    IFileSystem fileSystem, ScopeScriptRunner runner, PreviewCache? cache = null,
    Action? onGenerated = null)
    : IPreviewProvider, IDisposable
{
    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly ScopeScriptRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly PreviewCache _cache = cache ?? new PreviewCache();

    /// <summary>Whether files are previewed at all.</summary>
    public bool PreviewFiles { get; set; } = true;

    /// <summary>Whether directories are previewed.</summary>
    public bool PreviewDirectories { get; set; } = true;

    /// <summary>Whether the external script is used, or files are simply read as text.</summary>
    public bool UseScript { get; set; } = true;

    /// <summary>
    /// Largest file to preview, in bytes. Zero means no limit.
    /// </summary>
    /// <remarks>
    /// Moving the cursor over a very large file should not stall the browser while something
    /// reads all of it.
    /// </remarks>
    public long MaximumSize { get; set; }

    /// <summary>Whether images may be produced.</summary>
    public bool ImagesEnabled
    {
        get => _runner.ImagesEnabled;
        set => _runner.ImagesEnabled = value;
    }

    /// <inheritdoc />
    public PreviewResult Preview(string path, PreviewSize size,
                                 CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (size.Width <= 0 || size.Height <= 0)
        {
            return PreviewResult.None;
        }

        // Taken once and used for both the lookup and, if it comes to it, the storing — so the
        // entry records the file as it was when generation started rather than as it was after.
        PreviewStamp stamp = PreviewStamp.Of(
            _fileSystem.GetStatus(path, followSymbolicLinks: true));

        if (_cache.Find(path, size.Width, size.Height, stamp) is { } cached)
        {
            return cached;
        }

        // Generating can take a third of a second for a video — a thumbnail is a whole ffmpeg
        // run — and doing that on the drawing path means the browser stops dead every time the
        // cursor lands on one. So the answer is prepared on a worker and the row is left blank
        // until it arrives, which is what makes moving through a directory of films feel the
        // same as moving through a directory of text.
        if (onGenerated is null)
        {
            PreviewResult immediate = Generate(path, size, cancellationToken);
            _cache.Store(path, size.Width, size.Height, immediate, stamp);
            return immediate;
        }

        Request(path, size, stamp);

        // Not `None`: the caller has to be able to tell "there is nothing here" from "ask again in
        // a moment", because the preview column collapses on the first and must not on the second.
        return PreviewResult.Pending;
    }

    private readonly Lock _pending = new();
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Starts generating a preview, unless one is already being generated for it.</summary>
    private void Request(string path, PreviewSize size, PreviewStamp stamp)
    {
        // Keyed by size as well as path: a resized terminal wants a different preview, and the
        // cache is keyed the same way.
        string key = $"{size.Width}x{size.Height}:{path}";

        lock (_pending)
        {
            // A redraw asks about the same row repeatedly while the answer is being prepared;
            // without this every frame would start another ffmpeg.
            if (!_inFlight.Add(key))
            {
                return;
            }
        }

        _ = Task.Run(() =>
        {
            try
            {
                PreviewResult result = Generate(path, size, _shutdown.Token);
                _cache.Store(path, size.Width, size.Height, result, stamp);
            }
            catch (OperationCanceledException)
            {
                // Shutting down; nothing to store and nothing to report.
                return;
            }
            finally
            {
                lock (_pending)
                {
                    _inFlight.Remove(key);
                }
            }

            onGenerated?.Invoke();
        }, CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    /// <inheritdoc />
    public void Invalidate(string path) => _cache.Invalidate(path);

    /// <summary>Forgets every preview, for the <c>:reset_previews</c> command.</summary>
    public void InvalidateAll() => _cache.Clear();

    private PreviewResult Generate(string path, PreviewSize size,
                                   CancellationToken cancellationToken)
    {
        FileStatus? status = _fileSystem.GetStatus(path, followSymbolicLinks: true);

        if (status is null)
        {
            return PreviewResult.None;
        }

        if (status.Value.IsDirectory)
        {
            return PreviewDirectories ? PreviewDirectory(path, size) : PreviewResult.None;
        }

        if (!PreviewFiles || status.Value.Kind != FileKind.Regular)
        {
            return PreviewResult.None;
        }

        if (MaximumSize > 0 && status.Value.Size > MaximumSize)
        {
            return PreviewResult.None;
        }

        if (UseScript && _runner.IsUsable)
        {
            return _runner.Run(path, size, cancellationToken);
        }

        return PreviewPlainText(path, size);
    }

    /// <summary>
    /// Previews a directory by listing it.
    /// </summary>
    /// <remarks>
    /// Handled here rather than by the script because the browser already knows how to read a
    /// directory, and running a shell script to produce a listing it could produce itself would
    /// be slower and less consistent with the columns beside it.
    /// </remarks>
    private PreviewResult PreviewDirectory(string path, PreviewSize size)
    {
        try
        {
            IReadOnlyList<Core.FileSystem.DirectoryEntry> entries = _fileSystem.ListDirectory(path);

            if (entries.Count == 0)
            {
                return new PreviewResult(PreviewKind.Text, PreviewFit.AnySize, "empty");
            }

            IEnumerable<string> names = entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(size.Height)
                .Select(e => e.IsDirectory ? e.Name + "/" : e.Name);

            return new PreviewResult(PreviewKind.Text, PreviewFit.AnyWidth,
                                     string.Join("\n", names));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PreviewResult(PreviewKind.Text, PreviewFit.AnySize, "not accessible");
        }
    }

    /// <summary>Shows a file's first lines, when there is no script to do better.</summary>
    private PreviewResult PreviewPlainText(string path, PreviewSize size)
    {
        byte[] prefix = _fileSystem.ReadFilePrefix(path, 32 * 1024);

        if (prefix.Length == 0)
        {
            return PreviewResult.None;
        }

        // A control character outside the handful that appear in text means this is not text,
        // and showing it would fill the column with rubbish and possibly confuse the terminal.
        foreach (byte value in prefix)
        {
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0C or 0x0D))
            {
                return PreviewResult.None;
            }
        }

        string text = System.Text.Encoding.UTF8.GetString(prefix);
        string[] lines = text.Split('\n');

        return new PreviewResult(
            PreviewKind.Text,
            PreviewFit.AnyWidth,
            string.Join("\n", lines.Take(size.Height)));
    }
}
