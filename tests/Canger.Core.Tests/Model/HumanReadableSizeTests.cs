// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Byte counts as ranger writes them: three significant figures, not one decimal place.
/// </summary>
/// <remarks>
/// Canger rounded by decimal places — one below ten, none above — so 19.9 M became 20 M and
/// 7.59 M became 7.6 M, losing a digit in the column that is most often being read for one.
/// Ranger's <c>%.3g</c> (<c>ext/human_readable.py:51</c>) keeps three either way.
/// </remarks>
public class HumanReadableSizeTests
{
    [Theory]
    // Ranger's own doctests, verbatim (`ext/human_readable.py:16-26`).
    [InlineData(54, false, "54 B")]
    [InlineData(1500, false, "1.5 k")]
    [InlineData(1072693248, false, "1.07 G")]
    [InlineData(54, true, "54 B")]
    [InlineData(1500, true, "1.46 Ki")]
    [InlineData(1072693248, true, "1023 Mi")]
    public void Format_MatchesRangersDoctests(long bytes, bool binary, string expected)
    {
        Assert.Equal(expected, HumanReadable.Format(bytes, binary));
    }

    [Theory]
    // The sizes from the reported screenshot, which is what three figures buys.
    [InlineData(19_900_000, "19.9 M")]
    [InlineData(7_590_000, "7.59 M")]
    [InlineData(825_000, "825 k")]
    [InlineData(24_800_000, "24.8 M")]
    public void Format_KeepsThreeFiguresRatherThanOneDecimalPlace(long bytes, string expected)
    {
        Assert.Equal(expected, HumanReadable.Format(bytes));
    }

    [Theory]
    // Trailing zeros go, as `%g` drops them.
    [InlineData(20_000_000, "20 M")]
    [InlineData(1_000_000_000, "1 G")]
    [InlineData(3_000, "3 k")]
    [InlineData(1_000, "1 k")]
    public void Format_DropsTrailingZeros(long bytes, string expected)
    {
        Assert.Equal(expected, HumanReadable.Format(bytes));
    }

    [Fact]
    public void Format_UsesALowercaseKiloPrefix()
    {
        // Ranger's decimal prefixes are `('B', 'k', 'M', 'G', 'T', 'P')` — the kilo is the only
        // lowercase one, and Canger had it capital.
        Assert.EndsWith(" k", HumanReadable.Format(825_000), StringComparison.Ordinal);
        Assert.EndsWith(" M", HumanReadable.Format(825_000_000), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_WritesNothingAsABareZero(long bytes)
    {
        // No unit and no separator: ranger returns `'0'` before it reaches either
        // (`ext/human_readable.py:34-35`). A size of nothing needs no unit to be understood.
        Assert.Equal("0", HumanReadable.Format(bytes));
    }

    [Fact]
    public void Format_KeepsFourFiguresJustBelowABinaryBoundary()
    {
        // 1023 Mi is a real quantity and rounding it to three figures would say 1.02 Gi, which is
        // not what it is. Ranger switches to `%.4g` for the same reason.
        Assert.Equal("1023 Mi", HumanReadable.Format(1024L * 1024 * 1023, binary: true));
    }

    [Fact]
    public void Format_WritesTheCarryPlainlyRatherThanInExponentialNotation()
    {
        // The one deliberate difference from ranger, which renders this as `1e+03 k`: rounding
        // carries 999.999 up a digit, and C's `%g` switches notation when it does. Five
        // characters that say less than the number they replaced, in a column measured in
        // characters.
        Assert.Equal("1000 k", HumanReadable.Format(999_999));
    }

    [Fact]
    public void Format_WritesTheCountInFullWhenAskedForExactBytes()
    {
        // `size_in_bytes`, which ranger checks before anything else
        // (`ext/human_readable.py:36-37`) and writes with the locale's grouping.
        string exact = HumanReadable.Format(1_234_567, exact: true);

        Assert.DoesNotContain("M", exact, StringComparison.Ordinal);
        Assert.Equal("1234567", exact.Replace(",", string.Empty, StringComparison.Ordinal)
                                     .Replace(".", string.Empty, StringComparison.Ordinal)
                                     .Replace("\u00a0", string.Empty, StringComparison.Ordinal)
                                     .Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Format_PutsTheSeparatorBetweenTheNumberAndTheUnit()
    {
        // How ranger marks a measured size it can no longer vouch for.
        Assert.Equal("19.9? M", HumanReadable.Format(19_900_000, separator: "? "));
    }
}
