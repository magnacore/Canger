// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.Input;
using Canger.Core.Settings;

namespace Canger.Core.Tests.Configuration;

public class KeyBindingDirectiveTests
{
    private static (ConfigurationReader Reader, KeyMaps Maps) Build()
    {
        KeyMaps maps = new();
        ConfigurationReader reader = new();
        reader.Register(new KeyBindingDirective(maps));
        reader.Register(new SetDirective(new SettingsStore()));
        return (reader, maps);
    }

    private static string? CommandFor(KeyMap map, string binding) =>
        map.Find(KeyBindingParser.Parse(binding))?.Command;

    [Fact]
    public void Map_BindsInTheBrowserContext()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines(["map q quit", "map gg move to=0"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("quit", CommandFor(maps.Browser, "q"));
        Assert.Equal("move to=0", CommandFor(maps.Browser, "gg"));
    }

    [Fact]
    public void EachDirectiveBindsInItsOwnContext()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines([
            "map q quit",
            "cmap <ESC> console_close",
            "pmap q pager_close",
            "tmap q taskview_close",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal("quit", CommandFor(maps.Browser, "q"));
        Assert.Equal("console_close", CommandFor(maps.Console, "<ESC>"));
        Assert.Equal("pager_close", CommandFor(maps.Pager, "q"));
        Assert.Equal("taskview_close", CommandFor(maps.TaskView, "q"));

        // The contexts are independent: q means something different in each.
        Assert.Null(CommandFor(maps.Console, "q"));
    }

    [Fact]
    public void Map_KeepsTheCommandVerbatimIncludingSpacesAndPunctuation()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines(["map X shell -f chmod  u+x  --  %s"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("shell -f chmod  u+x  --  %s", CommandFor(maps.Browser, "X"));
    }

    [Fact]
    public void Map_TranslatesRangersOwnEvalBindings()
    {
        // A configuration carried over from ranger binds Escape and Enter to Python. Left
        // untranslated they leave the user in a prompt with no way out.
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines([
            "cmap <ESC> eval fm.ui.console.close()",
            "cmap <CR>  eval fm.ui.console.execute()",
            "map  dj    eval fm.cut(dirarg=dict(down=1), narg=quantifier)",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal("console_close", CommandFor(maps.Console, "<ESC>"));
        Assert.Equal("console_accept", CommandFor(maps.Console, "<CR>"));
        Assert.Equal("cut down=1", CommandFor(maps.Browser, "dj"));
    }

    [Fact]
    public void Map_LeavesAnEvalItDoesNotRecogniseAlone()
    {
        // The table covers ranger's own forms; anything else is the user's own C# and must
        // reach the eval machinery untouched.
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines(["map Q eval fm.Notify(\"mine\");"]);

        Assert.Equal("eval fm.Notify(\"mine\");", CommandFor(maps.Browser, "Q"));
    }

    [Fact]
    public void Map_LeavesMacrosUnexpanded()
    {
        // Macros are resolved when the binding fires, so they see the state at that moment.
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines(["map yp yank path", "map \"<any> tag_toggle tag=%any"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("tag_toggle tag=%any", CommandFor(maps.Browser, "\"<any>"));
    }

    [Fact]
    public void Copymap_DuplicatesToSeveralDestinationsAtOnce()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines([
            "map <PAGEDOWN> move down=1 pages=True",
            "copymap <PAGEDOWN> n f <C-F> <Space>",
        ]);

        Assert.Empty(reader.Errors);
        foreach (string binding in (string[])["n", "f", "<C-F>", "<Space>"])
        {
            Assert.Equal("move down=1 pages=True", CommandFor(maps.Browser, binding));
        }
    }

    [Fact]
    public void Copymap_AppliesToTheDirectivesOwnContext()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines([
            "pmap <DOWN> pager_move down=1",
            "copypmap <DOWN> j <C-n>",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal("pager_move down=1", CommandFor(maps.Pager, "j"));
        Assert.Null(CommandFor(maps.Browser, "j"));
    }

    [Fact]
    public void Unmap_RemovesBindings()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines(["map q quit", "map Q quitall", "unmap q Q"]);

        Assert.Empty(reader.Errors);
        Assert.Null(CommandFor(maps.Browser, "q"));
        Assert.Null(CommandFor(maps.Browser, "Q"));
    }

    [Fact]
    public void DeprecatedUnmapSpellingsStillWork()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines([
            "cmap <C-t> transpose",
            "pmap x something",
            "tmap y something",
            "cunmap <C-t>",
            "punmap x",
            "tunmap y",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Null(CommandFor(maps.Console, "<C-t>"));
        Assert.Null(CommandFor(maps.Pager, "x"));
        Assert.Null(CommandFor(maps.TaskView, "y"));
    }

    [Fact]
    public void OrderDecidesWhichBindingSurvivesARebind()
    {
        // The sample configuration relies on this in the task view: <pagedown> is copied to
        // n/f/<Space> while it still means "scroll", and only afterwards rebound to move a task.
        // Both meanings must survive, attached to different keys.
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadLines([
            "tmap <pagedown> taskview_move down=1.0 pages=True",
            "copytmap <pagedown> n f <Space>",
            "tmap <pagedown> eval -q fm.ui.taskview.task_move(-1)",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal("taskview_move down=1.0 pages=True", CommandFor(maps.TaskView, "n"));
        Assert.Equal("task_move_down", CommandFor(maps.TaskView, "<pagedown>"));
    }

    [Fact]
    public void RecordsAnErrorWhenTheSourceOfACopyIsUnbound()
    {
        (ConfigurationReader reader, _) = Build();

        reader.ReadLines(["copymap zz yy"]);

        Assert.Single(reader.Errors);
    }

    [Theory]
    [InlineData("map")]
    [InlineData("map q")]
    [InlineData("copymap")]
    [InlineData("copymap onlyone")]
    public void RecordsAnErrorForAMalformedDirective(string line)
    {
        (ConfigurationReader reader, _) = Build();

        reader.ReadLines([line]);

        Assert.Single(reader.Errors);
    }

    /// <summary>
    /// The shipped configuration must load with every directive understood.
    /// </summary>
    [Fact]
    public void ShippedConfigLoadsEveryBinding()
    {
        (ConfigurationReader reader, KeyMaps maps) = Build();

        reader.ReadFile(TestPaths.ShippedConfig("cc.conf"));

        string[] unexpected = [.. reader.Errors.Where(NotYetImplemented).Select(e => e.ToString())];
        Assert.Empty(unexpected);

        // Spot-check bindings a user would notice immediately if they were missing.
        Assert.Equal("move up=1", CommandFor(maps.Browser, "k"));
        Assert.Equal("move down=1", CommandFor(maps.Browser, "j"));
        Assert.Equal("move to=0", CommandFor(maps.Browser, "gg"));
        Assert.Equal("move to=-1", CommandFor(maps.Browser, "G"));
        Assert.Equal("cut", CommandFor(maps.Browser, "dd"));
        Assert.Equal("copy", CommandFor(maps.Browser, "yy"));
        Assert.Equal("quit", CommandFor(maps.Browser, "q"));
        Assert.Equal("console", CommandFor(maps.Browser, ":"));

        Assert.True(maps.Browser.Enumerate().Count() > 200,
                    "the shipped configuration should define hundreds of bindings");
    }

    /// <summary>
    /// The real user configuration must load too, bindings and all.
    /// </summary>
    [Fact]
    public void SampleRangerConfigLoadsEveryBinding()
    {
        Assert.SkipWhen(!File.Exists(TestPaths.SampleRangerConfig),
                        "sample ranger configuration is not present");

        (ConfigurationReader reader, KeyMaps maps) = Build();

        IEnumerable<string> lines = File.ReadLines(TestPaths.SampleRangerConfig)
            .Select(line => line.Replace("nested_ranger_warning", "nested_canger_warning",
                                         StringComparison.Ordinal));

        reader.ReadLines(lines, "ranger-settings/rc.conf");

        string[] unexpected = [.. reader.Errors.Where(NotYetImplemented).Select(e => e.ToString())];
        Assert.Empty(unexpected);

        // The user's own customisations, not ranger's defaults.
        Assert.Equal("paste_ext", CommandFor(maps.Browser, "pp"));
        Assert.Equal("console trash", CommandFor(maps.Browser, "<DELETE>"));
        Assert.Equal("shell cp -rv --reflink=auto --preserve=timestamps %c %d",
                     CommandFor(maps.Browser, "pb"));
        Assert.Equal("fzf_select", CommandFor(maps.Browser, "fd"));
    }

    /// <summary>Whether an error is about a directive a later phase will register.</summary>
    private static bool NotYetImplemented(ConfigurationError error) =>
        !error.Message.StartsWith("unknown directive", StringComparison.Ordinal);
}
