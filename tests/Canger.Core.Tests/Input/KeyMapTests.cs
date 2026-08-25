// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;

namespace Canger.Core.Tests.Input;

public class KeyMapTests
{
    [Fact]
    public void Bind_StoresASingleKeyBinding()
    {
        KeyMap map = new();
        map.Bind("q", "quit");

        Assert.Equal("quit", map.Find(KeyBindingParser.Parse("q"))?.Command);
    }

    [Fact]
    public void Bind_StoresAMultiKeyBinding()
    {
        KeyMap map = new();
        map.Bind("gg", "move to=0");

        Assert.Null(map.Find(KeyBindingParser.Parse("g"))?.Command);
        Assert.Equal("move to=0", map.Find(KeyBindingParser.Parse("gg"))?.Command);
    }

    [Fact]
    public void Bind_KeepsTheWholeCommandIncludingSpaces()
    {
        KeyMap map = new();
        map.Bind("dj", "eval fm.cut(dirarg=dict(down=1), narg=quantifier)");

        Assert.Equal("eval fm.cut(dirarg=dict(down=1), narg=quantifier)",
                     map.Find(KeyBindingParser.Parse("dj"))?.Command);
    }

    [Fact]
    public void Bind_ReplacesAPrefixCommandWithABranch()
    {
        // Order matters: binding gg after g discards the g binding entirely, because leaves and
        // branches share one structure. This is why cc.conf is applied strictly top to bottom.
        KeyMap map = new();
        map.Bind("g", "first");
        map.Bind("gg", "second");

        Assert.Null(map.Find(KeyBindingParser.Parse("g"))?.Command);
        Assert.Equal("second", map.Find(KeyBindingParser.Parse("gg"))?.Command);
    }

    [Fact]
    public void Bind_ReplacesABranchWithACommandWhenBoundTheOtherWayRound()
    {
        KeyMap map = new();
        map.Bind("gg", "first");
        map.Bind("gh", "second");
        map.Bind("g", "third");

        Assert.Equal("third", map.Find(KeyBindingParser.Parse("g"))?.Command);
        Assert.Null(map.Find(KeyBindingParser.Parse("gg")));
        Assert.Null(map.Find(KeyBindingParser.Parse("gh")));
    }

    [Fact]
    public void Bind_OverwritesAnExistingBinding()
    {
        KeyMap map = new();
        map.Bind("q", "quit");
        map.Bind("q", "quitall");

        Assert.Equal("quitall", map.Find(KeyBindingParser.Parse("q"))?.Command);
    }

    [Fact]
    public void Copy_DuplicatesASingleBinding()
    {
        KeyMap map = new();
        map.Bind("<UP>", "move up=1");
        map.Copy("<UP>", "k");

        Assert.Equal("move up=1", map.Find(KeyBindingParser.Parse("k"))?.Command);
        Assert.Equal("move up=1", map.Find(KeyBindingParser.Parse("<UP>"))?.Command);
    }

    [Fact]
    public void Copy_DuplicatesAWholeBranch()
    {
        // copymap m<bg> um<bg> moves a prefix and everything under it in one line.
        KeyMap map = new();
        map.Bind("m<any>", "set_bookmark %any");
        map.Bind("m<bg>", "draw_bookmarks");

        map.Copy("m", "um");

        Assert.Equal("set_bookmark %any", map.Find([.. KeyBindingParser.Parse("um"), KeyCodes.Any])?.Command);
        Assert.Equal("draw_bookmarks", map.Find(KeyBindingParser.Parse("um"))?.PassiveCommand);
    }

    [Fact]
    public void Copy_TakesAnIndependentCopy()
    {
        KeyMap map = new();
        map.Bind("ga", "original");
        map.Copy("g", "z");

        map.Bind("ga", "changed");

        Assert.Equal("changed", map.Find(KeyBindingParser.Parse("ga"))?.Command);
        Assert.Equal("original", map.Find(KeyBindingParser.Parse("za"))?.Command);
    }

    [Fact]
    public void Copy_ReflectsTheStateAtTheMomentItRuns()
    {
        // The sample configuration depends on this: a binding rebound after a copymap does not
        // change what the copy holds.
        KeyMap map = new();
        map.Bind("<pagedown>", "scroll");
        map.Copy("<pagedown>", "n");
        map.Bind("<pagedown>", "move task");

        Assert.Equal("scroll", map.Find(KeyBindingParser.Parse("n"))?.Command);
        Assert.Equal("move task", map.Find(KeyBindingParser.Parse("<pagedown>"))?.Command);
    }

    [Fact]
    public void Copy_ThrowsWhenTheSourceIsNotBound() =>
        Assert.Throws<KeyBindingException>(() => new KeyMap().Copy("zz", "yy"));

    [Fact]
    public void Unbind_RemovesABindingAndPrunesEmptyBranches()
    {
        KeyMap map = new();
        map.Bind("gg", "move to=0");

        Assert.True(map.Unbind("gg"));
        Assert.Null(map.Find(KeyBindingParser.Parse("gg")));
        // The now-childless g branch is pruned rather than left behind.
        Assert.Null(map.Find(KeyBindingParser.Parse("g")));
    }

    [Fact]
    public void Unbind_LeavesSiblingsAlone()
    {
        KeyMap map = new();
        map.Bind("gg", "first");
        map.Bind("gh", "second");

        Assert.True(map.Unbind("gg"));
        Assert.Equal("second", map.Find(KeyBindingParser.Parse("gh"))?.Command);
    }

    [Fact]
    public void Unbind_ReportsFailureForAnUnboundSequence() =>
        Assert.False(new KeyMap().Unbind("zz"));

    [Fact]
    public void Enumerate_ListsEveryBindingWithItsCommand()
    {
        KeyMap map = new();
        map.Bind("q", "quit");
        map.Bind("gg", "move to=0");
        map.Bind("G", "move to=-1");

        Dictionary<string, string> bindings = map.Enumerate()
            .ToDictionary(b => KeyCodes.ToDisplayString(b.Keys), b => b.Command, StringComparer.Ordinal);

        Assert.Equal(3, bindings.Count);
        Assert.Equal("quit", bindings["q"]);
        Assert.Equal("move to=0", bindings["gg"]);
        Assert.Equal("move to=-1", bindings["G"]);
    }
}
