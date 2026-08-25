// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;

namespace Canger.Core.Tests.Input;

/// <summary>
/// Parsing of key bindings, checked against ranger's own doctests where they exist.
/// </summary>
public class KeyBindingParserTests
{
    [Fact]
    public void Parse_MatchesRangersDoctests()
    {
        // ranger/ext/keybinding_parser.py:79-90
        Assert.Equal([108, 111, 108, 10], KeyBindingParser.Parse("lol<CR>"));
        Assert.Equal([120, 9003, 260], KeyBindingParser.Parse("x<A-Left>"));
    }

    [Fact]
    public void Parse_TreatsPlainCharactersAsThemselves() =>
        Assert.Equal([103, 103], KeyBindingParser.Parse("gg"));

    [Fact]
    public void Parse_IsCaseSensitiveOutsideBrackets()
    {
        Assert.NotEqual(KeyBindingParser.Parse("gg"), KeyBindingParser.Parse("GG"));
        Assert.Equal([71, 71], KeyBindingParser.Parse("GG"));
    }

    [Theory]
    [InlineData("<CR>")]
    [InlineData("<cr>")]
    [InlineData("<Cr>")]
    [InlineData("<cR>")]
    public void Parse_IsCaseInsensitiveInsideBrackets(string binding) =>
        Assert.Equal([10], KeyBindingParser.Parse(binding));

    [Theory]
    [InlineData("<space>", 32)]
    [InlineData("<esc>", 27)]
    [InlineData("<escape>", 27)]
    [InlineData("<tab>", 9)]
    [InlineData("<enter>", 10)]
    [InlineData("<return>", 10)]
    [InlineData("<backspace>", 263)]
    [InlineData("<backspace2>", 127)]
    [InlineData("<delete>", 330)]
    [InlineData("<insert>", 331)]
    [InlineData("<up>", 259)]
    [InlineData("<down>", 258)]
    [InlineData("<left>", 260)]
    [InlineData("<right>", 261)]
    [InlineData("<home>", 262)]
    [InlineData("<end>", 360)]
    [InlineData("<pagedown>", 338)]
    [InlineData("<pageup>", 339)]
    [InlineData("<s-tab>", 353)]
    public void Parse_ResolvesNamedKeys(string binding, int expected) =>
        Assert.Equal([expected], KeyBindingParser.Parse(binding));

    [Theory]
    [InlineData("<C-a>", 1)]
    [InlineData("<C-z>", 26)]
    [InlineData("<C-c>", 3)]
    [InlineData("<C-space>", 0)]
    public void Parse_ResolvesControlCombinations(string binding, int expected) =>
        Assert.Equal([expected], KeyBindingParser.Parse(binding));

    [Fact]
    public void Parse_ResolvesControlUnderscoreToMinusOne()
    {
        // ord('_') - 96 underflows to -1. This is ranger's value, quirk and all, and it is
        // preserved because -1 is also what "no key" reports.
        Assert.Equal([-1], KeyBindingParser.Parse("<C-_>"));
    }

    [Theory]
    [InlineData("<f0>", 264)]
    [InlineData("<f1>", 265)]
    [InlineData("<F10>", 274)]
    [InlineData("<f63>", 327)]
    public void Parse_ResolvesFunctionKeys(string binding, int expected) =>
        Assert.Equal([expected], KeyBindingParser.Parse(binding));

    [Fact]
    public void Parse_ExpandsAltCombinationsToTwoCodes()
    {
        Assert.Equal([KeyCodes.Alt, 106], KeyBindingParser.Parse("<A-j>"));
        Assert.Equal([KeyCodes.Alt, 261], KeyBindingParser.Parse("<a-right>"));
        Assert.Equal([KeyCodes.Alt, 49], KeyBindingParser.Parse("<a-1>"));
    }

    [Theory]
    [InlineData("<any>", KeyCodes.Any)]
    [InlineData("<bg>", KeyCodes.PassiveAction)]
    [InlineData("<alt>", KeyCodes.Alt)]
    [InlineData("<allow_quantifiers>", KeyCodes.AllowQuantifiers)]
    public void Parse_ResolvesWildcardSentinels(string binding, int expected) =>
        Assert.Equal([expected], KeyBindingParser.Parse(binding));

    [Fact]
    public void Parse_ResolvesAnAllDigitBracketToThatRawCode() =>
        Assert.Equal([27], KeyBindingParser.Parse("<27>"));

    [Fact]
    public void Parse_ResolvesLtToALiteralLessThan() =>
        Assert.Equal([60], KeyBindingParser.Parse("<lt>"));

    [Fact]
    public void Parse_ReturnsUnknownBracketsVerbatim() =>
        Assert.Equal([60, 110, 111, 112, 101, 62], KeyBindingParser.Parse("<nope>"));

    [Fact]
    public void Parse_HandlesAnUnterminatedBracket() =>
        // The closing bracket is absent from the output, matching ranger.
        Assert.Equal([60, 97, 98], KeyBindingParser.Parse("<ab"));

    [Fact]
    public void Parse_HandlesRealBindingsFromTheSampleConfiguration()
    {
        // p'<any>  — paste into the bookmark whose letter follows.
        Assert.Equal([112, 39, KeyCodes.Any], KeyBindingParser.Parse("p'<any>"));

        // m<bg>    — show the bookmark overlay as soon as m is pressed.
        Assert.Equal([109, KeyCodes.PassiveAction], KeyBindingParser.Parse("m<bg>"));

        // "<any>   — toggle the tag whose letter follows.
        Assert.Equal([34, KeyCodes.Any], KeyBindingParser.Parse("\"<any>"));
    }

    [Fact]
    public void Parse_TreatsNonAsciiBindingsAsTheirUtf8Bytes()
    {
        // Bindings match raw bytes, so a two-byte character binds a two-key sequence and
        // matches the bytes that arrive when it is typed.
        Assert.Equal([0xC3, 0xB6], KeyBindingParser.Parse("ö"));
    }

    [Fact]
    public void Parse_ReturnsNothingForAnEmptyBinding() =>
        Assert.Empty(KeyBindingParser.Parse(""));

    [Theory]
    [InlineData(32, "<space>")]
    [InlineData(9, "<c-i>")]
    [InlineData(10, "<c-j>")]
    [InlineData(27, "<escape>")]
    [InlineData(97, "a")]
    [InlineData(260, "<left>")]
    [InlineData(9001, "<any>")]
    [InlineData(4242, "<4242>")]
    public void ToDisplayString_RendersKeysTheWayTheTitleBarShowsThem(int key, string expected) =>
        Assert.Equal(expected, KeyCodes.ToDisplayString(key));

    [Fact]
    public void ToDisplayString_RendersAWholeSequence() =>
        Assert.Equal("gg", KeyCodes.ToDisplayString(KeyBindingParser.Parse("gg")));
}
