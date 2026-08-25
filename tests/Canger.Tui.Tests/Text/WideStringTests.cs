// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Text;

namespace Canger.Tui.Tests.Text;

/// <summary>
/// Cell-accurate text measurement and slicing, checked against ranger's own doctests in
/// <c>ext/widestring.py:104-125</c>.
/// </summary>
public class WideStringTests
{
    // モヒカン — four wide characters, eight cells.
    private const string Katakana = "モヒカン";

    [Fact]
    public void Width_CountsWideCharactersAsTwoCells()
    {
        Assert.Equal(3, CellWidth.Of("poo"));
        Assert.Equal(8, CellWidth.Of(Katakana));
        Assert.Equal(4, CellWidth.Of("aモa"));
        Assert.Equal(0, CellWidth.Of(""));
    }

    [Fact]
    public void Width_TreatsAsciiAsOneCellEach() =>
        Assert.Equal(5, CellWidth.Of("hello"));

    [Theory]
    [InlineData(0x1100)]  // Hangul Choseong
    [InlineData(0x30E2)]  // Katakana モ
    [InlineData(0xFF21)]  // Fullwidth A
    [InlineData(0x1F600)] // Emoji
    public void IsWide_RecognisesDoubleWidthCodePoints(int codePoint) =>
        Assert.True(CellWidth.IsWide(codePoint));

    [Theory]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData(0x00E9)] // é
    [InlineData(0x0416)] // Cyrillic Ж
    public void IsWide_RecognisesSingleWidthCodePoints(int codePoint) =>
        Assert.False(CellWidth.IsWide(codePoint));

    [Fact]
    public void Slice_MatchesRangersDoctests()
    {
        Assert.Equal("sd", new WideString("asdf").Slice(1, 2));
        Assert.Equal("ヒ", new WideString(Katakana).Slice(2, 2));
        Assert.Equal("ヒ ", new WideString(Katakana).Slice(2, 3));
        Assert.Equal("ab ", new WideString("モabカン").Slice(2, 3));
        Assert.Equal(" ヒ ", new WideString(Katakana).Slice(1, 4));
        Assert.Equal(Katakana, new WideString(Katakana).Slice(0, 8));
        Assert.Equal("aモ", new WideString("aモ").Slice(0, 3));
        Assert.Equal("a ", new WideString("aモ").Slice(0, 2));
        Assert.Equal("a", new WideString("aモ").Slice(0, 1));
    }

    [Fact]
    public void Slice_ReplacesEitherHalfOfACutWideCharacterWithASpace()
    {
        WideString text = new(Katakana);

        // Cutting through the tail of one character and the head of the next leaves two spaces,
        // so the slice still occupies exactly the cells it was asked for.
        Assert.Equal("  ", text.Slice(3, 2));
        Assert.Equal("  ", text.Slice(1, 2));
    }

    [Fact]
    public void Slice_AlwaysFillsTheCellsItWasAskedFor()
    {
        // The point of substituting spaces is that the next column starts where it should.
        // Ranger drops a character in this particular case (ext/widestring.py:137-138), which
        // leaves a one-cell hole; Canger keeps the alignment instead.
        Assert.Equal(" a", new WideString("aモa").Slice(2, 2));

        foreach (int start in Enumerable.Range(0, 8))
        {
            foreach (int length in Enumerable.Range(1, 8 - start))
            {
                string slice = new WideString(Katakana).Slice(start, length);
                Assert.Equal(length, CellWidth.Of(slice));
            }
        }
    }

    [Fact]
    public void Slice_HandlesRangesOutsideTheText()
    {
        WideString text = new("asdf");

        Assert.Equal("", text.Slice(0, 0));
        Assert.Equal("", text.Slice(0, -1));
        Assert.Equal("", text.Slice(10, 4));
        Assert.Equal("asdf", text.Slice(0, 100));
        Assert.Equal("asdf", text.Slice(-2, 100));
    }

    [Fact]
    public void Truncate_LeavesTextThatAlreadyFits() =>
        Assert.Equal("short", new WideString("short").Truncate(20));

    [Fact]
    public void Truncate_AppendsTheEllipsisWithinTheGivenWidth()
    {
        Assert.Equal("abcd~", new WideString("abcdefgh").Truncate(5));
        Assert.Equal("abcd…", new WideString("abcdefgh").Truncate(5, "…"));
    }

    [Fact]
    public void Truncate_NeverExceedsTheGivenWidth()
    {
        foreach (int width in Enumerable.Range(1, 10))
        {
            Assert.True(CellWidth.Of(new WideString(Katakana).Truncate(width)) <= width);
            Assert.True(CellWidth.Of(new WideString("abcdefghij").Truncate(width)) <= width);
        }
    }

    [Fact]
    public void Truncate_DegradesGracefullyWhenThereIsNoRoomForTheEllipsis() =>
        Assert.Equal("a", new WideString("abcdef").Truncate(1));

    [Fact]
    public void Width_ReportsCellsNotCharacters()
    {
        WideString text = new(Katakana);

        Assert.Equal(8, text.Width);
        Assert.Equal(4, text.Text.Length);
    }
}
