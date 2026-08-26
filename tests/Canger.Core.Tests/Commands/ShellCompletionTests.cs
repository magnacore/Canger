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
    private static IReadOnlyList<string> Complete(string line, params string[] files)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home");
        foreach (string name in files)
        {
            fs.AddFile("/home/" + name);
        }

        FakeFileManager manager = new(fs, "/home");
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
    public void OffersNoProgramsOnceTheProgramIsNamed()
    {
        // Past the first word the user is writing a command line, and every binary on the
        // machine would be noise. Filenames are offered instead — see below.
        Assert.Empty(Complete("shell echo hello"));
    }

    [Fact]
    public void CompletesAFilenameBeingTyped()
    {
        // The reported defect: `:shell some-program FIL<Tab>` did nothing, so a command line was
        // the one place in Canger where a filename had to be typed out in full.
        IReadOnlyList<string> candidates = Complete("shell some-program FIL", "FILE.txt", "other");

        Assert.Equal(["shell some-program FILE.txt"], candidates);
    }

    [Fact]
    public void KeepsEverythingBeforeTheWordBeingCompleted()
    {
        // Only the last word is replaced; the program and the earlier arguments stay put.
        Assert.Equal(["shell prog --flag one two.txt"],
                     Complete("shell prog --flag one tw", "two.txt"));
    }

    [Fact]
    public void MatchesRegardlessOfCase()
    {
        Assert.Equal(["shell prog Report.pdf"], Complete("shell prog rep", "Report.pdf"));
    }

    [Fact]
    public void QuotesANameThatTheShellWouldOtherwiseSplit()
    {
        Assert.Equal(["shell prog 'two words.txt'"],
                     Complete("shell prog two", "two words.txt"));
    }

    [Fact]
    public void DoublesAPerCentSoItSurvivesTheSecondExpansion()
    {
        // The completed line is expanded again when it runs, so a per cent left alone would be
        // read as the start of a macro — the trap `QuoteForCommandLine` exists for.
        Assert.Equal(["shell prog 'x%%sy.txt'"], Complete("shell prog x", "x%sy.txt"));
    }

    [Fact]
    public void LeavesAnOrdinaryNameUnquotedSoItCanBeTypedOver()
    {
        // A name that came back wrapped in quotes would no longer match itself if the user kept
        // typing, and cycling would stop finding it.
        Assert.Equal(["shell prog notes.txt"], Complete("shell prog not", "notes.txt"));
    }

    [Fact]
    public void OffersTheSelectionAfterATrailingSpace()
    {
        // An empty argument waiting to be filled, and what the user almost always wants there is
        // what they have marked. Ranger does the same (`config/commands.py:333-337`).
        IReadOnlyList<string> candidates = Complete("shell prog ", "alpha.txt");

        Assert.Equal(["shell prog alpha.txt "], candidates);
    }

    [Fact]
    public void OffersNoFilenamesThatDoNotMatch()
    {
        Assert.Empty(Complete("shell prog zzz", "alpha.txt", "beta.txt"));
    }

    [Fact]
    public void OffersNothingForAPrefixThatMatchesNoProgram()
    {
        Assert.Empty(Complete("shell zzzzznosuchprogram"));
    }
}
