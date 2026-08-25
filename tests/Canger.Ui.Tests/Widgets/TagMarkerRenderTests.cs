// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The tag marker: the one cell at the left of a row that says a file is tagged, and with what.
/// </summary>
/// <remarks>
/// These exist because the marker was missing entirely for a long while. Everything else about
/// tagging worked — the keys, the store, the file on disk — so nothing failed, and the only
/// symptom was that pressing <c>t</c> appeared to do nothing at all. Rendering is the part worth
/// pinning down.
/// </remarks>
public class TagMarkerRenderTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-tagmarker-" + Path.GetRandomFileName());

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private (BrowserColumn Column, ScreenBuffer Screen, DirectoryNode Directory, Tags Tags)
        Build(int width = 30, int height = 10)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.txt");

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        // A real file, because Tags re-reads from disk before every change so two instances
        // cannot clobber one another. Given somewhere unwritable, each change would silently
        // discard the last — which is ranger's behaviour too (container/tags.py:76-90), not
        // something to work around here.
        Directory.CreateDirectory(_root);
        Tags tags = new(Path.Join(_root, "tagged"));

        BrowserColumn column = new(new DefaultColorScheme())
        {
            Directory = directory,
            IsMainColumn = true,
            ShowSize = false,
            Tags = tags,
        };
        column.Layout(new Rect(0, 0, width, height));

        return (column, new ScreenBuffer(width, height), directory, tags);
    }

    [Fact]
    public void Draw_ShowsAnAsteriskForTheDefaultTag()
    {
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        tags.Toggle(["/home/beta.txt"]);
        column.Render(screen);

        Assert.StartsWith("*beta.txt", screen.TextAt(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheTagCharacterForACustomTag()
    {
        // What `map "<any> tag_toggle tag=%any` produces: the tag itself is the marker.
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        tags.Toggle(["/home/alpha.txt"], 'w');
        tags.Toggle(["/home/gamma.txt"], '7');
        column.Render(screen);

        Assert.StartsWith("walpha.txt", screen.TextAt(0), StringComparison.Ordinal);
        Assert.StartsWith("7gamma.txt", screen.TextAt(2), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ReservesTheMarkerCellSoNamesDoNotMoveWhenTagged()
    {
        // Ranger claims the cell whether or not the file is tagged, which is what stops the whole
        // listing sliding sideways the moment one row gains a marker.
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        column.Render(screen);
        string before = screen.TextAt(1);

        tags.Toggle(["/home/beta.txt"]);
        column.Render(screen);

        Assert.StartsWith(" beta.txt", before, StringComparison.Ordinal);
        Assert.StartsWith("*beta.txt", screen.TextAt(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ColoursTheMarkerDifferentlyFromTheName()
    {
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        tags.Toggle(["/home/beta.txt"]);
        column.Render(screen);

        Assert.NotEqual(screen[0, 1].Style, screen[1, 1].Style);
    }

    [Fact]
    public void Draw_SitsAfterTheLineNumber()
    {
        // number, gap, tag, name — the order ranger uses (browsercolumn.py:379-399).
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        column.LineNumbers = "absolute";
        column.OneIndexed = true;
        tags.Toggle(["/home/beta.txt"], 'x');
        column.Render(screen);

        Assert.StartsWith("2 xbeta.txt", screen.TextAt(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_OmitsTheMarkerInAncestryColumnsWhenConfiguredTo()
    {
        // display_tags_in_all_columns false: only the main column spends a cell on this.
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        column.IsMainColumn = false;
        column.DisplayTagsInAllColumns = false;
        tags.Toggle(["/home/beta.txt"]);
        column.Render(screen);

        Assert.StartsWith("beta.txt", screen.TextAt(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_ShowsTheMarkerInAncestryColumnsByDefault()
    {
        (BrowserColumn column, ScreenBuffer screen, _, Tags tags) = Build();
        column.IsMainColumn = false;
        tags.Toggle(["/home/beta.txt"]);
        column.Render(screen);

        Assert.StartsWith("*beta.txt", screen.TextAt(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Draw_LeavesEveryRowBlankWithNoTagStoreAtAll()
    {
        (BrowserColumn column, ScreenBuffer screen, _, _) = Build();
        column.Tags = null;
        column.Render(screen);

        Assert.StartsWith(" alpha.txt", screen.TextAt(0), StringComparison.Ordinal);
    }
}
