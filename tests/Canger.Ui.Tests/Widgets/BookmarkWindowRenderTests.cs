// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The bookmark list shown while <c>'</c> or <c>m</c> waits for a key.
/// </summary>
/// <remarks>
/// This was a single status-line message with every bookmark squeezed onto one row, shortened to
/// fit. Ranger draws a list over the bottom of the browser, one bookmark per row, under an
/// underlined heading — and the heading's rule is the heading itself drawn underlined, not a row
/// of dashes below it.
/// </remarks>
public sealed class BookmarkWindowRenderTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-bmwin-" + Path.GetRandomFileName());

    private readonly Bookmarks _bookmarks;

    public BookmarkWindowRenderTests()
    {
        Directory.CreateDirectory(_root);

        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/books")
            .AddDirectory("/home/notes")
            .AddDirectory("/home/.config/private");

        _bookmarks = new Bookmarks(fs, Path.Join(_root, "bookmarks"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (BookmarkWindow Window, ScreenBuffer Screen) Build(int width = 60, int height = 10)
    {
        BookmarkWindow window = new(new DefaultColorScheme())
        {
            Bookmarks = _bookmarks,
        };

        window.Layout(new Rect(0, 0, width, height));

        return (window, new ScreenBuffer(width, height));
    }

    [Fact]
    public void Draw_PutsOneBookmarkPerRowBeneathAHeading()
    {
        _bookmarks.Set('k', "/home/books");
        _bookmarks.Set('n', "/home/notes");

        (BookmarkWindow window, ScreenBuffer screen) = Build();
        window.Render(screen);

        // Anchored to the bottom: two rows plus the heading above them.
        Assert.StartsWith("mark  path", screen.TextAt(7), StringComparison.Ordinal);
        Assert.Equal(" k   /home/books", screen.TextAt(8).TrimEnd());
        Assert.Equal(" n   /home/notes", screen.TextAt(9).TrimEnd());
    }

    [Fact]
    public void Draw_UnderlinesTheHeadingRatherThanDrawingALineUnderIt()
    {
        _bookmarks.Set('k', "/home/books");

        (BookmarkWindow window, ScreenBuffer screen) = Build();
        window.Render(screen);

        // One bookmark, so the heading is on row 8 and the bookmark on row 9.
        Assert.True(screen[0, 8].Style.Attributes.HasFlag(CellAttributes.Underline),
                    "the heading carries the rule");

        Assert.False(screen[0, 9].Style.Attributes.HasFlag(CellAttributes.Underline),
                     "the bookmark rows must not be underlined");
    }

    [Fact]
    public void Draw_OrdersByKeyIgnoringCase()
    {
        // So `a` and `A` sit together rather than being separated by every capital letter.
        _bookmarks.Set('n', "/home/notes");
        _bookmarks.Set('k', "/home/books");

        (BookmarkWindow window, _) = Build();

        Assert.Equal(['k', 'n'], window.Entries.Select(e => e.Key));
    }

    [Fact]
    public void Draw_HidesBookmarksInsideHiddenDirectoriesWhenAsked()
    {
        _bookmarks.Set('k', "/home/books");
        _bookmarks.Set('p', "/home/.config/private");

        (BookmarkWindow window, _) = Build();
        window.ShowHidden = false;

        // Ranger's test is whether the path contains "/." anywhere, so anything under a hidden
        // directory goes, not only a bookmark whose last component is hidden.
        Assert.Equal(['k'], window.Entries.Select(e => e.Key));
    }

    [Fact]
    public void Draw_ShowsHiddenOnesByDefault()
    {
        _bookmarks.Set('p', "/home/.config/private");

        (BookmarkWindow window, _) = Build();

        Assert.Single(window.Entries);
    }

    [Fact]
    public void Draw_DrawsNothingWithNoBookmarks()
    {
        (BookmarkWindow window, ScreenBuffer screen) = Build();
        window.Render(screen);

        Assert.All(screen.Snapshot(), row => Assert.Equal(string.Empty, row.Trim()));
    }

    [Fact]
    public void Draw_KeepsTheLastBookmarksWhenThereIsNotRoomForAllOfThem()
    {
        foreach (char key in "abcdefgh")
        {
            _bookmarks.Set(key, "/home/notes");
        }

        // Four rows: one heading and three bookmarks.
        (BookmarkWindow window, ScreenBuffer screen) = Build(height: 4);
        window.Render(screen);

        Assert.StartsWith("mark  path", screen.TextAt(0), StringComparison.Ordinal);
        Assert.Equal(" a   /home/notes", screen.TextAt(1).TrimEnd());
        Assert.Equal(" c   /home/notes", screen.TextAt(3).TrimEnd());
    }

    [Fact]
    public void Draw_TruncatesAPathTooLongForTheWindow()
    {
        _bookmarks.Set('k', "/home/notes");

        (BookmarkWindow window, ScreenBuffer screen) = Build(width: 10);
        window.Render(screen);

        // Cut to the width rather than wrapped or overflowing into the row below. The trailing
        // marker is Canger's, used by every other truncating column; ranger's addnstr cuts
        // silently, which leaves no sign that the path shown is not the whole path.
        Assert.Equal(" k   /hom~", screen.TextAt(9));
    }
}
