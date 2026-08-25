// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Plugins;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// The <c>eval</c> command, as a key binding or at the prompt.
/// </summary>
public class EvalCommandTests
{
    /// <summary>A file manager with the eval command registered against a live host.</summary>
    private static FakeFileManager Manager()
    {
        FakeFileManager manager = new(new InMemoryFileSystem().AddFile("/home/a.txt"), "/home");
        EvalCommand.Host = new EvalHost(new ScriptCompiler());
        manager.Commands.Register<EvalCommand>();
        return manager;
    }

    [Fact]
    public void Eval_RunsTheSnippetAndSaysSo()
    {
        FakeFileManager manager = Manager();

        manager.Execute("""eval fm.Notify("from eval");""");

        Assert.Contains(manager.Messages, m => m.Message == "from eval");
        Assert.Contains(manager.Messages, m => m.Message == "eval: done");
    }

    [Fact]
    public void Eval_StaysQuietWithMinusQ()
    {
        // A binding that does its own notifying does not want a second message after it.
        FakeFileManager manager = Manager();

        manager.Execute("""eval -q fm.Notify("only this");""");

        Assert.Contains(manager.Messages, m => m.Message == "only this");
        Assert.DoesNotContain(manager.Messages, m => m.Message == "eval: done");
    }

    [Fact]
    public void Eval_PassesTheQuantifierThrough()
    {
        FakeFileManager manager = Manager();

        manager.Execute("""eval -q fm.Notify($"count {quantifier}");""", quantifier: 5);

        Assert.Contains(manager.Messages, m => m.Message == "count 5");
    }

    [Fact]
    public void Eval_ReportsACompilerErrorAtTheStatusBar()
    {
        FakeFileManager manager = Manager();

        manager.Execute("eval this is not C#;");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void Eval_SaysSoWhenGivenNothing()
    {
        FakeFileManager manager = Manager();

        manager.Execute("eval");

        Assert.Contains(manager.Messages, m => m.IsError);
    }
}
