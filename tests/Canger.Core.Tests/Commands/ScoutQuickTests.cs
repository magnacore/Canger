// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What a scout does while the pattern is still being typed.
/// </summary>
/// <remarks>
/// <para>
/// Reported as "<c>f</c> narrows the listing where ranger moves the cursor". <c>find</c> is
/// <c>scout -aets</c> — no <c>-f</c> and no <c>-p</c> — so ranger applies no filter at all: it
/// moves the cursor as you type and opens on a unique match. Canger narrowed whenever <c>-t</c>
/// was set, which is every one of these aliases, and never moved.
/// </para>
/// <para>
/// Ranger narrows for <c>-f</c>, or for <c>-p</c> with <c>-t</c>, and calls
/// <c>_count(move=asyoutype)</c> either way (<c>config/commands.py</c>, <c>scout.quick</c>).
/// </para>
/// </remarks>
public class ScoutQuickTests
{
    private static FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/betamax.txt")
            .AddFile("/home/gamma.txt");

        return new FakeFileManager(fs, "/home");
    }

    /// <summary>Runs the quick pass, as the console does on every keystroke.</summary>
    private static bool Quick(FakeFileManager manager, string line) =>
        manager.Dispatcher.Build(line)?.Quick() ?? false;

    private static IReadOnlyList<string> Listing(FakeFileManager manager) =>
        [.. manager.CurrentTab.Current.Entries.Select(e => e.RelativePath)];

    private static string? Cursor(FakeFileManager manager) =>
        manager.CurrentTab.Selected?.RelativePath;

    [Fact]
    public void FindMovesTheCursorAndLeavesTheListingAlone()
    {
        // The reported defect. `find` is `scout -aets`.
        FakeFileManager manager = Manager();

        Quick(manager, "scout -aets gam");

        Assert.Equal("gamma.txt", Cursor(manager));
        Assert.Equal(["alpha.txt", "beta.txt", "betamax.txt", "gamma.txt"], Listing(manager));
    }

    [Fact]
    public void IncrementalSearchMovesTheCursorAndLeavesTheListingAlone()
    {
        // `search_inc` is `scout -rts` — the same shape, and equally wrong before.
        FakeFileManager manager = Manager();

        Quick(manager, "scout -rts gam");

        Assert.Equal("gamma.txt", Cursor(manager));
        Assert.Equal(4, Listing(manager).Count);
    }

    [Fact]
    public void TravelNarrowsAndMoves()
    {
        // `travel` is `scout -aefklst`, and the `-f` is what makes it narrow. It must still do
        // both — the fix is about which flags narrow, not about narrowing less.
        FakeFileManager manager = Manager();

        Quick(manager, "scout -aefklst gam");

        Assert.Equal(["gamma.txt"], Listing(manager));
        Assert.Equal("gamma.txt", Cursor(manager));
    }

    [Fact]
    public void FilterStillNarrowsAsItIsTyped()
    {
        // `filter` is `scout -prts`: no `-f`, so the rule has to admit `-p` with `-t` as well or
        // this would have become a regression while fixing `find`.
        FakeFileManager manager = Manager();

        Quick(manager, "scout -prts beta");

        Assert.Equal(["beta.txt", "betamax.txt"], Listing(manager));
    }

    [Fact]
    public void AutoOpensOnAUniqueMatchWithoutNarrowing()
    {
        // `-a` closes the prompt when exactly one thing matches. The test is the number of
        // matches, not the number of rows left: without narrowing the listing never shrinks, so
        // counting rows meant `find` could only ever auto-open in a directory holding one file.
        Assert.True(Quick(Manager(), "scout -aets gam"));
    }

    [Fact]
    public void DoesNotAutoOpenWhileSeveralThingsMatch()
    {
        Assert.False(Quick(Manager(), "scout -aets beta"));
    }

    [Fact]
    public void DoesNotAutoOpenOnAnEmptyPattern()
    {
        // Ranger's `_count` returns zero for an empty pattern and for a lone `.`, so opening the
        // prompt cannot immediately close it again.
        Assert.False(Quick(Manager(), "scout -aets "));
        Assert.False(Quick(Manager(), "scout -aets ."));
    }

    [Fact]
    public void DoesNothingAtAllWithoutTheAsYouTypeFlag()
    {
        // `search` is `scout -rs` and `mark` is `scout -mr`: nothing happens until Enter.
        FakeFileManager manager = Manager();
        manager.CurrentTab.MoveCursorTo(manager.CurrentTab.Current.Entries[0]);

        Assert.False(Quick(manager, "scout -rs gam"));
        Assert.Equal("alpha.txt", Cursor(manager));
        Assert.Equal(4, Listing(manager).Count);
    }

    [Fact]
    public void SearchesOnwardFromTheCursorAndWraps()
    {
        // Ranger rotates the listing to start at the cursor, so a match already behind you is
        // found by wrapping rather than by jumping backwards.
        FakeFileManager manager = Manager();
        manager.CurrentTab.MoveCursorTo(manager.CurrentTab.Current.Entries.First(
            e => e.RelativePath == "gamma.txt"));

        Quick(manager, "scout -aets alpha");

        Assert.Equal("alpha.txt", Cursor(manager));
    }
}
