// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.Input;

namespace Canger.Core.Tests.Input;

/// <summary>
/// Finding bindings that name a command which is not there.
/// </summary>
/// <remarks>
/// The case this exists for is a plugin being removed: every key pointing at one of its commands
/// goes quiet, and until now the first anyone knew of it was pressing one and being told "unknown
/// command" with nothing to connect that to the file they deleted.
/// </remarks>
public class BindingCheckTests
{
    private static (KeyMaps Maps, CommandRegistry Commands) Build(params string[] bindings)
    {
        CommandRegistry commands = new();
        CommandDispatcher.RegisterBuiltins(commands);

        KeyMaps maps = new();

        foreach (string binding in bindings)
        {
            int split = binding.IndexOf(' ', StringComparison.Ordinal);

            maps.Browser.Bind(binding[..split], binding[(split + 1)..]);
        }

        return (maps, commands);
    }

    [Fact]
    public void ABindingNamingANonexistentCommandIsFound()
    {
        (KeyMaps maps, CommandRegistry commands) = Build("ead extract_to_dirs");

        DeadBinding dead = Assert.Single(BindingCheck.Find(maps, commands));

        Assert.Equal("extract_to_dirs", dead.Line);
        Assert.Contains("ead", dead.Keys, StringComparison.Ordinal);
    }

    [Fact]
    public void ABindingNamingARealCommandIsNot()
    {
        (KeyMaps maps, CommandRegistry commands) = Build("gg move to=0");

        Assert.Empty(BindingCheck.Find(maps, commands));
    }

    [Fact]
    public void ATrailingCommentDoesNotMakeAGoodBindingLookBroken()
    {
        // A `map` line may separate the command from its comment with tabs, and taking "cmd\t\t#"
        // as the name once reported a great many perfectly good bindings as dead.
        (KeyMaps maps, CommandRegistry commands) = Build("gg move to=0\t\t# to the top");

        Assert.Empty(BindingCheck.Find(maps, commands));
    }

    [Fact]
    public void AnAbbreviationThatResolvesIsNotReported()
    {
        // Commands may be typed by an unambiguous prefix, and a binding may use one.
        (KeyMaps maps, CommandRegistry commands) = Build("x mov to=0");

        Assert.Empty(BindingCheck.Find(maps, commands));
    }

    [Fact]
    public void OnlyTheCommandNameIsJudged()
    {
        // Whether the arguments make sense is the command's own business, and many are meaningful
        // only at the moment they run.
        (KeyMaps maps, CommandRegistry commands) = Build("x move nonsense=yes possibly");

        Assert.Empty(BindingCheck.Find(maps, commands));
    }

    [Fact]
    public void EveryDeadBindingIsListed()
    {
        (KeyMaps maps, CommandRegistry commands) =
            Build("pam mka_mode", "pap mka_pause", "gg move to=0");

        Assert.Equal(2, BindingCheck.Find(maps, commands).Count);
    }
}
