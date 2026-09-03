// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
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
public sealed class CommandTask : ILoadable, IReportsBytes, ISizedWork
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
    private readonly ICommandProgress? _progress;
    private IBackgroundProcess? _process;

    /// <summary>How it ended, kept because the handle does not outlive the job.</summary>
    /// <remarks>
    /// <see cref="TaskQueue.Stop"/> disposes the work the moment it finishes, which clears
    /// <see cref="_process"/> — so reading the exit code through it afterwards always found
    /// nothing, and a completed archive sat at 94% for ever. The unit tests missed it by driving
    /// <see cref="Steps"/> directly, where no queue is there to dispose anything.
    /// </remarks>
    private int? _endedWith;

    /// <summary>The measuring walk, created once.</summary>
    private IEnumerator<Unit>? _sizing;

    /// <summary>How fast the command is getting through its work.</summary>
    /// <remarks>
    /// The same smoothed rate a copy uses, fed from the readings the progress source collects.
    /// Timed from the first reading rather than from when the job was queued, so waiting behind
    /// another task does not count as time spent working.
    /// </remarks>
    private readonly TransferRate _rate = new();

    /// <summary>Where the clock comes from, so an estimate can be tested without waiting.</summary>
    private readonly TimeProvider _time;

    private DateTimeOffset? _startedReporting;
    private long _lastReading;

    /// <summary>What had already been counted when the first reading arrived.</summary>
    /// <remarks>
    /// Subtracted from every later reading, because the clock starts at that first reading too.
    /// Without it the bytes counted before the timing began were divided by a stretch of time that
    /// excluded them, and the rate came out several times too high — badly enough that an
    /// extraction with half a minute left showed <c>00:00</c> throughout.
    /// </remarks>
    private long _baseline;

    /// <summary>Creates a queued command.</summary>
    /// <param name="runner">Starts the program.</param>
    /// <param name="request">What to run, and where.</param>
    /// <param name="description">What the task view calls it, as ranger's <c>descr</c> does.</param>
    /// <param name="notify">Reports failures, taking the message and whether it is an error.</param>
    /// <param name="finished">
    /// Run once the program has ended, whatever its outcome — ranger's <c>after</c> signal, which
    /// the archive plugin uses to reload the directory the files landed in.
    /// </param>
    /// <param name="progress">
    /// Where a byte count comes from, for a command that can be made to report one. Omitted, the
    /// task shows a spinner as before.
    /// </param>
    /// <param name="time">
    /// Where the clock comes from. Defaults to the system clock; a test passes its own so that a
    /// rate and an estimate can be checked against known intervals rather than against however
    /// long the test happened to take.
    /// </param>
    public CommandTask(IProcessRunner runner, ProcessRequest request, string description,
                       Action<string, bool>? notify = null,
                       Action<CommandTask>? finished = null,
                       ICommandProgress? progress = null,
                       TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        _runner = runner;
        _request = request;
        _notify = notify;
        _finished = finished;
        _progress = progress;
        _time = time ?? TimeProvider.System;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>
    /// How far along it is, when the command was set up to say.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> unless a progress source was given <em>and</em> it knows the total —
    /// a spinner, which is what ranger's <c>CommandLoader</c> always shows. A source that reports
    /// only a byte count leaves this null on purpose; see <see cref="Transferred"/>.
    /// </remarks>
    public double? Progress =>
        _progress is not { Total: { } total, Completed: { } done } || total <= 0
            ? null

            // A command that succeeded is finished, whatever its last reading said. tar reports at
            // intervals and says nothing about the tail after the final one, so a twenty-four
            // megabyte archive ended at 85% and stayed there — a bar that stops short reads as a
            // job that stalled. A command that *failed* keeps its last honest figure.
            : _endedWith == 0
                ? 1
                : Math.Clamp((double)done / total, 0, 1);

    /// <summary>
    /// The bytes written or read so far, when that is all the command will say.
    /// </summary>
    /// <remarks>
    /// The honest half-answer for an archiver that reports nothing: the file can be watched
    /// growing, but how large it will end up depends on a compression ratio nobody knows. The task
    /// view shows this figure where it would otherwise have nothing but a spinner.
    /// </remarks>
    public long? Transferred => _progress?.Completed;

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

    /// <summary>
    /// What the program wrote to standard error.
    /// </summary>
    /// <remarks>
    /// Reported to the user when the job ends, and kept here as well for a caller that wants to
    /// act on it — a mount refused for want of authorisation says so here and nowhere else.
    /// </remarks>
    public string Error => _process?.StandardError ?? string.Empty;

    /// <summary>Its exit code, or <see langword="null"/> until it has finished.</summary>
    public int? ExitCode => _process?.ExitCode;

    /// <summary>The measuring half, for a command whose total has to be worked out.</summary>
    /// <remarks>
    /// Only a <see cref="MeasuredProgress"/> has anything to measure. Everything else is already
    /// sized — either it knows its total from the start or it never will — so it reports itself
    /// finished at once and the queue moves on.
    /// </remarks>
    private MeasuredProgress? Measuring => _progress as MeasuredProgress;

    /// <inheritdoc />
    public bool IsSized => Measuring is not { IsMeasured: false };

    /// <inheritdoc />
    public IEnumerator<Unit> SizingSteps() =>
        _sizing ??= Measuring?.Measure() ?? Enumerable.Empty<Unit>().GetEnumerator();

    /// <inheritdoc />
    public long? TotalBytes => _progress?.Total;

    /// <inheritdoc />
    public long? RemainingBytes =>
        _progress is { Total: { } total, Completed: { } done } ? Math.Max(total - done, 0) : null;

    /// <inheritdoc />
    /// <remarks>
    /// This was left unreported at first, on the grounds that the bytes counted are what has been
    /// read while the time goes on compressing them. That was too cautious: tar reads at whatever
    /// pace the compressor allows, so the rate at which the input is consumed is exactly what
    /// predicts when the input will run out — which is when the job ends.
    /// </remarks>
    public double? BytesPerSecond => _rate.BytesPerSecond;

    /// <summary>How much longer it should take, once there is enough to say.</summary>
    /// <remarks>
    /// Needs a total, so it appears for an archive being measured or unpacked and not for one
    /// merely watched growing — the same line the percentage draws.
    /// </remarks>
    public TimeSpan? Estimate =>
        RemainingBytes is { } remaining ? _rate.Estimate(remaining) : null;

    /// <inheritdoc />
    public string Subject => Description;

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
            // Offered the whole of standard error each time. A source that parses it keeps no
            // position of its own, and one that watches a file ignores it.
            _progress?.Update(_process.StandardError);
            RecordRate();
            yield return Unit.Value;
        }

        // Once more, so a command that finishes between two polls still ends at its final figure
        // rather than at whatever the last slice happened to catch.
        _progress?.Update(_process.StandardError);
        _endedWith = _process.ExitCode;

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

    /// <summary>Feeds the rate from whatever the source has counted so far.</summary>
    /// <remarks>
    /// Timed from the first reading, not from the start of the job: an archiver says nothing until
    /// its first checkpoint, and counting that silence as working time would halve the rate and
    /// double the estimate for as long as it took to arrive.
    /// </remarks>
    private void RecordRate()
    {
        if (_progress?.Completed is not { } completed || completed <= 0)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();

        if (_startedReporting is not { } since)
        {
            _startedReporting = now;
            _lastReading = completed;
            _baseline = completed;
            return;
        }

        if (completed != _lastReading)
        {
            _lastReading = completed;

            // Both halves measured from the same instant. Feeding the cumulative count against
            // time started at the first reading credited the command with bytes it had moved
            // before the stopwatch began.
            _rate.Record(completed - _baseline, now - since);
        }
    }

    /// <summary>Passes on whatever the program complained about.</summary>
    private void Report()
    {
        if (_process is null || _notify is null)
        {
            return;
        }

        // The progress source's own reporting is not a complaint. Asking tar to say how far it has
        // got makes it write to standard error, which is exactly where failures are read from.
        string error = string.Join(
            '\n',
            _process.StandardError
                    .Split('\n')
                    .Where(line => _progress?.Reports(line) != true))
            .Trim();

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
