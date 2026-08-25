// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// That the preview column and the tab agree on which directory object they are looking at.
/// </summary>
/// <remarks>
/// Each scan builds fresh <see cref="DirectoryNode"/> instances for the entries it finds, so the
/// node sitting in a listing is a different object from the interned one a tab uses when it enters
/// that directory — with its own cursor, marks and counted state. Drawing the preview column from
/// the listing therefore showed a directory whose cursor had never moved, so leaving a directory
/// and looking back at it showed the first row selected rather than the row the user left.
///
/// Ranger has no such split: its scan interns through <c>fm.get_directory</c>
/// (<c>container/directory.py:428</c>).
/// </remarks>
public class SelectedDirectoryTests
{
    [Fact]
    public void SelectedDirectory_IsTheSameObjectEnteringWouldUse()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/sub/a.txt");
        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home");

        DirectoryNode? shown = tab.SelectedDirectory;

        Assert.NotNull(shown);
        Assert.Same(cache.Get("/home/sub"), shown);
    }

    [Fact]
    public void SelectedDirectory_CarriesTheCursorLeftBehindInIt()
    {
        // The reported symptom: go in, move down, come back out, and the column showing that
        // directory must still point at where you were.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/sub/first.txt")
            .AddFile("/home/sub/second.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home");

        tab.Enter("/home/sub", cancellationToken: TestContext.Current.CancellationToken);
        tab.MoveCursor(1);
        Assert.Equal("second.txt", tab.Selected?.Basename);

        tab.GoUp(TestContext.Current.CancellationToken);

        Assert.Equal("second.txt", tab.SelectedDirectory?.Cursor.Current?.Basename);
    }

    [Fact]
    public void SelectedDirectory_IsNullWhenAFileIsSelected()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home");

        Assert.Null(tab.SelectedDirectory);
    }
}
