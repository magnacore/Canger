// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// <c>:shell</c> completing the program name against the PATH.
/// </summary>
/// <remarks>
/// Ranger's <c>shell</c> completes against <c>get_executables()</c>; Canger's offered nothing at
/// all, so <c>s</c> — bound to <c>console shell%space</c> — left the user typing the whole
/// program name. Against the real PATH, because that is what is being completed.
/// </remarks>
public class ShellCompletionTests
{
    private static IReadOnlyList<string> Complete(string line)
    {
        FakeFileManager manager = new(new InMemoryFileSystem().AddDirectory("/home"), "/home");
        return manager.Dispatcher.Build(line)?.Complete(1) ?? [];
    }

    [Fact]
    public void OffersProgramsOnThePath()
    {
        // `ls` exists everywhere this runs.
        Assert.Contains("shell ls", Complete("shell l"), StringComparer.Ordinal);
    }

    [Fact]
    public void KeepsTheFlagsThatWereTyped()
    {
        // `:shell -w gz<Tab>` must not lose the -w on the way.
        Assert.All(Complete("shell -w l"),
                   c => Assert.StartsWith("shell -w ", c, StringComparison.Ordinal));
    }

    [Fact]
    public void OffersNothingOnceTheProgramIsNamed()
    {
        // Past the first word the user is writing a command line, and every binary on the
        // machine would be noise.
        Assert.Empty(Complete("shell echo hello"));
    }

    [Fact]
    public void OffersNothingForAPrefixThatMatchesNoProgram()
    {
        Assert.Empty(Complete("shell zzzzznosuchprogram"));
    }
}
