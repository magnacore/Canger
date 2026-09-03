// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tasks;

/// <summary>
/// A progress source that has to look at the filesystem before it can say what the work amounts to.
/// </summary>
/// <remarks>
/// Measuring means walking a tree, and a tree may hold a million files, so it cannot be done on
/// the spot without stopping the interface. The task queue already solves this for copies: it
/// spends three-millisecond slices on a job's measuring while the job waits. This is the seam it
/// drives, named as an interface so that more than one kind of source can use it — a total to
/// count towards, and a manifest of what each name an archiver announces is worth.
/// </remarks>
public interface IMeasurableProgress : ICommandProgress
{
    /// <summary>Whether the walk has finished.</summary>
    bool IsMeasured { get; }

    /// <summary>Walks what the command was given, a file at a time.</summary>
    /// <returns>A step per file, for the queue to spend a slice on.</returns>
    IEnumerator<Unit> Measure();
}
