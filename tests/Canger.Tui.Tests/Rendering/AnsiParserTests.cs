// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;

namespace Canger.Tui.Tests.Rendering;

/// <summary>
/// Reading the colour codes that preview scripts and other programs emit.
/// </summary>
public class AnsiParserTests
{
    [Fact]
    public void Parse_ReturnsPlainTextAsOneRun()
    {
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("hello world");

        StyledRun run = Assert.Single(runs);
        Assert.Equal("hello world", run.Text);
        Assert.Equal(CellStyle.Default, run.Style);
    }

    [Fact]
    public void Parse_SplitsWhereTheStyleChanges()
    {
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("plain\e[31mred\e[0mplain again");

        Assert.Equal(3, runs.Count);
        Assert.Equal("plain", runs[0].Text);
        Assert.Equal("red", runs[1].Text);
        Assert.Equal(Color.Red, runs[1].Style.Foreground);
        Assert.Equal("plain again", runs[2].Text);
        Assert.Equal(Color.Default, runs[2].Style.Foreground);
    }

    [Fact]
    public void Parse_ReadsTheBaseColours()
    {
        Assert.Equal(Color.Red, AnsiParser.Parse("\e[31mx")[0].Style.Foreground);
        Assert.Equal(Color.Green, AnsiParser.Parse("\e[42mx")[0].Style.Background);
        Assert.Equal(Color.Default, AnsiParser.Parse("\e[31m\e[39mx")[0].Style.Foreground);
    }

    [Fact]
    public void Parse_ReadsTheBrightColours()
    {
        Assert.Equal(Color.FromIndex(12), AnsiParser.Parse("\e[94mx")[0].Style.Foreground);
        Assert.Equal(Color.FromIndex(10), AnsiParser.Parse("\e[102mx")[0].Style.Background);
    }

    [Fact]
    public void Parse_Reads256ColourCodes()
    {
        // This is what the shipped preview script emits for syntax highlighting.
        Assert.Equal(Color.FromIndex(244), AnsiParser.Parse("\e[38;5;244mx")[0].Style.Foreground);
        Assert.Equal(Color.FromIndex(17), AnsiParser.Parse("\e[48;5;17mx")[0].Style.Background);
    }

    [Fact]
    public void Parse_ReducesTrueColourToTheNearestPaletteEntry()
    {
        // Canger draws through a palette, and an approximation keeps highlighting legible where
        // discarding the colour would not.
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("\e[38;2;255;0;0mx");

        Assert.False(runs[0].Style.Foreground.IsDefault);
    }

    [Fact]
    public void Parse_ReadsAttributes()
    {
        Assert.True(AnsiParser.Parse("\e[1mx")[0].Style.Attributes.HasFlag(CellAttributes.Bold));
        Assert.True(AnsiParser.Parse("\e[3mx")[0].Style.Attributes.HasFlag(CellAttributes.Italic));
        Assert.True(AnsiParser.Parse("\e[4mx")[0].Style.Attributes.HasFlag(CellAttributes.Underline));
        Assert.True(AnsiParser.Parse("\e[7mx")[0].Style.Attributes.HasFlag(CellAttributes.Reverse));
    }

    [Fact]
    public void Parse_ReadsAttributesBeingTurnedOff()
    {
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("\e[1mbold\e[22mnormal");

        Assert.True(runs[0].Style.Attributes.HasFlag(CellAttributes.Bold));
        Assert.False(runs[1].Style.Attributes.HasFlag(CellAttributes.Bold));
    }

    [Fact]
    public void Parse_CombinesSeveralParametersInOneSequence()
    {
        CellStyle style = AnsiParser.Parse("\e[1;31;42mx")[0].Style;

        Assert.True(style.Attributes.HasFlag(CellAttributes.Bold));
        Assert.Equal(Color.Red, style.Foreground);
        Assert.Equal(Color.Green, style.Background);
    }

    [Fact]
    public void Parse_TreatsAnEmptySequenceAsAReset()
    {
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("\e[31mred\e[mplain");

        Assert.Equal(Color.Default, runs[1].Style.Foreground);
    }

    [Fact]
    public void Parse_DiscardsSequencesThatWouldMoveTheCursor()
    {
        // The text is being placed inside a widget, so anything that would take control of the
        // terminal has to go.
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("before\e[2Jafter\e[10;5Hend");

        Assert.Equal("beforeafterend", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Parse_KeepsAStrayEscapeRatherThanLosingText()
    {
        IReadOnlyList<StyledRun> runs = AnsiParser.Parse("a\eb");

        Assert.Contains("b", string.Concat(runs.Select(r => r.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_HandlesTheOutputOfARealHighlighter()
    {
        // Exactly what the shipped scope.sh produces for a shell script.
        string highlighted = "\e[38;5;15m\e[m\e[38;5;244m#!/bin/sh\e[m";

        IReadOnlyList<StyledRun> runs = AnsiParser.Parse(highlighted);

        Assert.Equal("#!/bin/sh", string.Concat(runs.Select(r => r.Text)));
        Assert.Contains(runs, r => r.Style.Foreground == Color.FromIndex(244));
    }

    [Fact]
    public void Strip_RemovesEverySequence()
    {
        Assert.Equal("#!/bin/sh",
                     AnsiParser.Strip("\e[38;5;15m\e[m\e[38;5;244m#!/bin/sh\e[m"));
        Assert.Equal("plain", AnsiParser.Strip("plain"));
    }

    [Theory]
    [InlineData(0, 0, 0, 16)]
    [InlineData(255, 255, 255, 231)]
    [InlineData(128, 128, 128, 244)]
    public void ToPaletteIndex_MapsGreysOntoTheGreyRamp(int r, int g, int b, int expected) =>
        Assert.Equal(expected, AnsiParser.ToPaletteIndex(r, g, b));

    [Fact]
    public void ToPaletteIndex_StaysWithinThePalette()
    {
        foreach (int value in (int[])[0, 64, 128, 192, 255])
        {
            Assert.InRange(AnsiParser.ToPaletteIndex(value, 0, 255 - value), 16, 255);
        }
    }
}
