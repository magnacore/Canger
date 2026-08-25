// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Plugins;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// The C# snippets an <c>eval</c> line carries.
/// </summary>
public class EvalHostTests
{
    private static EvalHost Host() => new(new ScriptCompiler());

    [Fact]
    public void Run_ExecutesAStatement()
    {
        EvalHost host = Host();
        List<string> lines = [];

        bool ran = host.Run("""cmd("hello");""", null, lines.Add);

        Assert.True(ran, string.Join("; ", host.Errors));
        Assert.Equal(["hello"], lines);
    }

    [Fact]
    public void Run_AcceptsASnippetWithoutATrailingSemicolon()
    {
        // Ranger's eval takes a bare expression, so Canger's should not demand punctuation.
        EvalHost host = Host();
        List<string> lines = [];

        bool ran = host.Run(EvalDirective.Statement("""cmd("hello")"""), null, lines.Add);

        Assert.True(ran, string.Join("; ", host.Errors));
        Assert.Equal(["hello"], lines);
    }

    [Fact]
    public void Run_CanGenerateAFamilyOfLines()
    {
        // This is the whole reason the directive exists: what a declarative line cannot say.
        EvalHost host = Host();
        List<string> lines = [];

        bool ran = host.Run(
            """foreach (string a in new[] { "r", "w" }) cmd($"map +u{a} chmod u+{a}");""",
            null, lines.Add);

        Assert.True(ran, string.Join("; ", host.Errors));
        Assert.Equal(["map +ur chmod u+r", "map +uw chmod u+w"], lines);
    }

    [Fact]
    public void Run_GivesTheSnippetTheFileManager()
    {
        EvalHost host = Host();
        FakeFileManager manager = new(new InMemoryFileSystem().AddDirectory("/home"), "/home");

        bool ran = host.Run("""fm.Notify("from the snippet");""", manager, _ => { });

        Assert.True(ran, string.Join("; ", host.Errors));
        Assert.Contains(manager.Messages, m => m.Message == "from the snippet");
    }

    [Fact]
    public void Run_GivesTheSnippetTheQuantifier()
    {
        EvalHost host = Host();
        FakeFileManager manager = new(new InMemoryFileSystem().AddDirectory("/home"), "/home");

        bool ran = host.Run("""fm.Notify($"count {quantifier}");""", manager, _ => { }, 7);

        Assert.True(ran, string.Join("; ", host.Errors));
        Assert.Contains(manager.Messages, m => m.Message == "count 7");
    }

    [Fact]
    public void Run_ReportsACompilerErrorRatherThanThrowing()
    {
        EvalHost host = Host();

        Assert.False(host.Run("this is not C#;", null, _ => { }));
        Assert.NotEmpty(host.Errors);
    }

    [Fact]
    public void Run_ReportsWhatTheSnippetThrewRatherThanTheReflectionWrapper()
    {
        // The wrapper says only "an exception was thrown", which helps nobody.
        EvalHost host = Host();

        Assert.False(host.Run(
            """throw new System.InvalidOperationException("deliberate");""", null, _ => { }));

        Assert.Contains(host.Errors, e => e.Contains("deliberate", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_ReusesTheCompiledSnippetWhenItIsRunAgain()
    {
        // A snippet bound to a key is run over and over; recompiling each time would be absurd.
        EvalHost host = Host();
        List<string> lines = [];

        for (int i = 0; i < 3; i++)
        {
            Assert.True(host.Run("""cmd("again");""", null, lines.Add),
                        string.Join("; ", host.Errors));
        }

        Assert.Equal(3, lines.Count);
    }

    [Theory]
    [InlineData("cmd(\"x\")", "cmd(\"x\");")]
    [InlineData("cmd(\"x\");", "cmd(\"x\");")]
    [InlineData("{ cmd(\"x\"); }", "{ cmd(\"x\"); }")]
    public void Statement_AddsPunctuationOnlyWhereItIsMissing(string input, string expected)
    {
        Assert.Equal(expected, EvalDirective.Statement(input));
    }

    [Fact]
    public void Run_SaysPlainlyThatPythonIsNotTheLanguageAnyMore()
    {
        // A configuration carried over from ranger contains Python eval lines. Thirteen C#
        // syntax errors is a hostile way to explain that the language changed.
        EvalHost host = Host();

        Assert.False(host.Run(
            """for arg in "rwxXst": cmd("map +u{0} chmod u+{0}".format(arg));""",
            null, _ => { }));

        string error = Assert.Single(host.Errors);
        Assert.Contains("C#", error, StringComparison.Ordinal);
        Assert.DoesNotContain("CS1003", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ReportsOneLineForACSharpMistakeRatherThanTheWholeCascade()
    {
        // The first error is the one that matters; the rest are its consequences.
        EvalHost host = Host();

        Assert.False(host.Run("cmd(", null, _ => { }));

        string error = Assert.Single(host.Errors);
        Assert.DoesNotContain(Path.GetTempPath(), error, StringComparison.Ordinal);
    }
}
