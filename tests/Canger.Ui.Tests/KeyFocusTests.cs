// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Which part of the interface a keystroke belongs to.
/// </summary>
/// <remarks>
/// Everything that draws over the browser takes the browser's keys with it. This was a chain of
/// <c>if</c>s, each ending in a <c>continue</c>, and the day one <c>continue</c> was left out
/// every keystroke in the device list ran twice — <c>q</c> closed the list and then quit Canger.
/// Nothing failed and nothing was logged, because both halves did exactly what they were bound to
/// do. The ordering is a function now so that it can be stated rather than inferred.
/// </remarks>
public class KeyFocusTests
{
    [Fact]
    public void WithNothingOverItTheBrowserGetsTheKey()
    {
        Assert.Equal(Browser.KeyTarget.Browser,
                     Browser.FocusedOn(console: false, pager: false, devices: false,
                                       taskView: false));
    }

    [Fact]
    public void WhateverIsShowingTakesTheKey()
    {
        Assert.Equal(Browser.KeyTarget.Console,
                     Browser.FocusedOn(true, false, false, false));
        Assert.Equal(Browser.KeyTarget.Pager,
                     Browser.FocusedOn(false, true, false, false));
        Assert.Equal(Browser.KeyTarget.Devices,
                     Browser.FocusedOn(false, false, true, false));
        Assert.Equal(Browser.KeyTarget.TaskView,
                     Browser.FocusedOn(false, false, false, true));
    }

    [Fact]
    public void TheDeviceListNeverSharesAKeyWithTheBrowser()
    {
        // The bug itself: `q` is close-the-list here and quit there, and it ran as both.
        Assert.NotEqual(Browser.KeyTarget.Browser,
                        Browser.FocusedOn(console: false, pager: false, devices: true,
                                          taskView: false));
    }

    [Fact]
    public void TheConsoleOutranksEverythingElse()
    {
        // Typing a filename that happens to contain `q` must not close anything.
        Assert.Equal(Browser.KeyTarget.Console,
                     Browser.FocusedOn(console: true, pager: true, devices: true,
                                       taskView: true));
    }

    [Fact]
    public void EveryCombinationChoosesExactlyOne()
    {
        // There is no combination with no answer, which is the other way the chain could have
        // gone wrong: a key belonging to nothing at all.
        foreach (bool console in new[] { false, true })
        {
            foreach (bool pager in new[] { false, true })
            {
                foreach (bool devices in new[] { false, true })
                {
                    foreach (bool taskView in new[] { false, true })
                    {
                        Assert.True(
                            Enum.IsDefined(Browser.FocusedOn(console, pager, devices, taskView)));
                    }
                }
            }
        }
    }
}
