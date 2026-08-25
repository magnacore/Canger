// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The window listing what can follow a partly-typed key sequence.
/// </summary>
public class HintWindowTests
{
    /// <summary>A key map with a handful of g-bindings, and a buffer part-way through one.</summary>
    private static KeyBuffer AfterPressing(char key, params string[] bindings)
    {
        KeyMap map = new();

        foreach (string binding in bindings)
        {
            int space = binding.IndexOf(' ', StringComparison.Ordinal);
            map.Bind(binding[..space], binding[(space + 1)..]);
        }

        KeyBuffer keys = new(map);
        keys.Add(key);
        return keys;
    }

    private static HintWindow Window(KeyBuffer keys, int threshold = 10) =>
        new(new DefaultColorScheme()) { Position = keys.Position, CollapseThreshold = threshold };

    [Fact]
    public void Hints_ListEveryContinuationOfThePartlyTypedSequence()
    {
        KeyBuffer keys = AfterPressing('g', "gg move to=0", "gh cd ~", "gn tab_new");
        HintWindow window = Window(keys);

        Assert.Equal(3, window.Hints.Count);
        Assert.Contains(window.Hints, h => h.Keys == "g" && h.Command == "move to=0");
        Assert.Contains(window.Hints, h => h.Keys == "h" && h.Command == "cd ~");
    }

    [Fact]
    public void Hints_ReachBindingsSeveralKeysDeep()
    {
        KeyBuffer keys = AfterPressing('g', "gab first", "gcd second");
        HintWindow window = Window(keys);

        Assert.Contains(window.Hints, h => h.Keys == "ab");
        Assert.Contains(window.Hints, h => h.Keys == "cd");
    }

    [Fact]
    public void Hints_AreEmptyWhenNothingHasBeenTyped()
    {
        HintWindow window = new(new DefaultColorScheme());

        Assert.Empty(window.Hints);
    }

    [Fact]
    public void Hints_CollapseAGroupWhenThereAreTooManyToRead()
    {
        // Forty lines of bindings would bury the answer rather than give it.
        KeyBuffer keys = AfterPressing('g',
            "ga one", "gb two", "gcc three", "gcd four", "gce five");

        HintWindow window = Window(keys, threshold: 3);

        Assert.Contains(window.Hints, h => h.Keys == "c" && h.Command == "...");
        Assert.DoesNotContain(window.Hints, h => h.Keys == "cc");
    }

    [Fact]
    public void Hints_LeaveSingleBindingsAloneWhenCollapsing()
    {
        KeyBuffer keys = AfterPressing('g', "ga one", "gb two", "gcc three", "gcd four");
        HintWindow window = Window(keys, threshold: 2);

        Assert.Contains(window.Hints, h => h.Keys == "a" && h.Command == "one");
        Assert.Contains(window.Hints, h => h.Keys == "c" && h.Command == "...");
    }

    [Fact]
    public void Draw_ShowsTheKeyAndTheCommandUnderAHeading()
    {
        KeyBuffer keys = AfterPressing('g', "gg move to=0", "gh cd ~");
        HintWindow window = Window(keys);
        window.IsVisible = true;
        window.Layout(new Rect(0, 0, 40, 4));

        ScreenBuffer screen = new(40, 4);
        window.Render(screen);

        string all = string.Concat(Enumerable.Range(0, 4).Select(screen.TextAt));
        Assert.Contains("key", all, StringComparison.Ordinal);
        Assert.Contains("command", all, StringComparison.Ordinal);
        Assert.Contains("move to=0", all, StringComparison.Ordinal);
    }

    [Fact]
    public void Hints_SkipABindingThatWouldOnlyShowTheseAgain()
    {
        KeyBuffer keys = AfterPressing('g', "gg move to=0", "g? hint something");
        HintWindow window = Window(keys);

        Assert.DoesNotContain(window.Hints, h => h.Command.StartsWith("hint",
                                                                     StringComparison.Ordinal));
    }
}
