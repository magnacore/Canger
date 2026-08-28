// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The console when what is being typed is a passphrase.
/// </summary>
/// <remarks>
/// The same line editor as ever, with four differences, every one of them about where the
/// passphrase must not go: not onto the screen, not into the history that is written to disc, not
/// into a completion, and not back out of the history on an Up arrow.
/// </remarks>
public class HiddenConsoleTests
{
    private const int Width = 60;

    private static (ConsoleWidget Console, ScreenBuffer Screen) Build(bool hidden)
    {
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Open(prompt: "Passphrase: ", hidden: hidden);
        console.Layout(new Rect(0, 0, Width, 1));

        return (console, new ScreenBuffer(Width, 1));
    }

    private static string Render(ConsoleWidget console, ScreenBuffer screen)
    {
        console.Render(screen);
        return screen.TextAt(0);
    }

    [Fact]
    public void WhatIsTypedIsNotDrawn()
    {
        (ConsoleWidget console, ScreenBuffer screen) = Build(hidden: true);
        console.Insert("hunter2");

        string line = Render(console, screen);

        Assert.DoesNotContain("hunter2", line, StringComparison.Ordinal);
        Assert.Contains("Passphrase:", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ABulletIsDrawnForEachCharacterTyped()
    {
        // Not nothing at all: the line has to show that something is being typed, and how much,
        // or a keystroke that did not register is invisible.
        (ConsoleWidget console, ScreenBuffer screen) = Build(hidden: true);
        console.Insert("hunter2");

        Assert.Equal(7, Render(console, screen).Count(c => c == '•'));
    }

    [Fact]
    public void AnOrdinaryLineIsStillDrawnAsItsText()
    {
        (ConsoleWidget console, ScreenBuffer screen) = Build(hidden: false);
        console.Insert("chmod 755");

        Assert.Contains("chmod 755", Render(console, screen), StringComparison.Ordinal);
    }

    [Fact]
    public void APassphraseIsNotWrittenToTheHistory()
    {
        // The history goes to ~/.local/share/canger, so a passphrase reaching it would be a
        // passphrase in a plain file — and would come back on the next Up arrow.
        (ConsoleWidget console, _) = Build(hidden: true);
        console.Insert("hunter2");

        console.Accept();

        Assert.DoesNotContain("hunter2", console.CommandHistory.Entries, StringComparer.Ordinal);
    }

    [Fact]
    public void AnOrdinaryLineIsStillRemembered()
    {
        (ConsoleWidget console, _) = Build(hidden: false);
        console.Insert("chmod 755");

        console.Accept();

        Assert.Contains("chmod 755", console.CommandHistory.Entries, StringComparer.Ordinal);
    }

    [Fact]
    public void TheHistoryCannotBeBrowsedIntoAPassphrasePrompt()
    {
        // Otherwise an Up arrow puts the last command where a passphrase is being typed, and the
        // Return that follows sends it to udisks.
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Open();
        console.Insert("chmod 755");
        console.Accept();

        console.Open(prompt: "Passphrase: ", hidden: true);
        console.HistoryMove(-1);

        Assert.Equal(string.Empty, console.Text);
    }

    [Fact]
    public void NothingCompletesIntoAPassphrasePrompt()
    {
        (ConsoleWidget console, _) = Build(hidden: true);

        console.CycleCompletions(["/home/manuj/secret.txt"], 1);

        Assert.Equal(string.Empty, console.Text);
    }

    [Fact]
    public void TheHiddenFlagIsClearedWhenTheConsoleCloses()
    {
        // Or the next ordinary command would be typed as bullets and kept out of the history.
        (ConsoleWidget console, _) = Build(hidden: true);

        console.Close();

        Assert.False(console.IsHidden);
    }

    [Fact]
    public void TheCursorSitsAfterTheBulletsRatherThanAfterTheText()
    {
        // A wide character would otherwise put the terminal's cursor somewhere the bullets are
        // not, which is both wrong and a hint about what was typed.
        (ConsoleWidget console, _) = Build(hidden: true);
        console.Insert("日本語");

        Assert.Equal("Passphrase: ".Length + 3, console.ScreenCursorX);
    }
}
