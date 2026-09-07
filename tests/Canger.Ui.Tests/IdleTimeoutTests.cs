// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// How long the main loop may sleep before looking at the screen again.
/// </summary>
/// <remarks>
/// Measured on the real thing: audio started at once while its clock took 2.11 s to appear and
/// then moved in 2.0 s steps. mpv reports eight times a second; the loop was looking twice a
/// minute, because only a *queued* job shortened the wait and playback is not one.
/// </remarks>
public class IdleTimeoutTests
{
    private const int IdleDelay = 2000;

    private static int Timeout(bool pendingInput = false, bool needsRedraw = false,
                               bool hasWork = false, int taskDelay = 30, bool hasActivity = false) =>
        Browser.IdleTimeout(pendingInput, needsRedraw, hasWork,
                            TimeSpan.FromMilliseconds(taskDelay), hasActivity, IdleDelay);

    [Fact]
    public void AnIdleBrowserWaitsTheWholeIdleDelay()
    {
        Assert.Equal(IdleDelay, Timeout());
    }

    [Fact]
    public void SomethingReportingOutsideTheQueueShortensTheWait()
    {
        // The defect. Audio playing is not a queued job, so nothing shortened the wait and its
        // clock crawled.
        int timeout = Timeout(hasActivity: true);

        Assert.True(timeout < IdleDelay, "a background activity did not shorten the wait");
        Assert.True(timeout <= 500, $"{timeout} ms is too long for a clock counting in seconds");
    }

    [Fact]
    public void AQueuedJobStillDecidesForItself()
    {
        // A copy asks to be looked at every few milliseconds; an activity must not slow that down.
        Assert.Equal(30, Timeout(hasWork: true, taskDelay: 30, hasActivity: true));
    }

    [Fact]
    public void AKeystrokeInFlightOutranksEverything()
    {
        Assert.True(Timeout(pendingInput: true, hasWork: true, hasActivity: true) < 500);
    }

    [Fact]
    public void SomethingAnsweringFromAnotherThreadDrawsAtOnce()
    {
        Assert.Equal(0, Timeout(needsRedraw: true, hasActivity: true));
    }

    [Fact]
    public void AnIdleDelayShorterThanTheActivityIntervalIsRespected()
    {
        // Somebody who has asked for a livelier interface should not be slowed down by this.
        Assert.Equal(100,
                     Browser.IdleTimeout(false, false, false, TimeSpan.Zero, true, idleDelay: 100));
    }
}
