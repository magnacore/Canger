// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Numbering the rows, which is what makes counted movement usable without counting by eye.
/// </summary>
public class LineNumberRenderTests
{
    /// <summary>A column over a directory of twelve files, so numbering reaches two digits.</summary>
    /// <remarks>
    /// The expectations below read <c>number, gap, tag-marker cell, name</c>, which is the order
    /// ranger lays the left of a row out in (<c>browsercolumn.py:379-399</c>). The tag cell is
    /// always claimed, so nothing here is tagged and it shows as one blank column before the name.
    /// </remarks>
    private static (BrowserColumn Column, ScreenBuffer Screen, DirectoryNode Directory) Build(
        string lineNumbers, int width = 30, int height = 12)
    {
        InMemoryFileSystem fs = new();

        foreach (char name in "abcdefghijkl")
        {
            fs.AddFile($"/home/{name}.txt");
        }

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        BrowserColumn column = new(new DefaultColorScheme())
        {
            Directory = directory,
            IsMainColumn = true,
            ShowSize = false,
            LineNumbers = lineNumbers,
        };
        column.Layout(new Rect(0, 0, width, height));

        return (column, new ScreenBuffer(width, height), directory);
    }

    [Fact]
    public void Draw_ShowsNoNumbersByDefault()
    {
        (BrowserColumn column, ScreenBuffer screen, _) = Build("false");
        column.Render(screen);

        Assert.StartsWith(" a.txt", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_NumbersRowsAbsolutelyFromZero()
    {
        (BrowserColumn column, ScreenBuffer screen, _) = Build("absolute");
        column.Render(screen);

        // Right-aligned to the widest number in view, so the names still line up.
        Assert.StartsWith(" 0  a.txt", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith(" 9  j.txt", screen.TextAt(9), StringComparison.Ordinal);
        Assert.StartsWith("10  k.txt", screen.TextAt(10), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_CanNumberFromOneInstead()
    {
        (BrowserColumn column, ScreenBuffer screen, _) = Build("absolute");
        column.OneIndexed = true;
        column.Render(screen);

        Assert.StartsWith(" 1  a.txt", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith("12  l.txt", screen.TextAt(11), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_NumbersRowsByDistanceFromTheCursor()
    {
        // The number beside a row is exactly what to type before j or k to land on it.
        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build("relative");
        directory.Cursor.MoveTo(4, directory.Entries);
        column.Render(screen);

        Assert.StartsWith("4  a.txt", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith("1  d.txt", screen.TextAt(3), StringComparison.Ordinal);
        Assert.StartsWith("1  f.txt", screen.TextAt(5), StringComparison.Ordinal);
        Assert.StartsWith("7  l.txt", screen.TextAt(11), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheCursorRowsOwnPositionRatherThanAUselessZero()
    {
        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build("relative");
        directory.Cursor.MoveTo(4, directory.Entries);
        column.Render(screen);

        Assert.StartsWith("4  e.txt", screen.TextAt(4), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsZeroOnTheCursorRowWhenAskedTo()
    {
        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build("relative");
        column.RelativeCurrentZero = true;
        directory.Cursor.MoveTo(4, directory.Entries);
        column.Render(screen);

        Assert.StartsWith("0  e.txt", screen.TextAt(4), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_LeavesTheAncestryColumnsUnnumbered()
    {
        // The quantifier applies to the cursor's own listing, so a number elsewhere would mean
        // nothing — and those columns are too narrow to spare the space.
        (BrowserColumn column, ScreenBuffer screen, _) = Build("absolute");
        column.IsMainColumn = false;
        column.Render(screen);

        Assert.StartsWith(" a.txt", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_DropsTheNumberRatherThanCrushTheNameInANarrowColumn()
    {
        (BrowserColumn column, ScreenBuffer screen, _) = Build("absolute", width: 4);
        column.Render(screen);

        // The name truncates with a marker, but it is the name — not a number and one letter.
        Assert.StartsWith(" a.", screen.TextAt(0), StringComparison.Ordinal);
    }
}
