// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.FileOperations;

/// <summary>
/// Tracks how fast data is actually moving, so a remaining time can be estimated.
/// </summary>
/// <remarks>
/// <para>
/// The rate is a decaying average rather than a simple total over elapsed time, because
/// throughput is not constant: it drops when a copy crosses onto a slower disk and recovers
/// afterwards. An average that never forgets would still be quoting the old speed minutes later.
/// </para>
/// <para>
/// Only bytes that genuinely moved are recorded. A reflinked file transfers nothing at all, so
/// feeding its size in would suggest an impossible rate and collapse every subsequent estimate to
/// zero — the copy would appear to be finishing instantly, right up until it did not.
/// </para>
/// </remarks>
public sealed class TransferRate(double smoothing = 0.3)
{
    private readonly double _smoothing = Math.Clamp(smoothing, 0.01, 1.0);

    private double _bytesPerSecond;
    private bool _hasSample;

    /// <summary>The current estimate of throughput, or <see langword="null"/> when unknown.</summary>
    public double? BytesPerSecond => _hasSample && _bytesPerSecond > 0 ? _bytesPerSecond : null;

    /// <summary>
    /// Records data that actually moved.
    /// </summary>
    /// <param name="bytes">How many bytes were transferred.</param>
    /// <param name="elapsed">How long it took.</param>
    public void Record(long bytes, TimeSpan elapsed)
    {
        // A zero-length interval says nothing about speed and would divide by zero.
        if (bytes <= 0 || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        double sample = bytes / elapsed.TotalSeconds;

        _bytesPerSecond = _hasSample
            ? (_smoothing * sample) + ((1 - _smoothing) * _bytesPerSecond)
            : sample;

        _hasSample = true;
    }

    /// <summary>
    /// Estimates how much longer the remaining bytes will take.
    /// </summary>
    /// <param name="remainingBytes">
    /// How much is left to transfer. This should exclude anything expected to be reflinked or
    /// renamed, since those take no time proportional to their size.
    /// </param>
    /// <returns>The estimate, or <see langword="null"/> when there is not enough to go on.</returns>
    public TimeSpan? Estimate(long remainingBytes)
    {
        if (remainingBytes <= 0)
        {
            return TimeSpan.Zero;
        }

        return BytesPerSecond is { } rate
            ? TimeSpan.FromSeconds(remainingBytes / rate)
            : null;
    }

    /// <summary>Forgets what has been measured.</summary>
    public void Reset()
    {
        _bytesPerSecond = 0;
        _hasSample = false;
    }
}
