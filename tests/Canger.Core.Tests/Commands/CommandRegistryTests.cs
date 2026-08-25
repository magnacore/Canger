// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

public class CommandRegistryTests
{
    [Command("alpha", Summary = "First.")]
    private sealed class AlphaCommand : CangerCommand
    {
        public override void Execute()
        {
        }
    }

    [Command("alphabet", Summary = "Second.")]
    private sealed class AlphabetCommand : CangerCommand
    {
        public override void Execute()
        {
        }
    }

    [Command("beta", AllowAbbreviation = false, Summary = "Third.")]
    private sealed class BetaCommand : CangerCommand
    {
        public override void Execute()
        {
        }
    }

    private static CommandRegistry Registry()
    {
        CommandRegistry registry = new();
        registry.Register<AlphaCommand>();
        registry.Register<AlphabetCommand>();
        registry.Register<BetaCommand>();
        return registry;
    }

    [Fact]
    public void Find_ResolvesAnExactName() =>
        Assert.Equal("alpha", Registry().Find("alpha")?.Name);

    [Fact]
    public void Find_ResolvesAnUnambiguousAbbreviation() =>
        Assert.Equal("alphabet", Registry().Find("alphab")?.Name);

    [Fact]
    public void Find_PrefersAnExactMatchOverALongerCommand()
    {
        // "alpha" is a prefix of "alphabet", so without this rule the shorter command would
        // become unreachable the moment a longer one was added.
        Assert.Equal("alpha", Registry().Find("alpha")?.Name);
    }

    [Fact]
    public void Find_ReportsAnAmbiguousAbbreviation()
    {
        CommandException error = Assert.Throws<CommandException>(() => Registry().Find("alph"));

        Assert.Contains("alpha", error.Message, StringComparison.Ordinal);
        Assert.Contains("alphabet", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_IgnoresCommandsThatOptOutOfAbbreviation()
    {
        // Commands where a near-miss would be expensive can require their full name.
        Assert.Null(Registry().Find("bet"));
        Assert.Equal("beta", Registry().Find("beta")?.Name);
    }

    [Fact]
    public void Find_ReturnsNothingForAnUnknownName() => Assert.Null(Registry().Find("zzz"));

    [Fact]
    public void Register_RejectsATypeWithoutTheAttribute() =>
        Assert.Throws<ArgumentException>(() => Registry().Register(typeof(string)));

    [Fact]
    public void RegisterAll_FindsEveryBuiltInCommand()
    {
        CommandRegistry registry = new();

        int count = CommandDispatcher.RegisterBuiltins(registry);

        Assert.True(count > 10, $"expected the built-in commands, found {count}");
        Assert.NotNull(registry.Find("quit"));
        Assert.NotNull(registry.Find("move"));
        Assert.NotNull(registry.Find("set"));
    }

    [Fact]
    public void Alias_MakesOneNameStandForACommandLine()
    {
        // This is how the shipped configuration turns one scout command into filter, find,
        // mark and search.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        FakeFileManager manager = new(fs);

        manager.Commands.Alias("bye", "quit");
        manager.Execute("bye");

        Assert.True(manager.HasQuit);
    }

    [Fact]
    public void Alias_PassesItsOwnArgumentsThrough()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/a.txt");
        FakeFileManager manager = new(fs);

        manager.Commands.Alias("say", "echo hello");
        manager.Execute("say world");

        Assert.Equal("hello world", manager.LastMessage);
    }

    [Fact]
    public void Alias_RejectsAnUnknownTarget() =>
        Assert.Throws<CommandException>(() => Registry().Alias("x", "no_such_command"));

    [Fact]
    public void Matching_ListsNamesForCompletion()
    {
        Assert.Equal(["alpha", "alphabet"], Registry().Matching("alph"));
        Assert.Empty(Registry().Matching("zzz"));
    }
}
