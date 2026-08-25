// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// How a scout pattern is turned into something to match against.
/// </summary>
/// <remarks>
/// The reported symptom was that <c>,</c> announced a pattern and selected nothing. It runs
/// <c>scout -m ^&lt;stem&gt;</c>, and Canger matched the pattern as a plain substring — so it
/// looked for a literal caret, found none, and reported success anyway.
///
/// Ranger takes a leading <c>^</c> and a trailing <c>$</c> off first and puts them back as
/// anchors afterwards, whatever search method the flags asked for, and escapes the rest unless
/// <c>r</c> was given (<c>config/commands.py</c>, <c>_build_regex</c>).
/// </remarks>
public class ScoutPatternTests
{
    private static FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/Series 01.mkv")
            .AddFile("/home/Series 01.srt")
            .AddFile("/home/Series 02.mkv")
            .AddFile("/home/Other Film.mkv")
            .AddFile("/home/notes (2020).txt")
            .AddFile("/home/prequel to Series 01.mkv");

        return new FakeFileManager(fs, "/home");
    }

    private static string[] Marked(FakeFileManager manager, string line)
    {
        manager.Execute(line);
        return [.. manager.CurrentTab.Current.MarkedEntries.Select(e => e.RelativePath).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Scout_AnchorsALeadingCaret()
    {
        // What `,` issues, and what marked nothing at all.
        Assert.Equal(["Series 01.mkv", "Series 01.srt", "Series 02.mkv"],
                     Marked(Manager(), "scout -m ^Series"));
    }

    [Fact]
    public void Scout_MatchesAnywhereWithoutAnAnchor()
    {
        Assert.Equal(["Series 01.mkv", "Series 01.srt", "Series 02.mkv",
                      "prequel to Series 01.mkv"],
                     Marked(Manager(), "scout -m Series"));
    }

    [Fact]
    public void Scout_AnchorsATrailingDollar()
    {
        Assert.Equal(["Other Film.mkv", "Series 01.mkv", "Series 02.mkv",
                      "prequel to Series 01.mkv"],
                     Marked(Manager(), "scout -m mkv$"));
    }

    [Fact]
    public void Scout_TakesBothAnchorsAtOnce()
    {
        Assert.Equal(["Series 01.mkv"], Marked(Manager(), "scout -m ^Series 01.mkv$"));
    }

    [Fact]
    public void Scout_EscapesTheRestOfThePattern()
    {
        // A name is text, not an expression: the brackets here match themselves, and the dot
        // matches a dot rather than anything at all.
        Assert.Equal(["notes (2020).txt"], Marked(Manager(), "scout -m (2020)"));
    }

    [Fact]
    public void Scout_TakesThePatternAsARegexWithR()
    {
        Assert.Equal(["Series 01.mkv", "Series 02.mkv"],
                     Marked(Manager(), "scout -mr ^Series 0[12]\\.mkv$"));
    }

    [Fact]
    public void Scout_ReadsAGlobWithG()
    {
        Assert.Equal(["Series 01.mkv", "Series 01.srt"],
                     Marked(Manager(), "scout -mg ^Series 01.*"));
    }

    [Fact]
    public void Scout_IsCaseSensitiveByDefault()
    {
        Assert.Empty(Marked(Manager(), "scout -m ^series"));
    }

    [Fact]
    public void Scout_IgnoresCaseWithI()
    {
        Assert.Equal(["Series 01.mkv", "Series 01.srt", "Series 02.mkv"],
                     Marked(Manager(), "scout -mi ^series"));
    }

    [Fact]
    public void Scout_SmartCaseIgnoresCaseOnlyForALowercasePattern()
    {
        // Which is what makes a lowercase search forgiving and a capitalised one deliberate.
        Assert.Equal(["Series 01.mkv", "Series 01.srt", "Series 02.mkv"],
                     Marked(Manager(), "scout -ms ^series"));
        Assert.Empty(Marked(Manager(), "scout -ms ^SERIES"));
    }

    [Fact]
    public void Scout_MatchesEverythingForALoneDot()
    {
        Assert.Equal(6, Marked(Manager(), "scout -m .").Length);
    }

    [Fact]
    public void Scout_SurvivesAHalfTypedRegularExpression()
    {
        // Normal while typing one. Ranger falls back to matching everything rather than
        // refusing, and refusing here would mean an exception out of a keystroke.
        Assert.Equal(6, Marked(Manager(), "scout -mr ^Series 0[").Length);
    }

    [Fact]
    public void Scout_InvertsWithV()
    {
        Assert.Equal(["Other Film.mkv", "notes (2020).txt", "prequel to Series 01.mkv"],
                     Marked(Manager(), "scout -mv ^Series"));
    }
}
