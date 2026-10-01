// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Where the filename's colour starts in the title bar.
/// </summary>
/// <remarks>
/// Reported: a name holding a fullwidth colon — <c>Larridin： Your Board…</c>, the character a
/// download keeps where the title had a colon the filesystem will not take — showed its first
/// letter in the colour of the path behind it, while the same name without one looked right.
/// </remarks>
public class TitleSelectionColourTests
{
    private const string Wide = "Larridin： Your Board.mkv";
    private const string Plain = "Larridin Your Board.mkv";

    private static (CellStyle First, CellStyle Separator, CellStyle Last) Draw(string name)
    {
        InMemoryFileSystem files = new InMemoryFileSystem().AddFile($"/home/x/{name}");
        Tab tab = new(new DirectoryCache(files), "/home/x");
        tab.Current.Load(TestContext.Current.CancellationToken);
        tab.MoveCursorTo(tab.Current.Entries.Single());

        ScreenBuffer screen = new(80, 1);
        TitleBar bar = new(new DefaultColorScheme()) { Tab = tab, ShowSelection = true };
        bar.Layout(new Rect(0, 0, 80, 1));
        bar.Render(screen);

        string line = screen.TextAt(0);
        int at = line.IndexOf(name[0] + name[1..3], StringComparison.Ordinal);

        // The '/' before the name, the name's first cell, and its last.
        return (screen[at, 0].Style, screen[at - 1, 0].Style,
                screen[at + new WideString(name).Width - 1, 0].Style);
    }

    [Fact]
    public void AnOrdinaryNameIsColouredFromItsFirstLetter()
    {
        (CellStyle first, CellStyle separator, CellStyle last) = Draw(Plain);

        Assert.NotEqual(separator, first);
        Assert.Equal(first, last);
    }

    [Fact]
    public void ANameWithAWideCharacterIsTooColouredFromItsFirstLetter()
    {
        // The reported case. The colour was started by counting characters and the line is drawn
        // in cells, so one fullwidth character left the name's first cell wearing the path's
        // colour and its last cell unpainted.
        (CellStyle first, CellStyle separator, CellStyle last) = Draw(Wide);

        Assert.NotEqual(separator, first);
        Assert.Equal(first, last);
    }

    [Fact]
    public void BothNamesAreColouredTheSameWay()
    {
        // Two files side by side in the same folder, one with the character and one without:
        // whatever the colour is, it must not depend on that.
        (CellStyle wide, _, _) = Draw(Wide);
        (CellStyle plain, _, _) = Draw(Plain);

        Assert.Equal(plain, wide);
    }
}
