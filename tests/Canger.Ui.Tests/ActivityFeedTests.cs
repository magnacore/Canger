// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// What the status bar is told about a background activity, and what it is told when there is no
/// plugin to tell it anything.
/// </summary>
/// <remarks>
/// A <see cref="Browser"/> cannot be built without a terminal, so the decision is a static rule
/// and this is where it is pinned. The alternative — asserting on a screen driven through a pty —
/// cannot see how many times the activity was asked, which is half of what matters here.
/// </remarks>
public class ActivityFeedTests
{
    /// <summary>An activity that counts how often it is asked.</summary>
    private sealed class Fake(string? line, string? badge, double? progress) : IBackgroundActivity
    {
        public int Asked { get; private set; }

        public string? Badge => badge;

        public double? Progress => progress;

        public string? Describe()
        {
            Asked++;
            return line;
        }
    }

    [Fact]
    public void WithNoActivityTheBarIsToldNothing()
    {
        // The state after removing the plugin: nothing feeds any of this, and nothing here holds
        // state between frames, so the bar is what it was before the plugin existed.
        Assert.Equal((null, null, null), Browser.ActivityFor(null, headlineTaken: false));
        Assert.Equal((null, null, null), Browser.ActivityFor(null, headlineTaken: true));
    }

    [Fact]
    public void TheLineAndItsProgressAreGivenWhileTheBarIsFree()
    {
        Fake activity = new("00:04:21 / 00:06:44", "MPV", 0.5);

        Assert.Equal(("MPV", "00:04:21 / 00:06:44", 0.5),
                     Browser.ActivityFor(activity, headlineTaken: false));
    }

    [Fact]
    public void TheBadgeIsGivenWithNoLineToGoWith()
    {
        // mpv has not said anything yet, but the keyboard is already handed over. Passing the
        // badge on only with a line is what left the flag out at exactly that moment.
        Fake activity = new(null, "MPV", null);

        Assert.Equal(("MPV", null, null), Browser.ActivityFor(activity, headlineTaken: false));
    }

    [Fact]
    public void AHeadlineTakesTheLineButNotTheBadge()
    {
        // The bar decides it cannot draw the badge under a headline; this is only about what it
        // is told. Nulling the badge here instead would mean the keyboard's owner going unnamed
        // for as long as a copy ran even once the copy's line was gone.
        Fake activity = new("00:04:21 / 00:06:44", "MPV", 0.5);

        Assert.Equal(("MPV", null, null), Browser.ActivityFor(activity, headlineTaken: true));
    }

    [Fact]
    public void ItIsAskedOnceAFrameEvenUnderAHeadline()
    {
        // Describe() is the plugin's only heartbeat: it is where mpv's output is drained and
        // where its exit is noticed. Skipping it while a task held the bar left the pipe filling
        // and the keyboard grab pointing at a process that had ended.
        Fake activity = new("00:04:21 / 00:06:44", "MPV", 0.5);

        Browser.ActivityFor(activity, headlineTaken: true);
        Browser.ActivityFor(activity, headlineTaken: false);

        Assert.Equal(2, activity.Asked);
    }
}
