// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;

namespace Canger.Core.FileOperations;

/// <summary>
/// How far a copy has got, and how much longer it is likely to take.
/// </summary>
/// <remarks>
/// Two byte counts are kept rather than one. The first drives the percentage and counts
/// everything the user asked to copy, however it was copied. The second drives the time estimate
/// and counts only what actually moved, because a reflinked file contributes to the first and
/// nothing at all to the second.
/// </remarks>
public sealed class CopyProgress(long totalBytes, int totalFiles)
{
    private readonly Stopwatch _clock = new();
    private readonly TransferRate _rate = new();

    private TimeSpan _lastSampleAt = TimeSpan.Zero;
    private long _transferredAtLastSample;

    /// <summary>How many bytes the whole operation covers.</summary>
    public long TotalBytes { get; private set; } = totalBytes;

    /// <summary>How many files the whole operation covers.</summary>
    public int TotalFiles { get; } = totalFiles;

    /// <summary>Bytes accounted for, however they were copied.</summary>
    public long CompletedBytes { get; private set; }

    /// <summary>Bytes that genuinely moved, which is what the rate is measured from.</summary>
    public long TransferredBytes { get; private set; }

    /// <summary>Files finished.</summary>
    public int CompletedFiles { get; private set; }

    /// <summary>Files the filesystem shared rather than duplicated.</summary>
    public int ReflinkedFiles { get; private set; }

    /// <summary>Files whose data was copied.</summary>
    public int CopiedFiles { get; private set; }

    /// <summary>Files moved by renaming, which copies nothing.</summary>
    public int RenamedFiles { get; private set; }

    /// <summary>What is being worked on now.</summary>
    public string CurrentFile { get; private set; } = string.Empty;

    /// <summary>How far along, from 0 to 1.</summary>
    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)CompletedBytes / TotalBytes, 0, 1)
        : CompletedFiles >= TotalFiles ? 1 : 0;

    /// <summary>Throughput, or <see langword="null"/> when nothing has moved yet.</summary>
    public double? BytesPerSecond => _rate.BytesPerSecond;

    /// <summary>
    /// How long the operation has been running.
    /// </summary>
    /// <remarks>
    /// From the first file it touched, not from when it was set up. A transfer is built the
    /// moment the user presses paste and may then sit in the queue behind another one for
    /// minutes; timing from construction counted that wait as time spent transferring. The rate
    /// is measured against this clock, so the first sample of a job that had waited its turn
    /// divided real bytes by the whole wait — a second film starting behind a first reported
    /// 387 k/s and an hour remaining, for as long as it took the average to recover.
    /// </remarks>
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>
    /// How much longer it is likely to take.
    /// </summary>
    /// <remarks>
    /// Based on the bytes still expected to move. Work already known to be instant is excluded,
    /// which is what stops a run of reflinks making the estimate meaningless.
    /// </remarks>
    public TimeSpan? Estimate => _rate.Estimate(Math.Max(TotalBytes - CompletedBytes, 0));

    /// <summary>Notes which file is being worked on.</summary>
    /// <param name="path">The file.</param>
    /// <remarks>
    /// Where the clock starts, this being the first moment the transfer is doing anything rather
    /// than waiting to be allowed to.
    /// </remarks>
    public void BeginFile(string path)
    {
        CurrentFile = path;

        if (!_clock.IsRunning)
        {
            _clock.Start();
        }
    }

    /// <summary>
    /// Records progress within a file whose data is being copied.
    /// </summary>
    /// <param name="bytes">How many more bytes have moved.</param>
    public void AdvanceTransferred(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        // Also here, and not only in `BeginFile`: a caller that reports bytes without announcing
        // a file would otherwise measure its rate against a clock that had never started.
        if (!_clock.IsRunning)
        {
            _clock.Start();
        }

        CompletedBytes += bytes;
        TransferredBytes += bytes;

        SampleRate();
    }

    /// <summary>
    /// Records a file that finished without its data being copied.
    /// </summary>
    /// <param name="bytes">The file's size, which counts towards the percentage.</param>
    /// <param name="strategy">How it was handled.</param>
    public void CompleteWithoutTransfer(long bytes, CopyStrategy strategy)
    {
        CompletedBytes += Math.Max(bytes, 0);

        // Deliberately not fed to the rate: nothing moved, so this says nothing about speed.
        switch (strategy)
        {
            case CopyStrategy.Reflink:
                ReflinkedFiles++;
                break;

            case CopyStrategy.Rename:
                RenamedFiles++;
                break;

            default:
                break;
        }
    }

    /// <summary>Records that a file finished.</summary>
    /// <param name="strategy">How it was copied.</param>
    public void CompleteFile(CopyStrategy strategy)
    {
        CompletedFiles++;

        if (strategy is CopyStrategy.Buffered or CopyStrategy.KernelCopy)
        {
            CopiedFiles++;
        }
    }

    /// <summary>
    /// Corrects the total once it is known more precisely than the initial estimate.
    /// </summary>
    /// <param name="totalBytes">The revised total.</param>
    public void ReviseTotal(long totalBytes) => TotalBytes = Math.Max(totalBytes, CompletedBytes);

    /// <summary>
    /// Describes the progress the way the task view shows it.
    /// </summary>
    /// <returns>A single line of text.</returns>
    public string Describe()
    {
        System.Text.StringBuilder text = new();

        text.Append(CultureInfo.InvariantCulture, $"{Fraction * 100:F0}%");

        if (TotalBytes > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                        $"  {FormatBytes(CompletedBytes)}/{FormatBytes(TotalBytes)}");
        }

        if (BytesPerSecond is { } rate)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {FormatBytes((long)rate)}/s");
        }

        if (Estimate is { } estimate)
        {
            text.Append(CultureInfo.InvariantCulture, $"  ETA {FormatDuration(estimate)}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Describes how the files were handled, when that is worth saying.
    /// </summary>
    /// <returns>
    /// A summary, or an empty string when nothing was reflinked or renamed and so there is
    /// nothing surprising to report.
    /// </returns>
    public string DescribeStrategies()
    {
        List<string> parts = [];

        if (ReflinkedFiles > 0)
        {
            parts.Add($"reflinked {ReflinkedFiles} (instant)");
        }

        if (RenamedFiles > 0)
        {
            parts.Add($"renamed {RenamedFiles}");
        }

        if (CopiedFiles > 0 && parts.Count > 0)
        {
            parts.Add($"copied {CopiedFiles}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>Measures the rate over the interval since the last measurement.</summary>
    private void SampleRate()
    {
        TimeSpan now = _clock.Elapsed;
        TimeSpan interval = now - _lastSampleAt;

        // Sampling too often measures scheduling noise rather than throughput.
        if (interval < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _rate.Record(TransferredBytes - _transferredAtLastSample, interval);
        _lastSampleAt = now;
        _transferredAtLastSample = TransferredBytes;
    }

    /// <summary>Formats a byte count compactly.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>A short representation.</returns>
    /// <remarks>
    /// The same formatting the listing uses, rather than a second copy of it. This was a second
    /// copy, and carried the same defect: rounding by decimal places instead of significant
    /// figures, so a transfer running at 19.9 MB/s reported 20 M/s.
    /// </remarks>
    public static string FormatBytes(long bytes) => Model.HumanReadable.Format(bytes);

    /// <summary>Formats a duration as minutes and seconds, or hours when it is long.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>A short representation.</returns>
    /// <remarks>
    /// The same formatting the queue's own figures use, rather than a second copy of it.
    /// </remarks>
    public static string FormatDuration(TimeSpan duration) => Model.HumanReadable.Duration(duration);
}
