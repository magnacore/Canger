// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Tab moving through the completions rather than picking one and stopping.
/// </summary>
/// <remarks>
/// The cycle was written correctly and then defeated from outside. Candidates are recomputed from
/// the console's current text on every Tab — and after the first Tab that text is no longer
/// <c>f</c> but <c>fd_next </c>, which takes the argument-completion path and comes back empty.
/// The empty list hit an early return before the "a cycle is already running" check, so the
/// second Tab did nothing and completion looked like a one-shot stuck on the first match.
/// </remarks>
public class CompletionCyclingTests
{
    private static ConsoleWidget Open(string text)
    {
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Layout(new Rect(0, 0, 80, 1));
        console.Open(text);
        return console;
    }

    private static readonly string[] Candidates =
        ["fd_next ", "fd_prev ", "filter ", "flat "];

    [Fact]
    public void TabMovesToTheFirstCandidate()
    {
        ConsoleWidget console = Open("f");

        console.CycleCompletions(Candidates, 1);

        Assert.Equal("fd_next ", console.Text);
    }

    [Fact]
    public void TabAgainMovesToTheNext()
    {
        // The reported bug: this used to stay on the first match for ever.
        ConsoleWidget console = Open("f");

        console.CycleCompletions(Candidates, 1);
        console.CycleCompletions([], 1);

        Assert.Equal("fd_prev ", console.Text);
    }

    [Fact]
    public void TabKeepsGoingThroughThemAll()
    {
        ConsoleWidget console = Open("f");
        List<string> seen = [];

        for (int i = 0; i < 4; i++)
        {
            console.CycleCompletions(i == 0 ? Candidates : [], 1);
            seen.Add(console.Text);
        }

        Assert.Equal(["fd_next ", "fd_prev ", "filter ", "flat "], seen);
    }

    [Fact]
    public void TabWrapsBackToWhatWasTyped()
    {
        // The typed line is part of the cycle, so a wrong guess can be walked out of.
        ConsoleWidget console = Open("f");

        for (int i = 0; i < 5; i++)
        {
            console.CycleCompletions(i == 0 ? Candidates : [], 1);
        }

        Assert.Equal("f", console.Text);
    }

    [Fact]
    public void ShiftTabGoesBack()
    {
        ConsoleWidget console = Open("f");

        console.CycleCompletions(Candidates, 1);
        console.CycleCompletions([], 1);
        console.CycleCompletions([], -1);

        Assert.Equal("fd_next ", console.Text);
    }

    [Fact]
    public void TypingStartsAFreshCycle()
    {
        // Otherwise the old list would be cycled against text it no longer matches.
        ConsoleWidget console = Open("f");
        console.CycleCompletions(Candidates, 1);

        console.TypeKey('x');

        console.CycleCompletions(["fx_one "], 1);
        Assert.Equal("fx_one ", console.Text);
    }

    [Fact]
    public void NothingHappensWithNoCandidatesAndNoCycleRunning()
    {
        ConsoleWidget console = Open("zzz");

        console.CycleCompletions([], 1);

        Assert.Equal("zzz", console.Text);
    }
}
