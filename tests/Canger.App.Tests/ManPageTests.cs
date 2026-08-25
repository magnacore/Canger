// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.App;
using Canger.Core.Commands;
using Canger.Core.Input;
using Canger.Core.Settings;

namespace Canger.App.Tests;

/// <summary>
/// The manual page, whose reference sections are generated from the same metadata the program
/// uses so that they cannot drift out of step with it.
/// </summary>
public class ManPageTests
{
    private static string Render(Action<KeyMaps>? bind = null)
    {
        KeyMaps keyMaps = new();
        bind?.Invoke(keyMaps);

        CommandRegistry commands = new();
        CommandDispatcher.RegisterBuiltins(commands);

        return ManPage.Render("1.2.3", keyMaps, commands);
    }

    [Fact]
    public void Render_StartsWithATitleHeaderCarryingTheVersion()
    {
        Assert.StartsWith(".TH CANGER 1", Render(), StringComparison.Ordinal);
        Assert.Contains("canger 1.2.3", Render(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".SH NAME")]
    [InlineData(".SH SYNOPSIS")]
    [InlineData(".SH DESCRIPTION")]
    [InlineData(".SH OPTIONS")]
    [InlineData(".SH KEY BINDINGS")]
    [InlineData(".SH SETTINGS")]
    [InlineData(".SH COMMANDS")]
    [InlineData(".SH FILES")]
    [InlineData(".SH ENVIRONMENT")]
    public void Render_IncludesEverySection(string heading)
    {
        Assert.Contains(heading, Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DocumentsEverySetting()
    {
        // The point of generating this is that a new setting cannot be forgotten.
        string page = Render();

        foreach (string name in SettingsCatalog.All.Keys)
        {
            Assert.Contains($".B {name}\n", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Render_DocumentsEveryCommand()
    {
        CommandRegistry commands = new();
        CommandDispatcher.RegisterBuiltins(commands);

        string page = ManPage.Render("1.0.0", new KeyMaps(), commands);

        foreach (CommandDescriptor command in commands.All())
        {
            Assert.Contains($".B :{command.Name}\n", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Render_ShowsASettingsTypeDefaultAndPermittedValues()
    {
        string page = Render();

        Assert.Contains("One of: miller, multipane.", page, StringComparison.Ordinal);
        Assert.Contains("Default: miller.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SpellsAnUnsetDefaultRatherThanLeavingItBlank()
    {
        // draw_borders_multipane is deliberately unset, which is different from "none".
        Assert.Contains("Default: unset.", Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesTheBindingsItIsGiven()
    {
        string page = Render(maps => maps[KeyContext.Browser].Bind([(int)'Z'], "quit"));

        Assert.Contains(".B Z\n", page, StringComparison.Ordinal);
        Assert.Contains("quit", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EscapesABindingThatWouldOtherwiseBeReadAsAMacro()
    {
        // A line starting with a full stop is a macro, not text. Without escaping, a binding
        // for "." would swallow the rest of the page.
        string page = Render(maps => maps[KeyContext.Browser].Bind([(int)'.'], "reload_cwd"));

        Assert.Contains(".B \\&.\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain(".B .\n", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EscapesABackslashInABinding()
    {
        string page = Render(maps => maps[KeyContext.Browser].Bind([(int)'\\'], "reload_cwd"));

        Assert.Contains("\\e", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DocumentsEveryCommandLineOption()
    {
        // The manual and the --help text come from one source, so neither can be updated alone.
        string page = Render();

        foreach (string option in (string[])
                 ["--clean", "--copy-config", "--choosefile", "--choosedir", "--cmd", "--man"])
        {
            Assert.Contains(option, page, StringComparison.Ordinal);
        }
    }
}
