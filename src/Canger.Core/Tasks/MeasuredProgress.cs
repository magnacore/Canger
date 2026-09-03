// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Tasks;

/// <summary>
/// A progress source that works out its own total by measuring what the command was given.
/// </summary>
/// <remarks>
/// <para>
/// Compressing a folder knew how far it had got and not how far there was to go, so it showed
/// <c>109 M</c> and no bar. The total is the size of the tree, and measuring one means walking it
/// — which is why nothing did. The task queue already solves that for copies: it measures a job
/// while the job waits, in three-millisecond slices, so the walk never holds up the interface.
/// This makes an archive the same kind of job.
/// </para>
/// <para>
/// The total is revised as the walk goes rather than only at the end, so a percentage appears
/// within a frame or two of the command starting rather than when the measuring finishes.
/// </para>
/// </remarks>
/// <param name="inner">Where the count of what is done comes from.</param>
/// <param name="fileSystem">Used to walk the tree.</param>
/// <param name="paths">What the command was given.</param>
public sealed class MeasuredProgress(
    ICommandProgress inner, IFileSystem fileSystem, IReadOnlyList<string> paths)
    : IMeasurableProgress
{
    private readonly ICommandProgress _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly IReadOnlyList<string> _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));

    private long _measured;

    /// <summary>Whether the walk has finished.</summary>
    public bool IsMeasured { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Withheld until the walk is done. A total that is still growing would make the percentage
    /// fall as the measuring caught up, and a bar that goes backwards reads as a fault — the
    /// mistake <see cref="TaskQueue.OverallProgress"/> was reverted for once already.
    /// </remarks>
    public long? Total => IsMeasured ? _measured : null;

    /// <inheritdoc />
    public long? Completed => _inner.Completed;

    /// <inheritdoc />
    public bool Reports(string line) => _inner.Reports(line);

    /// <inheritdoc />
    /// <remarks>
    /// The wrapped source's choice, not one of its own. Deciding for it would have silently
    /// switched a zip's manifest back to the stream Info-ZIP never writes to.
    /// </remarks>
    public bool ReadsStandardOutput => _inner.ReadsStandardOutput;

    /// <inheritdoc />
    public void Update(string reported) => _inner.Update(reported);

    /// <summary>Walks what the command was given, a file at a time.</summary>
    /// <returns>A step per file, for the queue to spend a slice on.</returns>
    public IEnumerator<Unit> Measure()
    {
        foreach (string path in _paths)
        {
            foreach (long size in TreeSize.Sizes(_fileSystem, path))
            {
                _measured += size;
                yield return Unit.Value;
            }
        }

        IsMeasured = true;
    }
}
