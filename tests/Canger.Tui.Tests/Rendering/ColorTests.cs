// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Tui.Tests.Rendering;

public class ColorTests
{
    [Fact]
    public void DefaultValueIsTheTerminalDefaultNotBlack()
    {
        // Styles are routinely passed as `default`, and a struct's default is all zero bits.
        // If that meant palette index 0, every unstyled cell would be drawn black on black.
        Assert.True(default(Color).IsDefault);
        Assert.True(Color.Default.IsDefault);
        Assert.Equal(-1, default(Color).Index);

        CellStyle style = default;
        Assert.True(style.Foreground.IsDefault);
        Assert.True(style.Background.IsDefault);
    }

    [Fact]
    public void BlackIsDistinctFromTheDefault()
    {
        Assert.False(Color.Black.IsDefault);
        Assert.Equal(0, Color.Black.Index);
        Assert.NotEqual(Color.Default, Color.Black);
    }

    [Fact]
    public void FromIndex_AcceptsTheWholePaletteAndTheDefault()
    {
        Assert.Equal(0, Color.FromIndex(0).Index);
        Assert.Equal(255, Color.FromIndex(255).Index);
        Assert.True(Color.FromIndex(-1).IsDefault);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(256)]
    public void FromIndex_RejectsAnIndexOutsideThePalette(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.FromIndex(index));

    [Fact]
    public void Bright_IsIdempotent()
    {
        // Colourschemes traditionally brighten by adding 8, which silently produces a different
        // colour if applied twice. Doing it through a method makes repetition harmless.
        Assert.Equal(Color.FromIndex(12), Color.Blue.Bright());
        Assert.Equal(Color.FromIndex(12), Color.Blue.Bright().Bright());
    }

    [Fact]
    public void Bright_LeavesColoursWithNoBrightFormAlone()
    {
        Assert.Equal(Color.FromIndex(200), Color.FromIndex(200).Bright());
        Assert.True(Color.Default.Bright().IsDefault);
    }

    [Fact]
    public void SetStyle_UsesTheBaseSixteenCodesWhereItCan()
    {
        Assert.Equal("\e[0;31m", Ansi.SetStyle(new CellStyle(Color.Red, Color.Default)));
        Assert.Equal("\e[0;42m", Ansi.SetStyle(new CellStyle(Color.Default, Color.Green)));
        Assert.Equal("\e[0;94m", Ansi.SetStyle(new CellStyle(Color.Blue.Bright(), Color.Default)));
    }

    [Fact]
    public void SetStyle_FallsBackToThe256ColourFormForHigherIndices()
    {
        // The solarized scheme uses raw indices such as 166 and 244.
        Assert.Equal("\e[0;38;5;166m",
                     Ansi.SetStyle(new CellStyle(Color.FromIndex(166), Color.Default)));
        Assert.Equal("\e[0;48;5;235m",
                     Ansi.SetStyle(new CellStyle(Color.Default, Color.FromIndex(235))));
    }

    [Fact]
    public void SetStyle_SaysNothingAboutColoursLeftAtTheDefault() =>
        Assert.Equal("\e[0m", Ansi.SetStyle(CellStyle.Default));

    [Fact]
    public void SetStyle_EmitsAttributes()
    {
        CellStyle style = CellStyle.Default
            .With(CellAttributes.Bold)
            .With(CellAttributes.Underline)
            .With(CellAttributes.Reverse);

        Assert.Equal("\e[0;1;4;7m", Ansi.SetStyle(style));
    }

    [Fact]
    public void SetStyle_AlwaysStartsFromAKnownState() =>
        // Every sequence begins with a reset, so a style describes itself completely rather than
        // depending on whatever was in effect before it.
        Assert.All(
            new[]
            {
                CellStyle.Default,
                new CellStyle(Color.Red, Color.Blue),
                CellStyle.Default.With(CellAttributes.Bold),
            },
            style => Assert.StartsWith("\e[0", Ansi.SetStyle(style), StringComparison.Ordinal));
}
