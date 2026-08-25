// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Which row each column marks as selected.
/// </summary>
/// <remarks>
/// The cursor was drawn only in the main column, so the directory you had just left showed no
/// cursor at all and the ancestry column did not show which entry you were inside. Ranger's
/// <c>_get_index_of_selected_file</c> returns the column's own directory pointer whichever column
/// it is (<c>browsercolumn.py:453-456</c>).
/// </remarks>
public class ColumnCursorTests
{
    private static (BrowserColumn Column, ScreenBuffer Screen, DirectoryNode Directory) Build(
        bool isMainColumn)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/b.txt")
            .AddFile("/home/c.txt");

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        BrowserColumn column = new(new DefaultColorScheme())
        {
            Directory = directory,
            IsMainColumn = isMainColumn,
            ShowSize = false,
        };

        column.Layout(new Rect(0, 0, 30, 6));

        return (column, new ScreenBuffer(30, 6), directory);
    }

    private static bool Highlighted(ScreenBuffer screen, int row) =>
        screen[2, row].Style.Attributes.HasFlag(CellAttributes.Reverse);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSelectedRow_IsMarkedInEveryColumn(bool isMainColumn)
    {
        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build(isMainColumn);
        directory.Cursor.MoveTo(1, directory.Entries);
        column.Render(screen);

        Assert.False(Highlighted(screen, 0));
        Assert.True(Highlighted(screen, 1));
        Assert.False(Highlighted(screen, 2));
    }

    [Fact]
    public void AnAncestryColumn_MarksTheEntryTheUserIsInside()
    {
        // What makes the breadcrumb column show where you came through.
        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory) = Build(false);
        directory.Cursor.MoveTo(2, directory.Entries);
        column.Render(screen);

        Assert.True(Highlighted(screen, 2));
    }
}
