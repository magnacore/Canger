// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;

namespace Canger.Core.Tasks;

/// <summary>
/// An external command run on the task queue instead of in front of the interface.
/// </summary>
/// <remarks>
/// <para>
/// This is ranger's <c>CommandLoader</c> (<c>core/loader.py:162-278</c>). It exists so that work
/// with a long silence in the middle — unpacking an archive, transcoding a file — appears in the
/// task view with a description and a spinner, can be cancelled from there, and leaves the
/// browser usable throughout. The alternative, handing the program the terminal, means the user
/// stares at a blank screen until it finishes and cannot do anything else meanwhile.
/// </para>
/// <para>
/// Anything the program writes to standard error is reported when it ends, because an archiver
/// asked for the wrong password says so there and nowhere else. Standard output is kept in
/// <see cref="Output"/> for callers that want it — ranger's <c>read=True</c> and its
/// <c>stdout_buffer</c>.
/// </para>
/// </remarks>
public sealed class CommandTask : ILoadable
{
    /// <summary>
    /// How often the program is looked in on while it runs.
    /// </summary>
    /// <remarks>
    /// The queue's own slice, and ranger's <c>select</c> timeout (<c>core/loader.py:239</c>).
    /// Unlike ranger's, the waiting is not done here — see <see cref="Idle"/>.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TaskQueue.DefaultSlice;

    private readonly IProcessRunner _runner;
    private readonly ProcessRequest _request;
    private readonly Action<string, bool>? _notify;
    private readonly Action<CommandTask>? _finished;
    private IBackgroundProcess? _process;

    /// <summary>Creates a queued command.</summary>
    /// <param name="runner">Starts the program.</param>
    /// <param name="request">What to run, and where.</param>
    /// <param name="description">What the task view calls it, as ranger's <c>descr</c> does.</param>
    /// <param name="notify">Reports failures, taking the message and whether it is an error.</param>
    /// <param name="finished">
    /// Run once the program has ended, whatever its outcome — ranger's <c>after</c> signal, which
    /// the archive plugin uses to reload the directory the files landed in.
    /// </param>
    public CommandTask(IProcessRunner runner, ProcessRequest request, string description,
                       Action<string, bool>? notify = null,
                       Action<CommandTask>? finished = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        _runner = runner;
        _request = request;
        _notify = notify;
        _finished = finished;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>
    /// Always <see langword="null"/>: an outside program does not say how far along it is.
    /// </summary>
    /// <remarks>
    /// The task view shows a spinner rather than a bar for these, which is the honest rendering —
    /// ranger does the same, its <c>CommandLoader</c> never setting a percentage.
    /// </remarks>
    public double? Progress => null;

    /// <summary>
    /// The poll interval while the program runs, and nothing once it has ended.
    /// </summary>
    /// <remarks>
    /// This task never has work of its own — it is waiting on another process — so the main loop
    /// should spend that time watching the keyboard. Saying so is what keeps the interface
    /// answering at once while an archive unpacks.
    /// </remarks>
    public TimeSpan Idle =>
        _process is null || _process.HasExited ? TimeSpan.Zero : PollInterval;

    /// <summary>What the program printed to standard output.</summary>
    public string Output => _process?.StandardOutput ?? string.Empty;

    /// <summary>Its exit code, or <see langword="null"/> until it has finished.</summary>
    public int? ExitCode => _process?.ExitCode;

    /// <inheritdoc />
    public IEnumerator<Unit> Steps()
    {
        _process = _runner.StartInBackground(_request);

        if (_process is null)
        {
            _notify?.Invoke($"{Description}: could not start", true);
            _finished?.Invoke(this);
            yield break;
        }

        // A poll, not a wait. Blocking here would block the whole interface, because the queue
        // is pumped from the same loop that reads the keyboard — measured at a 40 ms worst-case
        // key latency while an archive unpacked. The waiting is delegated to that loop through
        // `Idle`, which watches for a keystroke at the same time.
        while (!_process.WaitForExit(TimeSpan.Zero))
        {
            yield return Unit.Value;
        }

        Report();
        _finished?.Invoke(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Cancelling the task in the view has to end the program too, or the work carries on
        // invisibly with nothing left to stop it.
        _process?.Kill();
        _process?.Dispose();
        _process = null;
    }

    /// <summary>Passes on whatever the program complained about.</summary>
    private void Report()
    {
        if (_process is null || _notify is null)
        {
            return;
        }

        string error = _process.StandardError.Trim();

        if (error.Length > 0)
        {
            _notify(error, true);
            return;
        }

        // A failure with nothing on stderr would otherwise pass in silence.
        if (_process.ExitCode is { } code and not 0)
        {
            _notify($"{Description}: exited with {code}", true);
        }
    }
}
