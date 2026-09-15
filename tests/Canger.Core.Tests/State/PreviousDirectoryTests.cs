// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// What sets the previous-directory bookmark, which <c>`</c> and <c>'</c> jump to.
/// </summary>
/// <remarks>
/// Reported as a difference from ranger. Ranger sets this mark in three places and nowhere else —
/// a <c>cd</c>, entering a bookmark, and quitting (<c>core/actions.py:594-609</c>, <c>:916-926</c>,
/// <c>core/fm.py:547</c>) — while <c>fm.enter_dir</c> and ordinary movement leave it alone,
/// <c>enter_dir</c> defaulting to <c>remember=False</c>. Canger set it on every tab move, so
/// walking into a folder and out again overwrote it and the key stopped being a way back to where
/// the user had jumped from.
/// </remarks>
public class PreviousDirectoryTests
{
    private static FakeFileManager Manager() =>
        new(new InMemoryFileSystem()
                .AddDirectory("/home")
                .AddDirectory("/home/one")
                .AddDirectory("/home/one/deep")
                .AddDirectory("/home/two"),
            "/home/one");

    private static string? Mark(FakeFileManager manager) =>
        manager.Bookmarks.Get(Bookmarks.PreviousDirectory);

    [Fact]
    public void WalkingAboutLeavesTheMarkAlone()
    {
        // The reported difference. In ranger `h` and `l` go through `thistab.enter_dir`, which
        // does not remember; here every move did.
        FakeFileManager manager = Manager();
        manager.Execute("cd /home/two");

        manager.Execute("move right=1");
        manager.Execute("move left=1");
        manager.CurrentTab.Enter("/home/one/deep",
                                 cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/home/one", Mark(manager));
    }

    [Fact]
    public void ChangingDirectoryRemembersWhereYouWere()
    {
        FakeFileManager manager = Manager();

        manager.Execute("cd /home/two");

        Assert.Equal("/home/one", Mark(manager));
    }

    [Fact]
    public void ChangingToWhereYouAlreadyAreDoesNot()
    {
        // Ranger remembers only when the directory really changed, so `cd .` does not point the
        // mark at the place you are standing and cost you the way back.
        FakeFileManager manager = Manager();
        manager.Execute("cd /home/two");

        manager.Execute("cd /home/two");

        Assert.Equal("/home/one", Mark(manager));
    }

    [Fact]
    public void EnteringABookmarkRemembersWhereYouWere()
    {
        FakeFileManager manager = Manager();
        manager.Bookmarks.Set('b', "/home/two");

        manager.Execute("enter_bookmark b");

        Assert.Equal("/home/one", Mark(manager));
        Assert.Equal("/home/two", manager.CurrentTab.Current.Path);
    }

    [Fact]
    public void TheKeyPressedTwiceReturnsYouToWhereYouStarted()
    {
        // The whole point of the mark, and the shape the reporter uses: jump somewhere, press it
        // to come back, press it again to go there once more.
        FakeFileManager manager = Manager();
        manager.Execute("cd /home/two");

        manager.Execute("enter_bookmark '");
        Assert.Equal("/home/one", manager.CurrentTab.Current.Path);

        manager.Execute("enter_bookmark '");
        Assert.Equal("/home/two", manager.CurrentTab.Current.Path);
    }

    [Fact]
    public void TheBacktickIsTheSameMarkAsTheQuote()
    {
        FakeFileManager manager = Manager();
        manager.Execute("cd /home/two");

        manager.Execute("enter_bookmark `");

        Assert.Equal("/home/one", manager.CurrentTab.Current.Path);
    }
}
