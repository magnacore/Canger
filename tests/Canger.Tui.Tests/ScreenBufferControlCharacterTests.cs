// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Tui.Rendering;

namespace Canger.Tui.Tests;

/// <summary>
/// Characters that would steer the terminal instead of filling a cell.
/// </summary>
/// <remarks>
/// The buffer's contract is one cell per column, and a control character breaks it. A tab moves
/// the cursor to the next tab stop without clearing what it passes over, so the row keeps
/// whatever was drawn there before and everything after it lands in the wrong column — which is
/// what made the hint window unreadable for a binding written with a trailing tab and a comment.
///
/// The same hole let a <em>filename</em> drive the terminal: nothing between a name and the
/// screen was checking, so an escape sequence in one was written out verbatim. Ranger has the
/// same gap and says so (<c>gui/widgets/titlebar.py:92</c>).
/// </remarks>
public class ScreenBufferControlCharacterTests
{
    private const string Escape = "\u001b";

    private static string Rendered(string text, int width = 40)
    {
        ScreenBuffer screen = new(width, 1);
        screen.Write(0, 0, text);
        return screen.TextAt(0).TrimEnd();
    }

    private static string Flushed(string text)
    {
        ScreenBuffer screen = new(40, 1);
        screen.Write(0, 0, text);

        StringBuilder output = new();
        screen.Flush(output, force: true);
        return output.ToString();
    }

    [Fact]
    public void ATabBecomesASpace()
    {
        // What it was standing in for, which is what makes a binding's trailing comment read as
        // a description rather than wreck the row.
        Assert.Equal("shell foo   # Clear clipboard",
                     Rendered("shell foo\t\t\t# Clear clipboard"));
    }

    [Fact]
    public void NoTabReachesTheTerminal()
    {
        Assert.DoesNotContain("\t", Flushed("a\tb"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEscapeInAFilenameCannotColourTheScreen()
    {
        // A file named this way is not entitled to choose what the reader's terminal does.
        Assert.DoesNotContain(Escape + "[31m", Flushed("colour" + Escape + "[31mred.txt"),
                              StringComparison.Ordinal);
    }

    [Fact]
    public void AnEscapeIsShownAsAQuestionMark()
    {
        // Visible rather than silent: a name with something odd in it should look odd.
        Assert.Equal("colour?[31mred.txt", Rendered("colour" + Escape + "[31mred.txt"));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\u0007")]
    [InlineData("\u007f")]
    [InlineData("\u0001")]
    [InlineData("\u009b")]
    public void EveryOtherControlCharacterBecomesAQuestionMark(string control)
    {
        Assert.Equal("a?b", Rendered("a" + control + "b"));
    }

    [Fact]
    public void OneControlCharacterOccupiesOneCell()
    {
        // The row has to stay exactly as wide as it says it is, or the terminal wraps.
        Assert.Equal(Rendered("abcd").Length, Rendered("a" + Escape + "cd").Length);
    }

    [Fact]
    public void OrdinaryTextIsUntouched()
    {
        Assert.Equal("naïve — ファイル.txt", Rendered("naïve — ファイル.txt"));
    }
}
