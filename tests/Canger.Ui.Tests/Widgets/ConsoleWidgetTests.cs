// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

public class ConsoleWidgetTests
{
    private static ConsoleWidget Open(string text = "", int cursor = -1)
    {
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Open(text, cursor);
        return console;
    }

    private static void Type(ConsoleWidget console, string text)
    {
        foreach (char c in text)
        {
            console.TypeKey(c);
        }
    }

    [Fact]
    public void Open_StartsEmptyWithTheCursorAtTheEnd()
    {
        ConsoleWidget console = Open();

        Assert.True(console.IsOpen);
        Assert.Equal(string.Empty, console.Text);
        Assert.Equal(0, console.CursorPosition);
    }

    [Fact]
    public void Open_CanPreFillTheLineAndPlaceTheCursor()
    {
        // Bindings use this to open a partly typed command with the cursor where the user needs
        // to continue.
        ConsoleWidget console = Open("shell  %s", 6);

        Assert.Equal("shell  %s", console.Text);
        Assert.Equal(6, console.CursorPosition);
    }

    [Fact]
    public void TypeKey_InsertsCharactersAtTheCursor()
    {
        ConsoleWidget console = Open();
        Type(console, "hello");

        Assert.Equal("hello", console.Text);
        Assert.Equal(5, console.CursorPosition);
    }

    [Fact]
    public void TypeKey_InsertsInTheMiddle()
    {
        ConsoleWidget console = Open("helo", 3);
        Type(console, "l");

        Assert.Equal("hello", console.Text);
    }

    [Fact]
    public void TypeKey_ReassemblesAMultiByteCharacter()
    {
        // Keys arrive as bytes so bindings can match non-ASCII keys; the console is the only
        // place that needs whole characters, so it is the only place that reassembles them.
        ConsoleWidget console = Open();

        foreach (byte b in System.Text.Encoding.UTF8.GetBytes("ö"))
        {
            console.TypeKey(b);
        }

        Assert.Equal("ö", console.Text);
    }

    [Fact]
    public void TypeKey_ReassemblesAThreeByteCharacter()
    {
        ConsoleWidget console = Open();

        foreach (byte b in System.Text.Encoding.UTF8.GetBytes("モ"))
        {
            console.TypeKey(b);
        }

        Assert.Equal("モ", console.Text);
    }

    [Fact]
    public void Delete_RemovesBackwardsAndForwards()
    {
        ConsoleWidget console = Open("hello", 5);

        Assert.True(console.Delete(-1));
        Assert.Equal("hell", console.Text);

        console.MoveCursor(-2);
        console.Delete(0);
        Assert.Equal("hel", console.Text);
    }

    [Fact]
    public void Delete_BackwardsOnAnEmptyLineAsksToClose()
    {
        // Backspacing out of an empty prompt dismisses it, so the console can be abandoned
        // without reaching for Escape.
        ConsoleWidget console = Open();

        Assert.False(console.Delete(-1));
    }

    [Fact]
    public void MoveCursor_ClampsToTheLine()
    {
        ConsoleWidget console = Open("abc");

        console.MoveCursor(-99);
        Assert.Equal(0, console.CursorPosition);

        console.MoveCursor(99);
        Assert.Equal(3, console.CursorPosition);
    }

    [Fact]
    public void MoveCursorByWord_StepsOverWords()
    {
        ConsoleWidget console = Open("one two three", 13);

        console.MoveCursorByWord(-1);
        Assert.Equal(8, console.CursorPosition);

        console.MoveCursorByWord(-1);
        Assert.Equal(4, console.CursorPosition);

        console.MoveCursorByWord(1);
        Assert.Equal(7, console.CursorPosition);
    }

    [Fact]
    public void DeleteWord_RemovesTheWordBeforeTheCursor()
    {
        ConsoleWidget console = Open("delete this word", 16);

        console.DeleteWord();

        Assert.Equal("delete this ", console.Text);
    }

    [Fact]
    public void DeleteRest_KillsToEitherEnd()
    {
        ConsoleWidget console = Open("keep this away", 9);
        console.DeleteRest(1);
        Assert.Equal("keep this", console.Text);

        console = Open("away keep this", 5);
        console.DeleteRest(-1);
        Assert.Equal("keep this", console.Text);
    }

    [Fact]
    public void Paste_RestoresWhatWasLastDeleted()
    {
        ConsoleWidget console = Open("hello world", 11);

        console.DeleteWord();
        console.Paste();

        Assert.Equal("hello world", console.Text);
    }

    [Fact]
    public void Accept_RecordsTheLineAndCloses()
    {
        ConsoleWidget console = Open("echo hi");

        string line = console.Accept();

        Assert.Equal("echo hi", line);
        Assert.False(console.IsOpen);
        Assert.Contains("echo hi", console.CommandHistory.Entries, StringComparer.Ordinal);
    }

    [Fact]
    public void Accept_DoesNotRecordAnEmptyLine()
    {
        ConsoleWidget console = Open("   ");
        console.Accept();

        Assert.Empty(console.CommandHistory.Entries);
    }

    [Fact]
    public void HistoryMove_WalksPreviousLines()
    {
        ConsoleWidget console = Open("first");
        console.Accept();
        console.Open("second");
        console.Accept();

        console.Open();
        console.HistoryMove(-1);

        Assert.Equal("second", console.Text);
    }

    [Fact]
    public void HistoryMove_FiltersByWhatHasBeenTyped()
    {
        // Typing "cd" and pressing up should walk only the previous cd commands.
        ConsoleWidget console = Open("cd /tmp");
        console.Accept();
        console.Open("echo hello");
        console.Accept();
        console.Open("cd /home");
        console.Accept();

        console.Open();
        Type(console, "cd");
        console.HistoryMove(-1);

        Assert.Equal("cd /home", console.Text);
    }

    [Fact]
    public void CycleCompletions_OffersTheTypedLineFirstSoItCanBeReturnedTo()
    {
        ConsoleWidget console = Open("cd d");

        console.CycleCompletions(["cd docs/", "cd downloads/"], 1);
        Assert.Equal("cd docs/", console.Text);

        console.CycleCompletions(["cd docs/", "cd downloads/"], 1);
        Assert.Equal("cd downloads/", console.Text);

        // Cycling past the end returns to what was typed, rather than trapping the user in the
        // candidate list.
        console.CycleCompletions(["cd docs/", "cd downloads/"], 1);
        Assert.Equal("cd d", console.Text);
    }

    [Fact]
    public void CycleCompletions_GoesBackwards()
    {
        ConsoleWidget console = Open("cd d");

        console.CycleCompletions(["cd docs/", "cd downloads/"], -1);

        Assert.Equal("cd downloads/", console.Text);
    }

    [Fact]
    public void Typing_AbandonsTheCompletionCycle()
    {
        ConsoleWidget console = Open("cd d");
        console.CycleCompletions(["cd docs/"], 1);

        Type(console, "x");

        Assert.Equal("cd docs/x", console.Text);
    }

    // ---- Questions -------------------------------------------------------------------

    [Fact]
    public void Ask_AcceptsOneOfTheOfferedAnswers()
    {
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Ask("Delete 3 files?", ['y', 'n']);

        Assert.Equal('y', console.AnswerQuestion('y'));
        Assert.Equal('n', console.AnswerQuestion('n'));
        Assert.Null(console.AnswerQuestion('x'));
    }

    [Fact]
    public void Ask_TakesTheDefaultOnEnterAndDeclinesOnEscape()
    {
        // A confirmation must never trap the user: both keys always have a meaning.
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Ask("Delete?", ['y', 'n']);

        Assert.Equal('y', console.AnswerQuestion(KeyCodes.Enter));
        Assert.Equal('n', console.AnswerQuestion(KeyCodes.Escape));
    }

    // ---- Rendering -------------------------------------------------------------------

    [Fact]
    public void Draw_ShowsThePromptAndTheLine()
    {
        ConsoleWidget console = Open("echo hello");
        console.Layout(new Rect(0, 0, 40, 1));
        ScreenBuffer screen = new(40, 1);

        console.Render(screen);

        Assert.StartsWith(":echo hello", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsAQuestionWithItsChoices()
    {
        ConsoleWidget console = new(new DefaultColorScheme());
        console.Ask("Delete 3 files?", ['y', 'n']);
        console.Layout(new Rect(0, 0, 40, 1));
        ScreenBuffer screen = new(40, 1);

        console.Render(screen);

        Assert.StartsWith("Delete 3 files? [y/n]", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ScrollsToKeepTheCursorVisibleOnALongLine()
    {
        string line = new('x', 100);
        ConsoleWidget console = Open(line);
        console.Layout(new Rect(0, 0, 20, 1));
        ScreenBuffer screen = new(20, 1);

        console.Render(screen);

        // The row is full, which means the tail of the line is showing rather than the head.
        Assert.Equal(20, screen.TextAt(0).TrimEnd().Length);
    }
}
