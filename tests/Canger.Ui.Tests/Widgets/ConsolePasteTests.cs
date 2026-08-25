// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Putting pasted text into the prompt.
/// </summary>
/// <remarks>
/// Canger enables bracketed paste, so a terminal wraps a paste in markers and it arrives as one
/// event rather than as keystrokes — and nothing consumed that event, so pasting a path into
/// <c>:cd</c> did nothing whatsoever. Ranger appears to work here only because it never enables
/// bracketed paste and the terminal sends plain key presses; the cost of that is that a paste into
/// ranger's browser runs whatever bindings the characters name.
/// </remarks>
public class ConsolePasteTests
{
    private static ConsoleWidget Open()
    {
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Open();

        return console;
    }

    [Fact]
    public void Insert_PutsTextAtTheCursor()
    {
        ConsoleWidget console = Open();
        console.Insert("cd ");
        console.Insert("/tmp");

        Assert.Equal("cd /tmp", console.Text);
    }

    [Fact]
    public void Insert_GoesWhereTheCursorIsRatherThanAtTheEnd()
    {
        ConsoleWidget console = Open();
        console.Insert("cd end");
        console.MoveCursor(-3);
        console.Insert("the ");

        Assert.Equal("cd the end", console.Text);
    }

    [Fact]
    public void Insert_LeavesTheCursorAfterWhatWasPasted()
    {
        ConsoleWidget console = Open();
        console.Insert("/tmp/x");

        Assert.Equal(6, console.CursorPosition);
    }

    [Fact]
    public void Insert_TakesAPathWithSpacesIntact()
    {
        // Every one of the real bindings points at a path with spaces in it.
        ConsoleWidget console = Open();
        console.Insert("/home/manuj/04 BIN/RA RP SP");

        Assert.Equal("/home/manuj/04 BIN/RA RP SP", console.Text);
    }
}
