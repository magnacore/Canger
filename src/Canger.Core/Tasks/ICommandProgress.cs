// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tasks;

/// <summary>
/// How far along an external command is, for the commands that can be made to say.
/// </summary>
/// <remarks>
/// <para>
/// A queued command shows a spinner because a program Canger did not write does not report itself.
/// Some can be persuaded to: <c>tar</c> will echo a byte count at intervals if asked, and any
/// archiver's output file can be watched growing. Canger stays ignorant of which is which — it
/// takes a source of numbers, and whoever builds the command line decides where they come from.
/// </para>
/// <para>
/// The distinction between the two properties is the whole design. <see cref="Total"/> is what
/// separates a percentage from a byte count: with both, the task view draws a bar and the status
/// bar's overall figure includes the work; with only <see cref="Completed"/>, the honest answer is
/// "142 MB so far" and no bar at all. Inventing a total from a guessed compression ratio would
/// produce a bar that lies, which is worse than a spinner.
/// </para>
/// </remarks>
public interface ICommandProgress
{
    /// <summary>What the work amounts to, or <see langword="null"/> when that is not knowable.</summary>
    long? Total { get; }

    /// <summary>How much is done, or <see langword="null"/> until the command has said anything.</summary>
    long? Completed { get; }

    /// <summary>Whether a line of the command's output is this source's own reporting.</summary>
    /// <param name="line">One line of standard error.</param>
    /// <returns><see langword="true"/> when it carries progress rather than a complaint.</returns>
    /// <remarks>
    /// Asking a program to report progress makes it talk, and it talks on standard error, which is
    /// where a queued command's failures are read from. Without this every archive would end by
    /// showing its own checkpoints as an error — the mechanism reporting itself as a fault.
    /// </remarks>
    bool Reports(string line) => false;

    /// <summary>Which of the command's streams this source reads.</summary>
    /// <remarks>
    /// Standard error by default, because that is where a program asked to report progress
    /// usually talks — tar's checkpoints do. Info-ZIP does not: both <c>zip</c> and <c>unzip</c>
    /// name each entry on standard output as they go, so a source reading only the error stream
    /// would watch a program that never stops talking and never hear a word of it.
    /// </remarks>
    bool ReadsStandardOutput => false;

    /// <summary>Offers what the command has written to its chosen stream so far.</summary>
    /// <param name="reported">
    /// Everything it has written to that stream, from the beginning — standard error unless
    /// <see cref="ReadsStandardOutput"/> says otherwise.
    /// </param>
    /// <remarks>
    /// The whole text each time rather than the new part, so a source parses the last thing it
    /// recognises and holds no position of its own. A source that watches a file ignores it.
    /// Called once per queue slice, which is every 30 ms at most.
    /// </remarks>
    void Update(string reported);
}
