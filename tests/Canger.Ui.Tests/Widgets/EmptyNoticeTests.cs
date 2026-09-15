// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The word a column shows in place of a listing.
/// </summary>
/// <remarks>
/// Reported as looking cramped against the left edge. It was: the colour was painted across the
/// whole row, so in a scheme that gives <c>empty</c> a background the word was the left end of a
/// band rather than a label. Ranger erases the column and writes the word into it
/// (<c>gui/widgets/browsercolumn.py:199, 286-289</c>), colouring five characters and no more.
/// </remarks>
public class EmptyNoticeTests
{
    private static (BrowserColumn Column, ScreenBuffer Screen, Tab Tab) Build(string path)
    {
        InMemoryFileSystem files = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/nothing")
            .AddFile("/home/something.txt");

        DirectoryCache cache = new(files);
        Tab tab = new(cache, path, 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        BrowserColumn column = new(new DefaultColorScheme());
        column.Layout(new Rect(0, 0, 20, 4));

        return (column, new ScreenBuffer(20, 4), tab);
    }

    [Fact]
    public void AnEmptyDirectorySaysSo()
    {
        (BrowserColumn column, ScreenBuffer screen, Tab tab) = Build("/home/nothing");

        column.Directory = tab.Current;
        column.Render(screen);

        Assert.StartsWith("empty", screen.TextAt(0), StringComparison.Ordinal);
    }

    [Fact]
    public void TheColourStopsWithTheWord()
    {
        // The reported complaint, and what it really was: beyond the word the row belongs to the
        // column, not to the notice, so a scheme with a background paints a label rather than a
        // band reaching the edge of the pane.
        (BrowserColumn column, ScreenBuffer screen, Tab tab) = Build("/home/nothing");

        column.Directory = tab.Current;
        column.Render(screen);

        CellStyle word = screen[0, 0].Style;
        CellStyle after = screen[6, 0].Style;
        CellStyle far = screen[19, 0].Style;

        Assert.NotEqual(word, after);
        Assert.Equal(after, far);
    }

    [Fact]
    public void TheRowBehindItIsStillCleared()
    {
        // Something else may have drawn there on an earlier frame, and half a filename left
        // beside the word would read as a listing that had not finished loading.
        (BrowserColumn column, ScreenBuffer screen, Tab tab) = Build("/home/nothing");

        screen.Write(0, 0, "left over from before");

        column.Directory = tab.Current;
        column.Render(screen);

        Assert.Equal("empty" + new string(' ', 15), screen.TextAt(0));
    }
}
