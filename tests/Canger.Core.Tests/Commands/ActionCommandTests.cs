// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// The ranger actions that had no Canger command at all.
/// </summary>
/// <remarks>
/// Sixteen bindings in a stock configuration named commands that did not exist, so pressing the
/// key did nothing and said nothing. These tests exist mostly so that cannot happen again
/// quietly; <c>--config</c> now resolves every browser binding against the registry, which is the
/// check that found them.
/// </remarks>
public class ActionCommandTests
{
    private static FakeFileManager Manager() =>
        new(new InMemoryFileSystem()
                .AddDirectory("/home/adir")
                .AddDirectory("/home/bdir")
                .AddFile("/home/one.txt")
                .AddFile("/home/two.txt"),
            "/home");

    private static string? CursorName(FakeFileManager manager) =>
        manager.CurrentTab.Selected?.Basename;

    // ---- jump_non ------------------------------------------------------------------

    [Fact]
    public void JumpNon_FromADirectoryGoesToTheFirstFile()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.MoveCursor(0);

        manager.Execute("jump_non");

        Assert.Equal("one.txt", CursorName(manager));
    }

    [Fact]
    public void JumpNon_FromAFileGoesToTheNextDirectory()
    {
        // Wrapping, because from the last file the only directories are behind the cursor.
        FakeFileManager manager = Manager();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == "two.txt"));

        manager.Execute("jump_non -w");

        Assert.Equal("adir", CursorName(manager));
    }

    [Fact]
    public void JumpNon_StaysPutWithoutWrappingWhenThereIsNothingAhead()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == "two.txt"));

        manager.Execute("jump_non");

        Assert.Equal("two.txt", CursorName(manager));
    }

    // ---- search_next ---------------------------------------------------------------

    [Fact]
    public void SearchNext_RepeatsTheLastTextSearch()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.LastSearch = "dir";
        manager.SearchMethod = "search";
        manager.CurrentTab.MoveCursor(0);

        manager.Execute("search_next");

        Assert.Equal("bdir", CursorName(manager));
    }

    [Fact]
    public void SearchNext_WrapsAroundTheListing()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.LastSearch = "adir";
        manager.SearchMethod = "search";
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == "two.txt"));

        manager.Execute("search_next");

        Assert.Equal("adir", CursorName(manager));
    }

    [Fact]
    public void SearchNext_StepsThroughTaggedEntries()
    {
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/two.txt"]);
        manager.CurrentTab.MoveCursor(0);

        manager.Execute("search_next order=tag");

        Assert.Equal("two.txt", CursorName(manager));
    }

    [Fact]
    public void SearchNext_RemembersTheOrderItWasGiven()
    {
        // `cm` then `n` has to keep walking by modification time, so the order sticks.
        FakeFileManager manager = Manager();

        manager.Execute("search_next order=mtime");

        Assert.Equal("mtime", manager.SearchMethod);
    }

    [Fact]
    public void SearchNext_SaysSoWhenThereIsNothingToFind()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.LastSearch = "nothing matches this";
        manager.SearchMethod = "search";

        manager.Execute("search_next");

        Assert.Contains(manager.Messages, m => m.Message.Contains("nothing further",
                                                                 StringComparison.Ordinal));
    }

    // ---- visual mode ---------------------------------------------------------------

    [Fact]
    public void ToggleVisualMode_TurnsItOnThenOffAgain()
    {
        FakeFileManager manager = Manager();

        manager.Execute("toggle_visual_mode");
        Assert.True(manager.IsVisualMode);

        manager.Execute("toggle_visual_mode");
        Assert.False(manager.IsVisualMode);
    }

    [Fact]
    public void ToggleVisualMode_ReverseAsksForTheUnmarkingVariety()
    {
        FakeFileManager manager = Manager();

        manager.Execute("toggle_visual_mode reverse=True");

        Assert.Contains("visual-reverse", manager.ModeChanges);
    }

    [Fact]
    public void ToggleVisualMode_WithAQuantifierMarksThatManyOutright()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.MoveCursor(0);

        manager.Execute("toggle_visual_mode", quantifier: 3);

        Assert.Equal(3, manager.CurrentTab.Current.MarkedEntries.Count);
    }

    [Fact]
    public void ChangeMode_ComplainsWithNoModeNamed()
    {
        FakeFileManager manager = Manager();

        manager.Execute("change_mode");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    // ---- the rest ------------------------------------------------------------------

    [Fact]
    public void ScrollPreview_UsesTheQuantifierOverTheArgument()
    {
        FakeFileManager manager = Manager();

        manager.Execute("scroll_preview 1", quantifier: 7);

        Assert.Equal(7, manager.PreviewScroll);
    }

    [Fact]
    public void ScrollPreview_TakesADirectionFromTheArgument()
    {
        FakeFileManager manager = Manager();

        manager.Execute("scroll_preview -4");

        Assert.Equal(-4, manager.PreviewScroll);
    }

    [Fact]
    public void DisplayLog_ShowsWhatWasSaidEarlier()
    {
        FakeFileManager manager = Manager();
        manager.Notify("something happened");

        manager.Execute("display_log");

        Assert.Contains(manager.PagerText,
                        page => page.Contains("something happened", StringComparison.Ordinal));
    }

    [Fact]
    public void DisplayLog_SaysSoWhenNothingHasHappened()
    {
        FakeFileManager manager = Manager();

        manager.Execute("display_log");

        Assert.Contains(manager.PagerText,
                        page => page.Contains("No messages!", StringComparison.Ordinal));
    }

    [Fact]
    public void Help_AsksWhichKindOfHelpIsWanted()
    {
        FakeFileManager manager = Manager();

        manager.Execute("help");

        // Four choices, plus q to abandon, exactly as ranger asks it.
        Assert.Equal(['m', 'q', 'k', 'c', 's'], manager.PendingQuestion?.Choices);
        Assert.Contains("[m]an page", manager.PendingQuestion?.Question ?? string.Empty,
                        StringComparison.Ordinal);
    }

    [Theory]
    [InlineData('k')]
    [InlineData('c')]
    [InlineData('s')]
    public void Help_SendsTheDumpsToTheUsersOwnPager(char answer)
    {
        // They went to Canger's pager, which only scrolls — so `/` did nothing and a few hundred
        // lines of bindings had to be read by eye. Ranger hands these three to `$PAGER`
        // (`core/actions.py:1483-1484`, `1514`), which is where their search comes from.
        FakeFileManager manager = Manager();

        manager.Execute("help");
        manager.Answer(answer);

        Assert.Single(manager.ExternalPagerText);
        Assert.Empty(manager.PagerText);
        Assert.NotEmpty(manager.ExternalPagerText[0]);
    }

    [Fact]
    public void Help_StillShowsTheManPageThroughMan()
    {
        // `m` is not a dump: man formats and pages it itself, so it does not go through either
        // pager. This asserted the whole command line, `man canger` — which is what broke, since
        // that asks the system for an installed page and Canger is normally run from wherever it
        // was unpacked. The intent above is worth keeping; the exact spelling was not.
        FakeFileManager manager = Manager();

        manager.Execute("help");
        manager.Answer('m');

        Assert.Empty(manager.ExternalPagerText);

        (string Command, string Flags) launched = Assert.Single(manager.LaunchedPrograms);

        Assert.Contains("man -l", launched.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("man canger", launched.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void DrawBookmarks_AsksForTheWindowRatherThanWritingALine()
    {
        // It used to put every bookmark on the status line, which is unreadable past a handful
        // and is not what `<bg>` is for.
        FakeFileManager manager = Manager();
        manager.Bookmarks.Set('k', "/home");

        manager.Execute("draw_bookmarks");

        Assert.True(manager.BookmarksShown);
        Assert.Empty(manager.Messages);
    }

    [Fact]
    public void DrawBookmarks_SaysSoWhenThereAreNone()
    {
        FakeFileManager manager = Manager();

        manager.Execute("draw_bookmarks");

        Assert.False(manager.BookmarksShown);
        Assert.Contains(manager.Messages,
                        m => m.Message.Contains("no bookmarks", StringComparison.Ordinal));
    }

    [Fact]
    public void Exit_Quits()
    {
        FakeFileManager manager = Manager();

        manager.Execute("exit");

        Assert.True(manager.HasQuit);
    }

    [Fact]
    public void PasteSymlink_SaysSoWithAnEmptyCopyBuffer()
    {
        FakeFileManager manager = Manager();

        manager.Execute("paste_symlink");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void Chmod_RejectsSomethingThatIsNotOctal()
    {
        FakeFileManager manager = Manager();

        manager.Execute("chmod 899");

        Assert.Contains(manager.Messages,
                        m => m.IsError && m.Message.Contains("octal", StringComparison.Ordinal));
    }

    [Fact]
    public void Chmod_NeedsEitherAnArgumentOrAQuantifier()
    {
        FakeFileManager manager = Manager();

        manager.Execute("chmod");

        Assert.Contains(manager.Messages,
                        m => m.IsError && m.Message.Contains("Syntax", StringComparison.Ordinal));
    }
}
