// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

/// <summary>
/// The figures a transfer reports, and where on the line they sit.
/// </summary>
/// <remarks>
/// Every field here changes width as it counts — <c>5%</c> to <c>48%</c> to <c>100%</c>,
/// <c>159 M</c> to <c>1.08 G</c> — and written plainly each change shoves everything after it
/// sideways. The rate and the time remaining are the two a person watches, and they were jittering
/// left and right several times a second.
/// </remarks>
public class TransferFiguresTests
{
    /// <summary>A run of a real copy, at the points where a field changes width.</summary>
    private static readonly (double Fraction, long Completed)[] Progression =
    [
        (0.025, 75_200_000L),        // 75.2 M — six characters
        (0.053, 159_000_000L),       //  159 M — five
        (0.360, 1_080_000_000L),     // 1.08 G — six again, and the percentage gains a digit
        (1.000, 3_000_000_000L),     //    3 G — three, and the percentage gains another
    ];

    private const long Total = 3_000_000_000L;

    private static string[] Lines(double? rate, TimeSpan? estimate) =>
        [.. Progression.Select(point => TransferFigures.Describe(
                point.Fraction, point.Completed, Total, rate, estimate))];

    [Fact]
    public void TheRateStaysInOnePlaceAsEverythingBeforeItChangesWidth()
    {
        string[] lines = Lines(2_290_000_000d, TimeSpan.FromSeconds(41));

        int[] positions = [.. lines.Select(l => l.IndexOf("/s", StringComparison.Ordinal))];

        Assert.DoesNotContain(-1, positions);
        Assert.Single(positions.Distinct());
    }

    [Fact]
    public void TheTimeRemainingStaysInOnePlaceToo()
    {
        string[] lines = Lines(2_290_000_000d, TimeSpan.FromSeconds(41));

        int[] positions = [.. lines.Select(l => l.IndexOf("ETA", StringComparison.Ordinal))];

        Assert.DoesNotContain(-1, positions);
        Assert.Single(positions.Distinct());
    }

    [Fact]
    public void ARateThatChangesWidthDoesNotMoveTheTimeRemaining()
    {
        // 671 M/s and 1.18 G/s, which is what crossing from one prefix to the next looks like.
        string narrow = TransferFigures.Describe(0.5, 1_500_000_000L, Total, 671_000_000d,
                                                 TimeSpan.FromSeconds(41));
        string wide = TransferFigures.Describe(0.5, 1_500_000_000L, Total, 1_180_000_000d,
                                               TimeSpan.FromSeconds(41));

        Assert.Equal(narrow.IndexOf("ETA", StringComparison.Ordinal),
                     wide.IndexOf("ETA", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingIsPaddedOffTheEndOfTheLine()
    {
        // Trailing spaces would be invisible on screen and misleading in a test, and the field
        // that happens to come last needs no width of its own.
        string line = TransferFigures.Describe(0.5, 1_500_000_000L, Total, null, null);

        Assert.Equal(line.TrimEnd(), line);
    }

    [Fact]
    public void AWiderValueThanItsColumnIsStillShownInFull()
    {
        // Padding grows a field and never truncates it. A figure that outgrew its column would
        // shift the line, which is untidy; losing a digit would be wrong.
        string line = TransferFigures.Describe(1, 999_000_000_000_000L, 999_000_000_000_000L,
                                               null, null);

        Assert.Contains("999 T", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ATransferWithNoKnownTotalStillReportsWhatItCan()
    {
        string line = TransferFigures.Describe(0, 0, 0, null, null);

        Assert.Equal("0%", line.TrimStart());
    }
}
