// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Tui.Rendering;
using Canger.Tui.Text;

namespace Canger.Tui.Tests.Text;

/// <summary>
/// Text whose characters are drawn on top of one another rather than beside them.
/// </summary>
/// <remarks>
/// Reported from a listing of Hindi filenames: gaps inside the words, and a number from the column
/// behind showing through at the end of the row. Both from one cause — a Devanagari matra counted
/// as a column of its own, so every such name measured wider than it rendered, was truncated
/// early, and left the cells past the end untouched.
/// </remarks>
public class CombiningMarkTests
{
    // हेल्थ इंश्यो — twelve code points, eight columns: four nonspacing marks take none, and the
    // one spacing mark keeps its own.
    private const string Hindi = "हेल्थ इंश्यो";

    [Fact]
    public void ANonSpacingMarkTakesNoColumn()
    {
        Assert.Equal(0, CellWidth.Of(new Rune(0x0947)));   // ◌े  Devanagari vowel sign e
        Assert.Equal(0, CellWidth.Of(new Rune(0x094D)));   // ◌्  virama
        Assert.Equal(0, CellWidth.Of(new Rune(0x0301)));   // ◌́  combining acute
    }

    [Fact]
    public void ASpacingMarkKeepsItsColumn()
    {
        // Tried at zero and reverted. It closes the gaps inside Devanagari words and makes every
        // name containing one measure narrower than it draws, so the text overruns its column and
        // writes over the one beside it. A gap is a blemish; bleeding columns are unusable.
        Assert.Equal(1, CellWidth.Of(new Rune(0x093E)));   // ◌ा  Devanagari vowel sign aa
        Assert.Equal(1, CellWidth.Of(new Rune(0x093F)));   // ◌ि  Devanagari vowel sign i
        Assert.Equal(1, CellWidth.Of(new Rune(0x094B)));   // ◌ो  Devanagari vowel sign o
    }

    [Fact]
    public void OverReservingIsTheSafeDirection()
    {
        // Whether a cluster takes one column or two is a question about the font and the
        // terminal's shaping, not one the Unicode category can answer. Until it is measured, a
        // measurement that is too large wastes a column and keeps the grid; one that is too small
        // destroys it.
        Assert.True(CellWidth.Of("का") >= 1);
        Assert.True(CellWidth.Of("मकान") >= 3);
    }

    [Fact]
    public void AZeroWidthJoinerTakesNoColumn()
    {
        Assert.Equal(0, CellWidth.Of(new Rune(0x200D)));
    }

    [Fact]
    public void TheHindiNameMeasuresWhatItRenders()
    {
        Assert.Equal(12, Hindi.EnumerateRunes().Count());
        Assert.Equal(8, CellWidth.Of(Hindi));
    }

    [Fact]
    public void WritingItAdvancesByItsWidthAndNotByItsLength()
    {
        // The heart of the defect: Write returning 12 where the terminal moved 8 is what left the
        // last four columns of the row unwritten.
        ScreenBuffer screen = new(40, 1);

        Assert.Equal(8, screen.Write(0, 0, Hindi));
    }

    [Fact]
    public void TheMarksAreStillDrawn()
    {
        // Measuring them as zero must not mean dropping them: the name has to read correctly.
        ScreenBuffer screen = new(40, 1);
        screen.Write(0, 0, Hindi);

        Assert.StartsWith(Hindi, screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void WhatFollowsIsNotPushedAlong()
    {
        // The gaps inside the words: everything after a mark used to be positioned a column too
        // far right for each mark before it.
        ScreenBuffer screen = new(40, 1);
        int used = screen.Write(0, 0, Hindi);
        screen.Write(used, 0, "END");

        Assert.Equal(Hindi + "END", screen.TextAt(0).TrimEnd());
    }

    [Fact]
    public void ARowIsFilledToItsEndSoNothingShowsThrough()
    {
        // The stray digit: a shorter measurement left cells the previous frame had written.
        ScreenBuffer screen = new(12, 1);
        screen.Write(0, 0, "1234567890AB");
        screen.Fill(0, 0, 12, 1, CellStyle.Default);
        screen.Write(0, 0, Hindi);

        Assert.Equal(Hindi, screen.TextAt(0).TrimEnd());
        Assert.DoesNotContain("B", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkWithNothingToAttachToIsDropped()
    {
        // A row cannot start with a mark: there is no character under it.
        ScreenBuffer screen = new(10, 1);

        Assert.Equal(0, screen.Write(0, 0, "े"));
        Assert.Equal(string.Empty, screen.TextAt(0).TrimEnd());
    }

    [Fact]
    public void EveryMeasurementAgrees()
    {
        // The invariant both symptoms came from. `CellWidth` said one thing, `WideString` said
        // another, and the buffer advanced by a third — so the model of where things were on
        // screen stopped matching the terminal, text was truncated early, and cells the buffer
        // believed it had filled were never emitted. Whatever the previous frame had left in them
        // stayed: in the report, a count from the column behind.
        ScreenBuffer screen = new(40, 1);

        Assert.Equal(8, CellWidth.Of(Hindi));
        Assert.Equal(8, new WideString(Hindi).Width);
        Assert.Equal(8, screen.Write(0, 0, Hindi));
    }

    [Fact]
    public void TruncatingKeepsTheMarksWithTheirLetters()
    {
        // Cutting between a letter and its matra would leave the mark stranded on whatever
        // followed it.
        string cut = new WideString(Hindi).Truncate(5);

        Assert.True(CellWidth.Of(cut) <= 5, $"'{cut}' is {CellWidth.Of(cut)} cells, not 5");
        Assert.EndsWith("~", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void SlicingDoesNotHangOnAMark()
    {
        // `Slice` advanced by the rune's width, so a mark measuring zero left the index where it
        // was and the loop never ended. Canger cleared the screen and drew nothing at all.
        WideString text = new(Hindi);

        Assert.Equal(3, CellWidth.Of(text.Take(3)));
    }
}
