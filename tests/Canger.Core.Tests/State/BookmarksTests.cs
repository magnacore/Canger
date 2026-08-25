// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

public sealed class BookmarksTests : IDisposable
{
    private readonly string _root;
    private readonly string _file;
    private readonly InMemoryFileSystem _fileSystem = new();

    public BookmarksTests()
    {
        _root = Path.Join(Path.GetTempPath(), "canger-bookmarks-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _file = Path.Join(_root, "bookmarks");

        _fileSystem.AddDirectory("/home/user");
        _fileSystem.AddDirectory("/tmp");
        _fileSystem.AddDirectory("/var/log");
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

    private Bookmarks Build() => new(_fileSystem, _file) { AutoSave = false };

    [Fact]
    public void Set_And_Get_RoundTrip()
    {
        Bookmarks bookmarks = Build();

        bookmarks.Set('h', "/home/user");

        Assert.Equal("/home/user", bookmarks.Get('h'));
        Assert.Null(bookmarks.Get('z'));
    }

    [Fact]
    public void BacktickIsAnAliasForTheQuoteKey()
    {
        // Both keys are conventionally used for the same thing, and which is easier to reach
        // depends on the keyboard.
        Bookmarks bookmarks = Build();

        bookmarks.Set('`', "/tmp");

        Assert.Equal("/tmp", bookmarks.Get('\''));
        Assert.Equal("/tmp", bookmarks.Get('`'));
    }

    [Fact]
    public void Set_IgnoresAKeyThatCannotHoldABookmark()
    {
        Bookmarks bookmarks = Build();

        bookmarks.Set('!', "/tmp");

        Assert.Null(bookmarks.Get('!'));
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('7', true)]
    [InlineData('\'', true)]
    [InlineData('`', true)]
    [InlineData('!', false)]
    [InlineData(' ', false)]
    public void IsValidKey_AcceptsLettersDigitsAndTheTwoAliases(char key, bool expected) =>
        Assert.Equal(expected, Bookmarks.IsValidKey(key));

    [Fact]
    public void Remove_ClearsAKey()
    {
        Bookmarks bookmarks = Build();
        bookmarks.Set('h', "/home/user");

        Assert.True(bookmarks.Remove('h'));
        Assert.Null(bookmarks.Get('h'));
        Assert.False(bookmarks.Remove('h'));
    }

    [Fact]
    public void SaveAndLoad_RoundTripThroughTheFile()
    {
        Bookmarks first = Build();
        first.Set('h', "/home/user");
        first.Set('t', "/tmp");
        first.Save();

        Bookmarks second = Build();
        second.Load();

        Assert.Equal("/home/user", second.Get('h'));
        Assert.Equal("/tmp", second.Get('t'));
    }

    [Fact]
    public void Load_KeepsPathsContainingColons()
    {
        // Only the first colon separates the key from the path.
        Bookmarks bookmarks = Build();
        bookmarks.Set('x', "/odd:path/here");
        bookmarks.Save();

        Bookmarks reloaded = Build();
        reloaded.Load();

        Assert.Equal("/odd:path/here", reloaded.Get('x'));
    }

    [Fact]
    public void Load_IgnoresMalformedLines()
    {
        File.WriteAllLines(_file, ["h:/home/user", "not a bookmark", "", "t:/tmp"]);

        Bookmarks bookmarks = Build();
        bookmarks.Load();

        Assert.Equal(2, bookmarks.Entries.Count);
    }

    [Fact]
    public void Save_KeepsBookmarksAnotherInstanceAdded()
    {
        // Two windows open at once must not mean the second to exit discards the first's work.
        Bookmarks first = Build();
        first.Load();
        first.Set('a', "/home/user");

        Bookmarks second = Build();
        second.Load();
        second.Set('b', "/tmp");
        second.Save();

        first.Save();

        Bookmarks reader = Build();
        reader.Load();

        Assert.Equal("/home/user", reader.Get('a'));
        Assert.Equal("/tmp", reader.Get('b'));
    }

    [Fact]
    public void Save_LetsThisInstancesChangeWinForKeysItTouched()
    {
        Bookmarks first = Build();
        first.Set('a', "/original");
        first.Save();

        Bookmarks second = Build();
        second.Load();

        // Another instance changes the same key while this one is running.
        Bookmarks other = Build();
        other.Load();
        other.Set('a', "/from-elsewhere");
        other.Save();

        second.Set('a', "/mine");
        second.Save();

        Bookmarks reader = Build();
        reader.Load();

        Assert.Equal("/mine", reader.Get('a'));
    }

    [Fact]
    public void Save_HonoursADeletionByThisInstance()
    {
        Bookmarks first = Build();
        first.Set('a', "/home/user");
        first.Set('b', "/tmp");
        first.Save();

        Bookmarks second = Build();
        second.Load();
        second.Remove('a');
        second.Save();

        Bookmarks reader = Build();
        reader.Load();

        Assert.Null(reader.Get('a'));
        Assert.Equal("/tmp", reader.Get('b'));
    }

    [Fact]
    public void Save_CanLeaveOutThePreviousDirectoryBookmark()
    {
        // It changes on every navigation and means nothing next session.
        Bookmarks bookmarks = Build();
        bookmarks.SaveBacktickBookmark = false;
        bookmarks.Set('h', "/home/user");
        bookmarks.RememberPrevious("/tmp");
        bookmarks.Save();

        Bookmarks reader = Build();
        reader.Load();

        Assert.Equal("/home/user", reader.Get('h'));
        Assert.Null(reader.Get('\''));
    }

    [Fact]
    public void RemoveStale_DropsBookmarksToDirectoriesThatAreGone()
    {
        Bookmarks bookmarks = Build();
        bookmarks.Set('h', "/home/user");
        bookmarks.Set('x', "/no/such/place");

        Assert.Equal(1, bookmarks.RemoveStale());
        Assert.Equal("/home/user", bookmarks.Get('h'));
        Assert.Null(bookmarks.Get('x'));
    }

    [Fact]
    public void Load_SurvivesAnUnreadableFile()
    {
        // A broken bookmarks file must not stop Canger starting.
        Directory.CreateDirectory(Path.Join(_root, "in-the-way"));
        Bookmarks bookmarks = new(_fileSystem, Path.Join(_root, "in-the-way")) { AutoSave = false };

        bookmarks.Load();

        Assert.Empty(bookmarks.Entries);
    }
}
