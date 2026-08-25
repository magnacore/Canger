// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// The progress and time estimate, including the rule that keeps the estimate honest when files
/// are reflinked rather than copied.
/// </summary>
public class CopyProgressTests
{
    [Fact]
    public void Fraction_TracksTheBytesAccountedFor()
    {
        CopyProgress progress = new(1000, 1);

        progress.AdvanceTransferred(250);

        Assert.Equal(0.25, progress.Fraction, 3);
    }

    [Fact]
    public void Fraction_CountsReflinkedFilesToo()
    {
        // A reflinked file is finished, so it must count towards the percentage even though no
        // data moved. Otherwise a directory of reflinks would appear stalled at zero.
        CopyProgress progress = new(1000, 2);

        progress.CompleteWithoutTransfer(500, CopyStrategy.Reflink);

        Assert.Equal(0.5, progress.Fraction, 3);
    }

    [Fact]
    public void Fraction_IsClampedToOne()
    {
        CopyProgress progress = new(100, 1);

        progress.AdvanceTransferred(500);

        Assert.Equal(1, progress.Fraction, 3);
    }

    [Fact]
    public void ReflinkedBytes_AreExcludedFromTheThroughputMeasurement()
    {
        // This is the point. Feeding a reflink's size into the rate would claim an impossible
        // speed, and every later estimate would collapse to nothing.
        CopyProgress progress = new(1_000_000_000, 2);

        progress.CompleteWithoutTransfer(900_000_000, CopyStrategy.Reflink);

        Assert.Equal(0, progress.TransferredBytes);
        Assert.Null(progress.BytesPerSecond);
    }

    [Fact]
    public void Strategies_AreCountedSeparately()
    {
        CopyProgress progress = new(300, 3);

        progress.CompleteWithoutTransfer(100, CopyStrategy.Reflink);
        progress.CompleteFile(CopyStrategy.Reflink);
        progress.CompleteWithoutTransfer(100, CopyStrategy.Rename);
        progress.CompleteFile(CopyStrategy.Rename);
        progress.AdvanceTransferred(100);
        progress.CompleteFile(CopyStrategy.Buffered);

        Assert.Equal(1, progress.ReflinkedFiles);
        Assert.Equal(1, progress.RenamedFiles);
        Assert.Equal(1, progress.CopiedFiles);
        Assert.Equal(3, progress.CompletedFiles);
    }

    [Fact]
    public void DescribeStrategies_SaysWhenFilesWereShared()
    {
        CopyProgress progress = new(200, 2);

        progress.CompleteWithoutTransfer(100, CopyStrategy.Reflink);
        progress.CompleteFile(CopyStrategy.Reflink);
        progress.AdvanceTransferred(100);
        progress.CompleteFile(CopyStrategy.Buffered);

        string summary = progress.DescribeStrategies();

        Assert.Contains("reflinked 1", summary, StringComparison.Ordinal);
        Assert.Contains("copied 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeStrategies_SaysNothingWhenEverythingWasCopiedNormally()
    {
        // Nothing surprising happened, so there is nothing to explain.
        CopyProgress progress = new(100, 1);

        progress.AdvanceTransferred(100);
        progress.CompleteFile(CopyStrategy.Buffered);

        Assert.Equal(string.Empty, progress.DescribeStrategies());
    }

    [Fact]
    public void Describe_ReadsAsAProgressLine()
    {
        CopyProgress progress = new(2_000_000, 1);
        progress.AdvanceTransferred(500_000);

        string line = progress.Describe();

        Assert.Contains("25%", line, StringComparison.Ordinal);
        Assert.Contains("500 k/2 M", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(512, "512 B")]
    [InlineData(1_500, "1.5 k")]
    [InlineData(25_000, "25 k")]
    [InlineData(1_500_000_000, "1.5 G")]
    public void FormatBytes_IsCompactAndReadable(long bytes, string expected) =>
        Assert.Equal(expected, CopyProgress.FormatBytes(bytes));

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(65, "01:05")]
    [InlineData(3_725, "1:02:05")]
    public void FormatDuration_ShowsHoursOnlyWhenThereAreSome(int seconds, string expected) =>
        Assert.Equal(expected, CopyProgress.FormatDuration(TimeSpan.FromSeconds(seconds)));
}

/// <summary>The decaying average that the time estimate is built on.</summary>
public class TransferRateTests
{
    [Fact]
    public void Rate_IsUnknownUntilSomethingHasMoved() =>
        Assert.Null(new TransferRate().BytesPerSecond);

    [Fact]
    public void Rate_ReflectsWhatWasMeasured()
    {
        TransferRate rate = new();

        rate.Record(1_000_000, TimeSpan.FromSeconds(1));

        Assert.InRange(rate.BytesPerSecond!.Value, 900_000, 1_100_000);
    }

    [Fact]
    public void Rate_MovesTowardsANewSpeedRatherThanJumpingToIt()
    {
        // Throughput varies; a rate that jumped to every sample would be unreadable, and one
        // that never moved would still quote the old speed long after the disk changed.
        TransferRate rate = new(smoothing: 0.3);

        rate.Record(1_000_000, TimeSpan.FromSeconds(1));
        rate.Record(10_000_000, TimeSpan.FromSeconds(1));

        Assert.InRange(rate.BytesPerSecond!.Value, 1_000_000, 10_000_000);
    }

    [Fact]
    public void Rate_IgnoresMeasurementsThatSayNothing()
    {
        TransferRate rate = new();

        rate.Record(0, TimeSpan.FromSeconds(1));
        rate.Record(1000, TimeSpan.Zero);

        Assert.Null(rate.BytesPerSecond);
    }

    [Fact]
    public void Estimate_DividesWhatIsLeftByTheRate()
    {
        TransferRate rate = new();
        rate.Record(1_000_000, TimeSpan.FromSeconds(1));

        TimeSpan? estimate = rate.Estimate(5_000_000);

        Assert.NotNull(estimate);
        Assert.InRange(estimate!.Value.TotalSeconds, 4, 6);
    }

    [Fact]
    public void Estimate_IsUnknownBeforeAnythingHasMoved() =>
        Assert.Null(new TransferRate().Estimate(1_000_000));

    [Fact]
    public void Estimate_IsZeroWhenNothingIsLeft()
    {
        TransferRate rate = new();
        rate.Record(1_000, TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, rate.Estimate(0));
    }
}
