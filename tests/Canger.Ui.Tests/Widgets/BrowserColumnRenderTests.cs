// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Rendering, asserted on the screen buffer rather than on a terminal. This is what the buffer
/// exists for: a widget can be laid out at a chosen size, drawn, and inspected exactly.
/// </summary>
public class BrowserColumnRenderTests
{
    private static (BrowserColumn Column, ScreenBuffer Screen, DirectoryNode Directory) Build(
        InMemoryFileSystem fs, int width = 30, int height = 10, string path = "/home")
    {
        DirectoryNode directory = new(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        BrowserColumn column = new(new DefaultColorScheme())
        {
            Directory = directory,
            IsMainColumn = true,
            ShowSize = false,
        };
        column.Layout(new Rect(0, 0, width, height));

        return (column, new ScreenBuffer(width, height), directory);
    }

    [Fact]
    public void Draw_ListsEntriesOnePerRow()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddDirectory("/home/gamma");

        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs);
        column.Render(screen);

        Assert.StartsWith(" gamma", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith(" alpha.txt", screen.TextAt(1), StringComparison.Ordinal);
        Assert.StartsWith(" beta.txt", screen.TextAt(2), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_HighlightsTheCursorRowAcrossTheWholeColumn()
    {
        // The highlight has to span the full width, or the cursor row looks ragged.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/b.txt");

        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build(fs);
        directory.Cursor.MoveTo(1, directory.Entries);
        column.Render(screen);

        Assert.All(Enumerable.Range(0, 30), x =>
            Assert.True(screen[x, 1].Style.Attributes.HasFlag(CellAttributes.Reverse),
                        $"cell {x} of the cursor row is not highlighted"));

        Assert.False(screen[0, 0].Style.Attributes.HasFlag(CellAttributes.Reverse));
    }

    [Fact]
    public void Draw_ShowsDirectoriesInTheDirectoryColour()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFile("/home/plain.txt");

        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs);
        column.Render(screen);

        Assert.Equal(Color.Blue.Bright(), screen[1, 0].Style.Foreground);
        Assert.True(screen[1, 0].Style.Attributes.HasFlag(CellAttributes.Bold));

        Assert.Equal(Color.Default, screen[1, 1].Style.Foreground);
    }

    [Fact]
    public void Draw_MarksMarkedEntries()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/b.txt");

        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build(fs);
        directory.Entries[0].IsMarked = true;
        column.Render(screen);

        // A mark shifts the name one cell right and recolours it, as ranger's does
        // (browsercolumn.py:367-369). It deliberately draws no glyph: the asterisk belongs to
        // the tag marker, and having it mean two things is what made a tag invisible.
        Assert.StartsWith("  a.txt", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith(" b.txt", screen.TextAt(1), StringComparison.Ordinal);

        // The name itself, not the blank cells around it, carries the marked colour.
        Assert.NotEqual(screen[2, 0].Style, screen[1, 1].Style);
    }

    [Fact]
    public void Draw_TruncatesNamesTooLongForTheColumn()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a-really-very-long-file-name-indeed.txt");

        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs, width: 20);
        column.Render(screen);

        string row = screen.TextAt(0);
        Assert.Equal(20, row.Length);
        Assert.Contains("~", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_KeepsColumnsAlignedWithWideCharacters()
    {
        // A double-width character must occupy two cells, or every row below drifts sideways.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/モヒカン.txt")
            .AddFile("/home/plain.txt");

        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs, width: 20);
        column.Render(screen);

        // Compare cells, not characters: a wide character is one character occupying two cells,
        // so the row's text is shorter than the column is wide while still filling it exactly.
        Assert.All(Enumerable.Range(0, 2),
                   row => Assert.Equal(20, CellWidth.Of(screen.TextAt(row))));
    }

    [Fact]
    public void Draw_SaysSoWhenTheDirectoryIsEmpty()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home");

        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs);
        column.Render(screen);

        Assert.StartsWith("empty", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_SaysSoWhenTheDirectoryCannotBeRead()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/secret")
            .MakeInaccessible("/home/secret");

        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs, path: "/home/secret");
        column.Render(screen);

        Assert.StartsWith("not accessible", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ScrollsToKeepTheCursorVisible()
    {
        InMemoryFileSystem fs = new();
        for (int i = 0; i < 100; i++)
        {
            fs.AddFile($"/home/file{i:D3}.txt");
        }

        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) =
            Build(fs, height: 10);
        directory.Cursor.MoveTo(50, directory.Entries);
        column.Render(screen);

        string[] rows = [.. Enumerable.Range(0, 10).Select(screen.TextAt)];
        Assert.Contains(rows, row => row.Contains("file050.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void Draw_LeavesNothingBehindWhenThereIsNoDirectory()
    {
        BrowserColumn column = new(new DefaultColorScheme());
        column.Layout(new Rect(0, 0, 10, 3));
        ScreenBuffer screen = new(10, 3);

        column.Render(screen);

        Assert.All(screen.Snapshot(), row => Assert.Equal("          ", row));
    }

    /// <summary>Builds a column that shows sizes.</summary>
    private static (BrowserColumn Column, ScreenBuffer Screen) WithSizes(
        InMemoryFileSystem fs, int width = 30)
    {
        (BrowserColumn column, ScreenBuffer screen, _) = Build(fs, width);
        column.ShowSize = true;
        return (column, screen);
    }

    /// <summary>A 2 KiB file whose name cannot fit in a thirty-column listing.</summary>
    private static InMemoryFileSystem LongNamed() =>
        new InMemoryFileSystem().AddFileOfSize(
            "/home/a-name-far-too-long-to-fit-in-this-column.txt", 2048, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Draw_KeepsASpaceBetweenATruncatedNameAndItsSize()
    {
        // The reported defect: the size ran straight onto the ellipsis, `…-column~2.05 k`. A
        // column was reserved for the gap and then spent at the far right of the row, past the
        // size, where nothing needed it. Ranger carries the space in the string itself
        // (`" " + infostringdata`, browsercolumn.py:410-411).
        (BrowserColumn column, ScreenBuffer screen) = WithSizes(LongNamed());
        column.Render(screen);

        string row = screen.TextAt(0);

        Assert.Contains("~", row, StringComparison.Ordinal);
        Assert.Contains("~ 2.05 k", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_PutsTheSizeAgainstTheRightEdge()
    {
        // The gap belongs between the name and the size, so the size itself ends at the last
        // column. A trailing blank would be the same bug wearing the other shoe — which is
        // exactly what it was.
        (BrowserColumn column, ScreenBuffer screen) = WithSizes(LongNamed());
        column.Render(screen);

        Assert.Equal('k', (char)screen[29, 0].Rune.Value);
        Assert.Equal(' ', (char)screen[23, 0].Rune.Value);
        Assert.NotEqual(' ', (char)screen[22, 0].Rune.Value);
    }

    [Fact]
    public void Draw_StillSeparatesThemWhenTheNameIsShort()
    {
        // Nothing is truncated here, so the gap is padding rather than the reserved column. It
        // has to be there either way, and the size still ends at the edge.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFileOfSize("/home/a.txt", 2048, DateTimeOffset.UnixEpoch);

        (BrowserColumn column, ScreenBuffer screen) = WithSizes(fs);
        column.Render(screen);

        Assert.Equal(" a.txt                  2.05 k", screen.TextAt(0));
    }

    [Fact]
    public void Draw_DropsTheSizeRatherThanCrushTheName()
    {
        // The guard that was already there: in a column too narrow for both, the name wins.
        (BrowserColumn column, ScreenBuffer screen) = WithSizes(LongNamed(), width: 10);
        column.Render(screen);

        Assert.DoesNotContain("2.05 k", screen.TextAt(0), StringComparison.Ordinal);
    }
}
