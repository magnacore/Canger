// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;

namespace Canger.Core.FileOperations;

/// <summary>
/// Says what became of a run of background work, once all of it has stopped.
/// </summary>
/// <remarks>
/// <para>
/// One message for the lot, because there is only ever one message. Each job used to be reported
/// on its own and each report replaced the last, so what the user saw was whatever happened to
/// finish last — and when a transfer that had lost a file finished alongside one that had not,
/// the problem was reported and then unreported within the same frame. Nothing was lost but the
/// telling, which is quite bad enough: the whole purpose of the notice is that a file which did
/// not arrive should be noticed.
/// </para>
/// <para>
/// Trouble therefore outranks everything. A run that had any problem in it says so, and says how
/// many; only a run that was entirely clean gets to say <c>done</c>.
/// </para>
/// </remarks>
public static class FinishedWork
{
    /// <summary>
    /// Sums up a run of finished jobs.
    /// </summary>
    /// <param name="finished">The jobs that have stopped, however they stopped.</param>
    /// <returns>
    /// What to tell the user and whether it is bad news, or <see langword="null"/> when there is
    /// nothing worth saying — a run of work that moved no files has nothing to report.
    /// </returns>
    public static (string Message, bool IsError)? Describe(IReadOnlyList<QueuedTask> finished)
    {
        ArgumentNullException.ThrowIfNull(finished);

        List<string> problems = [];
        bool cancelled = false;
        bool transferred = false;
        int files = 0;
        int reflinked = 0;
        int renamed = 0;
        int copied = 0;

        foreach (QueuedTask task in finished)
        {
            if (task.State == TaskState.Failed && task.Error is { } error)
            {
                problems.Add($"{Name(task)}: {error.Message}");
            }

            if (task.State == TaskState.Cancelled)
            {
                cancelled = true;
            }

            if (task.Work is not CopyJob job)
            {
                continue;
            }

            transferred = true;
            problems.AddRange(job.Errors);

            files += job.Progress.CompletedFiles;
            reflinked += job.Progress.ReflinkedFiles;
            renamed += job.Progress.RenamedFiles;
            copied += job.Progress.CopiedFiles;
        }

        if (problems.Count > 0)
        {
            return (problems.Count == 1
                        ? problems[0]
                        : $"{problems.Count} problems, first: {problems[0]}",
                    true);
        }

        // Said plainly rather than as "done", which is what a stopped transfer used to report:
        // the files it had already moved are still there, and a user who pressed abort should be
        // told what got through rather than congratulated.
        if (cancelled)
        {
            return ($"stopped: {Count(files)} copied", false);
        }

        if (!transferred)
        {
            return null;
        }

        string strategies = DescribeStrategies(reflinked, renamed, copied);

        return (strategies.Length > 0
                    ? $"done: {Count(files)}, {strategies}"
                    : $"done: {Count(files)}",
                false);
    }

    /// <summary>Names a job without its running figures, which are over.</summary>
    private static string Name(QueuedTask task) =>
        task.Work is ISizedWork sized ? sized.Subject : task.Description;

    /// <summary>Counts files, in the singular when there is one of them.</summary>
    private static string Count(int files) => files == 1 ? "1 file" : $"{files} files";

    /// <summary>How the files were handled, when that is worth saying.</summary>
    private static string DescribeStrategies(int reflinked, int renamed, int copied) =>
        CopyProgress.DescribeStrategies(reflinked, renamed, copied);
}
