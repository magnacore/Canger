// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Tui.Rendering;

namespace Canger.Tui.Tests.Rendering;

public class ScreenBufferTests
{
    private static string Flush(ScreenBuffer buffer, bool force = false)
    {
        StringBuilder output = new();
        buffer.Flush(output, force);
        return output.ToString();
    }

    [Fact]
    public void NewBuffer_IsFilledWithBlanks()
    {
        ScreenBuffer buffer = new(4, 2);

        Assert.Equal("    ", buffer.TextAt(0));
        Assert.Equal(["    ", "    "], buffer.Snapshot());
    }

    [Fact]
    public void Write_PlacesTextAtAPosition()
    {
        ScreenBuffer buffer = new(10, 2);

        buffer.Write(2, 1, "hello");

        Assert.Equal("          ", buffer.TextAt(0));
        Assert.Equal("  hello   ", buffer.TextAt(1));
    }

    [Fact]
    public void Write_ReportsTheCellsItUsed()
    {
        ScreenBuffer buffer = new(20, 1);

        Assert.Equal(5, buffer.Write(0, 0, "hello"));
        // Wide characters take two cells each.
        Assert.Equal(8, buffer.Write(0, 0, "モヒカン"));
    }

    [Fact]
    public void Write_StopsAtTheRightEdge()
    {
        ScreenBuffer buffer = new(5, 1);

        buffer.Write(3, 0, "abcdef");

        Assert.Equal("   ab", buffer.TextAt(0));
    }

    [Fact]
    public void Write_GivesAWideCharacterTwoCells()
    {
        ScreenBuffer buffer = new(6, 1);

        buffer.Write(0, 0, "aモb");

        // The continuation cell is not part of the text, so the row reads as written.
        Assert.Equal("aモb  ", buffer.TextAt(0));
        Assert.True(buffer[2, 0].IsContinuation);
        Assert.False(buffer[1, 0].IsContinuation);
    }

    [Fact]
    public void Write_ReplacesAWideCharacterThatWouldOverhangTheEdge()
    {
        // Drawing half a wide character would make the terminal wrap and corrupt the layout,
        // so a space is drawn instead and the row stays exactly Width cells.
        ScreenBuffer buffer = new(3, 1);

        buffer.Write(0, 0, "abモ");

        Assert.Equal("ab ", buffer.TextAt(0));
    }

    [Fact]
    public void Writes_OutsideTheBufferAreIgnored()
    {
        ScreenBuffer buffer = new(4, 2);

        buffer.Write(0, 5, "offscreen");
        buffer.Write(-3, 0, "x");
        buffer.Write(0, -1, "x");

        Assert.Equal(["    ", "    "], buffer.Snapshot());
    }

    [Fact]
    public void Fill_ClearsARectangleWithoutTouchingTheRest()
    {
        ScreenBuffer buffer = new(6, 3);
        buffer.Write(0, 0, "aaaaaa");
        buffer.Write(0, 1, "bbbbbb");
        buffer.Write(0, 2, "cccccc");

        buffer.Fill(1, 1, 3, 1);

        Assert.Equal("aaaaaa", buffer.TextAt(0));
        Assert.Equal("b   bb", buffer.TextAt(1));
        Assert.Equal("cccccc", buffer.TextAt(2));
    }

    [Fact]
    public void Recolor_ChangesStyleWithoutChangingText()
    {
        // This is how the progress bar tints part of the status bar.
        ScreenBuffer buffer = new(10, 1);
        buffer.Write(0, 0, "copying...");

        buffer.Recolor(0, 0, 4, new CellStyle(Color.Default, Color.Blue));

        Assert.Equal("copying...", buffer.TextAt(0));
        Assert.Equal(Color.Blue, buffer[0, 0].Style.Background);
        Assert.Equal(Color.Blue, buffer[3, 0].Style.Background);
        Assert.Equal(Color.Default, buffer[4, 0].Style.Background);
    }

    [Fact]
    public void Indexer_ReturnsABlankOutsideTheBuffer()
    {
        ScreenBuffer buffer = new(2, 2);

        Assert.Equal(Cell.Blank, buffer[99, 99]);
        Assert.False(buffer.Contains(2, 0));
        Assert.True(buffer.Contains(1, 1));
    }

    [Fact]
    public void Flush_PaintsEverythingOnTheFirstFrame()
    {
        ScreenBuffer buffer = new(5, 1);
        buffer.Write(0, 0, "hello");

        string output = Flush(buffer);

        Assert.Contains("hello", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_EmitsNothingWhenNothingChanged()
    {
        ScreenBuffer buffer = new(5, 2);
        buffer.Write(0, 0, "hello");
        Flush(buffer);

        buffer.Write(0, 0, "hello");

        Assert.Equal(string.Empty, Flush(buffer));
    }

    [Fact]
    public void Flush_EmitsOnlyWhatChanged()
    {
        // Repainting the whole screen on every keystroke would be visibly slow over ssh.
        ScreenBuffer buffer = new(20, 3);
        buffer.Write(0, 0, "unchanged");
        buffer.Write(0, 1, "unchanged");
        Flush(buffer);

        buffer.Write(0, 0, "unchanged");
        buffer.Write(0, 1, "different");
        string output = Flush(buffer);

        Assert.Contains("different", output, StringComparison.Ordinal);
        Assert.DoesNotContain("unchanged", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_RepaintsEverythingWhenForced()
    {
        // Needed after something Canger did not draw has disturbed the screen, such as an
        // external program or an image preview.
        ScreenBuffer buffer = new(5, 1);
        buffer.Write(0, 0, "hello");
        Flush(buffer);

        buffer.Write(0, 0, "hello");

        Assert.Contains("hello", Flush(buffer, force: true), StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_MovesTheCursorOncePerRunOfChanges()
    {
        ScreenBuffer buffer = new(10, 1);
        buffer.Write(0, 0, "abcdefghij");
        Flush(buffer);

        buffer.Write(0, 0, "abcdefghij");
        buffer.Write(3, 0, "XY");
        string output = Flush(buffer);

        // Two adjacent changed cells, so one cursor move, not two.
        Assert.Equal(1, CountOccurrences(output, Ansi.Csi + "1;4H"));
        Assert.Contains("XY", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_EmitsAStyleOnlyWhenItChanges()
    {
        ScreenBuffer buffer = new(10, 1);
        CellStyle red = new(Color.Red, Color.Default);

        buffer.Write(0, 0, "aaa", red);
        string output = Flush(buffer, force: true);

        // Three cells share a style, so one style sequence covers them all.
        Assert.Equal(1, CountOccurrences(output, Ansi.SetStyle(red)));
    }

    [Fact]
    public void Flush_ResetsTheStyleWhenItHasFinished()
    {
        ScreenBuffer buffer = new(4, 1);
        buffer.Write(0, 0, "x", new CellStyle(Color.Red, Color.Default));

        Assert.EndsWith(Ansi.ResetStyle, Flush(buffer), StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_SkipsContinuationCells()
    {
        // Emitting the second half of a wide character would push the rest of the row along.
        ScreenBuffer buffer = new(6, 1);
        buffer.Write(0, 0, "モヒ");

        string output = Flush(buffer);

        Assert.Contains("モヒ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Resize_ChangesTheGridAndForcesARepaint()
    {
        ScreenBuffer buffer = new(5, 1);
        buffer.Write(0, 0, "hello");
        Flush(buffer);

        buffer.Resize(10, 2);

        Assert.Equal(10, buffer.Width);
        Assert.Equal(2, buffer.Height);
        Assert.Equal("          ", buffer.TextAt(0));

        buffer.Write(0, 0, "hello");
        Assert.Contains("hello", Flush(buffer), StringComparison.Ordinal);
    }

    [Fact]
    public void Resize_ToTheSameSizeKeepsTheContents()
    {
        ScreenBuffer buffer = new(5, 1);
        buffer.Write(0, 0, "hello");

        buffer.Resize(5, 1);

        Assert.Equal("hello", buffer.TextAt(0));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
