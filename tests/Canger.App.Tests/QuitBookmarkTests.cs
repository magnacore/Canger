// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.App.Tests;

/// <summary>
/// The bookmark that takes you back to where you were.
/// </summary>
/// <remarks>
/// Ranger's <c>`</c> is a bookmark like any other, kept under the key <c>'</c>, and what makes it
/// useful across sessions is that ranger files the directory you are in as it exits
/// (<c>core/fm.py:547-548</c>). Canger did everything else — set the bookmark on every move, alias
/// the two keys, write the file on the way out — and simply never made that last call, so what
/// was saved was the directory most recently *left*. Quit inside a folder and <c>`</c> took you
/// to its parent.
/// </remarks>
public sealed class QuitBookmarkTests : IDisposable
{
    private readonly string _root;
    private readonly string _file;

    // The bookmark file is written with real System.IO — it is state, not something the browsing
    // filesystem abstraction covers — so this needs a real directory. The filesystem below is
    // only consulted to check that a bookmark still points at something.
    private readonly InMemoryFileSystem _fileSystem = new();

    public QuitBookmarkTests()
    {
        _root = Path.Join(Path.GetTempPath(), "canger-quit-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _file = Path.Join(_root, "bookmarks");

        _fileSystem.AddDirectory("/home/manuj");
        _fileSystem.AddDirectory("/home/manuj/work");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    private (Bookmarks Bookmarks, InMemoryFileSystem Fs, string Path) Build()
    {
        Bookmarks bookmarks = new(_fileSystem, _file);
        bookmarks.Load();

        return (bookmarks, _fileSystem, _file);
    }

    [Fact]
    public void QuittingFilesTheDirectoryTheUserWasIn()
    {
        (Bookmarks bookmarks, InMemoryFileSystem fs, string path) = Build();

        // Having walked from home into work, the in-session bookmark points back at home.
        bookmarks.RememberPrevious("/home/manuj");

        Program.RememberWhereTheUserEnded(bookmarks, "/home/manuj/work");

        Bookmarks next = new(fs, path);
        next.Load();

        Assert.Equal("/home/manuj/work", next.Get('`'));
    }

    [Fact]
    public void WithoutThatTheBookmarkWouldPointAtTheParent()
    {
        // The bug, written down: saving without remembering first keeps the directory left rather
        // than the one the user was in.
        (Bookmarks bookmarks, InMemoryFileSystem fs, string path) = Build();

        bookmarks.RememberPrevious("/home/manuj");
        bookmarks.Save();

        Bookmarks next = new(fs, path);
        next.Load();

        Assert.Equal("/home/manuj", next.Get('`'));
    }

    [Fact]
    public void TheApostropheAndTheBacktickAreTheSameBookmark()
    {
        // Ranger maps ` to ' on the way in and out, so both keys reach one entry.
        (Bookmarks bookmarks, InMemoryFileSystem fs, string path) = Build();

        Program.RememberWhereTheUserEnded(bookmarks, "/home/manuj/work");

        Bookmarks next = new(fs, path);
        next.Load();

        Assert.Equal(next.Get('\''), next.Get('`'));
    }

    [Fact]
    public void QuittingWithNowhereToRecordStillWritesTheOtherBookmarks()
    {
        // Defensive: a browser that never laid out a tab has no path to file, and the bookmarks
        // the user set by hand must still be saved.
        (Bookmarks bookmarks, InMemoryFileSystem fs, string path) = Build();
        bookmarks.Set('w', "/home/manuj/work");

        Program.RememberWhereTheUserEnded(bookmarks, string.Empty);

        Bookmarks next = new(fs, path);
        next.Load();

        Assert.Equal("/home/manuj/work", next.Get('w'));
    }
}
