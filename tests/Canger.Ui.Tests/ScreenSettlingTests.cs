// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Painting in full for a while after another program has had the terminal.
/// </summary>
/// <remarks>
/// Reported against a <c>.verso</c> file, which no rule claimed: it fell through to
/// <c>xdg-open</c>, which started a graphical editor and returned at once. A second later the
/// editor — still attached to the terminal — wrote a sandbox warning onto it, one line at the
/// bottom scrolled the screen, and every frame after that was drawn a row out of place. The
/// frames are differences against what Canger believes is on screen, and that belief had become
/// wrong in every cell, so nothing put it right. <c>&lt;C-l&gt;</c> did, which is what showed the
/// repair was there and only the trigger was missing.
/// </remarks>
public class ScreenSettlingTests
{
    private const long Now = 1_000_000;

    [Fact]
    public void AFrameDuringTheWindowIsWrittenInFull()
    {
        long until = ScreenSettling.From(Now);

        Assert.True(ScreenSettling.Covers(Now, until));
        Assert.True(ScreenSettling.Covers(Now + 3_000, until));
    }

    [Fact]
    public void TheWindowCoversAGraphicalProgramsFirstComplaints()
    {
        // The reported line arrived a second or two after the launcher returned, which is when
        // they nearly always do; a couple of seconds of cover would have been a coin toss.
        Assert.True(ScreenSettling.Covers(Now + 5_000, ScreenSettling.From(Now)));
    }

    [Fact]
    public void TheWindowCloses()
    {
        // It has to end: every frame written in full for the rest of the session would be paid
        // for on every keystroke, and over a slow link that is felt.
        Assert.False(ScreenSettling.Covers(Now + 60_000, ScreenSettling.From(Now)));
    }

    [Fact]
    public void NoProgramHasRunSoNothingIsForced()
    {
        Assert.False(ScreenSettling.Covers(Now, 0));
    }

    [Fact]
    public void TheLoopLooksUpOftenEnoughToRepairWithoutAKeystroke()
    {
        // The defect this half answers: the loop sleeps for the whole idle delay, so a flag
        // alone would leave the screen wrong until something was typed. The resize path made the
        // same mistake once and was fixed the same way.
        int timeout = ScreenSettling.Clamp(2000, Now, ScreenSettling.From(Now));

        Assert.True(timeout <= 500, $"{timeout} ms is too long to leave a broken screen up");
    }

    [Fact]
    public void AShorterWaitIsLeftAlone()
    {
        // A job asking to be looked at every 30 ms must not be slowed to 500.
        Assert.Equal(30, ScreenSettling.Clamp(30, Now, ScreenSettling.From(Now)));
    }

    [Fact]
    public void AnIdleBrowserKeepsItsIdleDelayOnceTheWindowHasClosed()
    {
        Assert.Equal(2000, ScreenSettling.Clamp(2000, Now + 60_000, ScreenSettling.From(Now)));
        Assert.Equal(2000, ScreenSettling.Clamp(2000, Now, 0));
    }
}
