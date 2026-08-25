// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;

namespace Canger.Core.Tests.Input;

public class KeyBufferTests
{
    private static KeyBuffer BufferWith(params (string Binding, string Command)[] bindings)
    {
        KeyMap map = new();
        foreach ((string binding, string command) in bindings)
        {
            map.Bind(binding, command);
        }

        return new KeyBuffer(map);
    }

    /// <summary>Feeds a string of plain characters and returns the last result.</summary>
    private static string? Type(KeyBuffer buffer, string keys)
    {
        string? result = null;
        foreach (char key in keys)
        {
            result = buffer.Add(key);
        }

        return result;
    }

    [Fact]
    public void Add_CompletesASingleKeyBinding()
    {
        KeyBuffer buffer = BufferWith(("q", "quit"));

        Assert.Equal("quit", buffer.Add('q'));
        Assert.True(buffer.IsFinished);
        Assert.False(buffer.HasFailed);
    }

    [Fact]
    public void Add_WaitsForTheRestOfAMultiKeyBinding()
    {
        KeyBuffer buffer = BufferWith(("gg", "move to=0"));

        Assert.Null(buffer.Add('g'));
        Assert.False(buffer.IsFinished);

        Assert.Equal("move to=0", buffer.Add('g'));
        Assert.True(buffer.IsFinished);
    }

    [Fact]
    public void Add_FailsOnASequenceThatGoesNowhere()
    {
        KeyBuffer buffer = BufferWith(("gg", "move to=0"));

        Assert.Null(Type(buffer, "gx"));
        Assert.True(buffer.IsFinished);
        Assert.True(buffer.HasFailed);
    }

    [Fact]
    public void Add_CollectsLeadingDigitsAsAQuantifier()
    {
        KeyBuffer buffer = BufferWith(("dd", "cut"));

        Assert.Null(Type(buffer, "12"));
        Assert.Equal(12, buffer.Quantifier);
        Assert.False(buffer.IsFinished);

        Assert.Equal("cut", Type(buffer, "dd"));
        Assert.Equal(12, buffer.Quantifier);
    }

    [Fact]
    public void Add_StopsCollectingTheQuantifierAtTheFirstNonDigit()
    {
        // Only leading digits count; a digit inside a binding is part of the binding.
        KeyBuffer buffer = BufferWith(("d1", "weird"));

        Assert.Equal("weird", Type(buffer, "3d1"));
        Assert.Equal(3, buffer.Quantifier);
    }

    [Fact]
    public void Add_TreatsZeroAsAQuantifierRatherThanAKey()
    {
        KeyBuffer buffer = BufferWith(("j", "move down=1"));

        Assert.Equal("move down=1", Type(buffer, "0j"));
        Assert.Equal(0, buffer.Quantifier);
    }

    [Fact]
    public void Add_LeavesTheQuantifierUnsetWhenNoDigitsWereTyped()
    {
        KeyBuffer buffer = BufferWith(("j", "move down=1"));

        buffer.Add('j');

        Assert.Null(buffer.Quantifier);
    }

    [Fact]
    public void Add_TypesDigitsNormallyWhenTheMapDisablesQuantifiers()
    {
        // The console binds <allow_quantifiers> false so that digits reach the command line.
        KeyMap map = new();
        map.Bind("<allow_quantifiers>", "false");
        map.Bind("1", "typed one");
        KeyBuffer buffer = new(map);

        Assert.Equal("typed one", buffer.Add('1'));
        Assert.Null(buffer.Quantifier);
    }

    [Fact]
    public void Add_PrefersAnExactBindingOverTheWildcard()
    {
        KeyBuffer buffer = BufferWith(("ga", "exact"), ("g<any>", "wildcard"));

        Assert.Equal("exact", Type(buffer, "ga"));
        Assert.Empty(buffer.Wildcards);
    }

    [Fact]
    public void Add_FallsBackToTheWildcardAndRecordsTheKey()
    {
        KeyBuffer buffer = BufferWith(("ga", "exact"), ("g<any>", "wildcard"));

        Assert.Equal("wildcard", Type(buffer, "gz"));
        Assert.Equal(['z'], buffer.Wildcards);
    }

    [Fact]
    public void Add_NeverMatchesEscapeAgainstTheWildcard()
    {
        // Escape must always be able to abandon a partly typed sequence.
        KeyBuffer buffer = BufferWith(("g<any>", "wildcard"));

        buffer.Add('g');
        Assert.Null(buffer.Add(KeyCodes.Escape));
        Assert.True(buffer.HasFailed);
    }

    [Fact]
    public void Add_RecordsEveryWildcardInOrder()
    {
        KeyBuffer buffer = BufferWith(("<any><any>", "two wildcards"));

        Assert.Equal("two wildcards", Type(buffer, "xy"));
        Assert.Equal(['x', 'y'], buffer.Wildcards);
    }

    [Fact]
    public void Add_FiresAPassiveActionWithoutEndingTheSequence()
    {
        // map m<bg> draw_bookmarks shows the overlay the moment m is pressed, while m<any>
        // still waits for the bookmark letter.
        KeyBuffer buffer = BufferWith(("m<bg>", "draw_bookmarks"), ("m<any>", "set_bookmark %any"));

        Assert.Equal("draw_bookmarks", buffer.Add('m'));
        Assert.False(buffer.IsFinished);

        Assert.Equal("set_bookmark %any", buffer.Add('a'));
        Assert.True(buffer.IsFinished);
        Assert.Equal(['a'], buffer.Wildcards);
    }

    [Fact]
    public void Clear_DiscardsEverythingTyped()
    {
        KeyBuffer buffer = BufferWith(("gg", "move to=0"));

        Type(buffer, "3g");
        buffer.Clear();

        Assert.Null(buffer.Quantifier);
        Assert.Empty(buffer.Keys);
        Assert.False(buffer.IsFinished);
        Assert.Equal("move to=0", Type(buffer, "gg"));
    }

    [Fact]
    public void Use_ClearsTheBufferWhenTheMapChanges()
    {
        KeyMap browser = new();
        browser.Bind("gg", "move to=0");
        KeyMap console = new();
        console.Bind("<CR>", "execute");

        KeyBuffer buffer = new(browser);
        buffer.Add('g');

        buffer.Use(console);

        Assert.Empty(buffer.Keys);
        Assert.Equal("execute", buffer.Add(KeyCodes.Enter));
    }

    [Fact]
    public void Use_KeepsThePartialSequenceWhenTheMapIsUnchanged()
    {
        KeyMap map = new();
        map.Bind("gg", "move to=0");
        KeyBuffer buffer = new(map);

        buffer.Add('g');
        buffer.Use(map);

        Assert.Equal("move to=0", buffer.Add('g'));
    }

    [Fact]
    public void ToString_RendersWhatTheTitleBarShows()
    {
        KeyBuffer buffer = BufferWith(("gg", "move to=0"));

        Type(buffer, "3g");

        Assert.Equal("3g", buffer.ToString());
    }
}
