// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Processes;

/// <summary>
/// A program running alongside the interface, rather than in front of it.
/// </summary>
/// <remarks>
/// <para>
/// The two ways of running a program that <see cref="IProcessRunner"/> already offers both stop
/// the world: one hands over the terminal, the other waits for the output. Neither suits work
/// that takes a while and has nothing to say — unpacking an archive, transcoding a video — which
/// should go on in the background while the browser stays usable.
/// </para>
/// <para>
/// The handle is deliberately poll-shaped rather than <c>async</c>, because its consumer is
/// <see cref="Tasks.CommandTask"/>, which is a step-at-a-time <see cref="Tasks.ILoadable"/> on the
/// task queue. Ranger arrives at the same shape for the same reason: its <c>CommandLoader</c> is
/// a generator that <c>select</c>s with a short timeout and yields (<c>core/loader.py:162-278</c>).
/// </para>
/// </remarks>
public interface IBackgroundProcess : IDisposable
{
    /// <summary>Whether the program has finished.</summary>
    bool HasExited { get; }

    /// <summary>Its exit code, or <see langword="null"/> while it is still running.</summary>
    int? ExitCode { get; }

    /// <summary>What it has written to standard output so far.</summary>
    string StandardOutput { get; }

    /// <summary>What it has written to standard error so far.</summary>
    string StandardError { get; }

    /// <summary>Waits a little for the program to finish.</summary>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>Whether it finished within that time.</returns>
    /// <remarks>
    /// Waiting rather than spinning is what keeps a background command from costing a core.
    /// The queue asks for a step, the step waits briefly and yields, and the interface goes round
    /// again — which is ranger's <c>select(..., 0.03)</c> written the other way up.
    /// </remarks>
    bool WaitForExit(TimeSpan timeout);

    /// <summary>Ends the program, and anything it started.</summary>
    void Kill();
}
