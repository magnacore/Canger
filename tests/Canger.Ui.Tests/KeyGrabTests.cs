// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests;

/// <summary>
/// The badge a background activity puts in front of its line.
/// </summary>
/// <remarks>
/// For a state rather than a figure: while the keyboard belongs to something else every key does
/// something different, and the bar has to say so at a glance.
/// </remarks>
public class ActivityBadgeTests
{
    private const int Width = 160;

    private static (StatusBar Bar, ScreenBuffer Screen) Build()
    {
        InMemoryFileSystem fs = new();
        fs.AddFileOfSize("/home/a.txt", 8, DateTimeOffset.UnixEpoch);
        fs.AddFile("/home/b.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 1_000_000_000 };
        bar.Layout(new Rect(0, 0, Width, 1));

        return (bar, new ScreenBuffer(Width, 1));
    }

    [Fact]
    public void ItSitsWithTheFlagsAtTheRightHandEnd()
    {
        // Where this bar says what state you are in: `Mrk`, `VIS` and `FROZEN` are all there, and
        // the eye already goes there for them. In front of the activity line it was the one flag
        // in a place no other flag appears.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ActivityDescription = "00:04:21 / 00:06:44";
        bar.ActivityBadge = "MPV";

        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.True(line.IndexOf("MPV", StringComparison.Ordinal) >
                    line.IndexOf("00:04:21", StringComparison.Ordinal),
                    "the badge is not past the line it belongs to");
        Assert.EndsWith("MPV", line.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void ItIsSpacedAndPaddedLikeTheFlagsBesideIt()
    {
        // Two columns between the flags, one off the right edge, and the highlighted block is the
        // word itself: a badge that padded itself would be two columns wider than `VIS` for a
        // word of the same length, and would not line up with it.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.IsVisualMode = true;
        bar.ActivityBadge = "MPV";

        bar.Render(screen);
        string line = screen.TextAt(0);
        int badge = line.IndexOf("MPV", StringComparison.Ordinal);
        int flag = line.IndexOf("VIS", StringComparison.Ordinal);

        Assert.Equal(flag + "VIS".Length + 2, badge);
        Assert.Equal(" ", line[(badge + 3)..]);
        Assert.Equal(screen[flag, 0].Style, screen[badge, 0].Style);
        Assert.NotEqual(screen[badge, 0].Style, screen[badge - 1, 0].Style);
        Assert.NotEqual(screen[badge, 0].Style, screen[badge + 3, 0].Style);
    }

    [Fact]
    public void ItIsShownWithNoLineToGoWith()
    {
        // It names a state rather than decorating a figure, so it is drawn while the state holds.
        // Requiring a line meant the badge was missing for as long as mpv had not yet said
        // anything — with the keyboard handed over the whole time, which is precisely when the
        // user needs telling.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ActivityBadge = "MPV";

        bar.Render(screen);

        Assert.Contains("MPV", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void NoBadgeLeavesTheFlagsAsTheyWere()
    {
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.ActivityDescription = "00:04:21 / 00:06:44";
        bar.ActivityBadge = null;

        bar.Render(screen);

        Assert.Contains("00:04:21", screen.TextAt(0), StringComparison.Ordinal);
        Assert.DoesNotContain("MPV", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadlineTakesTheBarAndEveryFlagWithIt()
    {
        // A queued task replaces the whole bar, as a message does, so `Mrk` and `VIS` go too.
        // The badge is one of them now and goes the same way.
        (StatusBar bar, ScreenBuffer screen) = Build();
        bar.IsVisualMode = true;
        bar.ActivityBadge = "MPV";
        bar.TaskDescription = "Copying 3 files";

        bar.Render(screen);
        string line = screen.TextAt(0);

        Assert.Contains("Copying 3 files", line, StringComparison.Ordinal);
        Assert.DoesNotContain("MPV", line, StringComparison.Ordinal);
        Assert.DoesNotContain("VIS", line, StringComparison.Ordinal);
    }
}

/// <summary>
/// Something holding the keyboard ahead of the browser's own bindings.
/// </summary>
/// <remarks>
/// Added so a plugin can hand the keys to a program Canger is running — mpv's <c>[</c> and
/// <c>]</c> for speed, <c>8</c> and <c>9</c> for volume, every one of which means something else
/// to the browser. What matters is the ordering: after the overlays, before the bindings.
/// </remarks>
public class KeyGrabTests
{
    /// <summary>A grab that takes one particular key and notes everything it is offered.</summary>
    private sealed class Grab(int taking) : IKeyGrab
    {
        public List<int> Seen { get; } = [];

        public bool Handle(int key)
        {
            Seen.Add(key);
            return key == taking;
        }
    }

    [Fact]
    public void ItTakesAKeyMeantForTheBrowser()
    {
        Grab grab = new(taking: 'j');

        Assert.True(Browser.GrabTakes(Browser.KeyTarget.Browser, grab, 'j'));
        Assert.Equal(['j'], grab.Seen);
    }

    [Fact]
    public void AKeyItDeclinesGoesOnToTheBindings()
    {
        // Declining matters as much as taking: a grab that swallowed everything would make even
        // its own way out unreachable if it forgot one key.
        Grab grab = new(taking: 'j');

        Assert.False(Browser.GrabTakes(Browser.KeyTarget.Browser, grab, 'k'));
        Assert.Equal(['k'], grab.Seen);
    }

    [Fact]
    public void ItIsNotEvenAskedWhileSomethingElseIsUp()
    {
        // The console, the pager, the task view and the device list are things the user opened and
        // has to be able to close. A grab that swallowed the key closing one would be a trap.
        foreach (Browser.KeyTarget target in
                 new[] { Browser.KeyTarget.Console, Browser.KeyTarget.Pager,
                         Browser.KeyTarget.Devices, Browser.KeyTarget.TaskView })
        {
            Grab grab = new(taking: 'j');

            Assert.False(Browser.GrabTakes(target, grab, 'j'), $"{target} lost its key");
            Assert.Empty(grab.Seen);
        }
    }

    [Fact]
    public void NothingHoldingTheKeyboardChangesNothing()
    {
        Assert.False(Browser.GrabTakes(Browser.KeyTarget.Browser, null, 'j'));
    }
}
