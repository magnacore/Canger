// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

public class HistoryTests
{
    [Fact]
    public void Add_RecordsEntriesInOrder()
    {
        History<string> history = new(10);
        history.Add("a");
        history.Add("b");

        Assert.Equal(["a", "b"], history.Entries);
        Assert.Equal("b", history.Current);
    }

    [Fact]
    public void Add_IgnoresARepeatOfTheCurrentEntry()
    {
        History<string> history = new(10);
        history.Add("a");
        history.Add("a");

        Assert.Single(history.Entries);
    }

    [Fact]
    public void Add_MovesARepeatedEntryToTheEndWhenUniqueIsRequested()
    {
        // The console wants this, so that pressing up does not walk through the same command
        // several times.
        History<string> history = new(10, unique: true);
        history.Add("first");
        history.Add("second");
        history.Add("first");

        Assert.Equal(["second", "first"], history.Entries);
    }

    [Fact]
    public void Add_DiscardsTheForwardPath()
    {
        History<string> history = new(10);
        history.Add("a");
        history.Add("b");
        history.Add("c");
        history.Back();
        history.Back();

        history.Add("d");

        Assert.Equal(["a", "d"], history.Entries);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void Add_TrimsTheOldestEntriesAtCapacity()
    {
        History<string> history = new(3);
        foreach (string entry in (string[])["a", "b", "c", "d"])
        {
            history.Add(entry);
        }

        Assert.Equal(["b", "c", "d"], history.Entries);
    }

    [Fact]
    public void BackAndForward_WalkTheHistory()
    {
        History<string> history = new(10);
        history.Add("a");
        history.Add("b");
        history.Add("c");

        Assert.Equal("b", history.Back());
        Assert.Equal("a", history.Back());
        Assert.Equal("b", history.Forward());
    }

    [Fact]
    public void BackAndForward_StopAtTheEnds()
    {
        History<string> history = new(10);
        history.Add("only");

        Assert.False(history.CanGoBack);
        Assert.Null(history.Back());
        Assert.False(history.CanGoForward);
        Assert.Null(history.Forward());
    }

    [Fact]
    public void Move_ClampsRatherThanFailing()
    {
        History<string> history = new(10);
        history.Add("a");
        history.Add("b");
        history.Add("c");

        Assert.Equal("a", history.Move(-99));
        Assert.Equal("c", history.Move(99));
    }

    [Fact]
    public void Search_FindsTheNearestMatchInADirection()
    {
        History<string> history = new(10);
        foreach (string entry in (string[])["git status", "ls -la", "git commit", "cd /tmp"])
        {
            history.Add(entry);
        }

        Assert.Equal("git commit", history.Search("git", -1));
        Assert.Equal("git status", history.Search("git", -1));
    }

    [Fact]
    public void Search_ReturnsNothingWhenNoEntryMatches()
    {
        History<string> history = new(10);
        history.Add("one");
        history.Add("two");

        Assert.Null(history.Search("zzz", -1));
    }

    [Fact]
    public void InheritFrom_AdoptsAnotherHistory()
    {
        // A newly opened tab starts knowing where its parent has been rather than blank.
        History<string> parent = new(10);
        parent.Add("a");
        parent.Add("b");

        History<string> child = new(10);
        child.InheritFrom(parent);

        Assert.Equal(["a", "b"], child.Entries);
        Assert.Equal("b", child.Current);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        History<string> history = new(10);
        history.Add("a");

        history.Clear();

        Assert.True(history.IsEmpty);
        Assert.Null(history.Current);
    }
}
