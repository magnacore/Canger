// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Processes;
using Canger.Rifle;
using Canger.TestSupport;

namespace Canger.Rifle.Tests;

/// <summary>A runner that records what it was asked to run instead of running it.</summary>
internal sealed class RecordingRunner : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public ProcessResult Result { get; set; } = new(0);

    /// <inheritdoc />
    /// <remarks>Never raised: nothing here takes the terminal.</remarks>
    public event EventHandler? Suspending
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    /// <remarks>Never raised: nothing here takes the terminal.</remarks>
    public event EventHandler? Resumed
    {
        add { }
        remove { }
    }

    public ProcessResult Run(ProcessRequest request)
    {
        Requests.Add(request);
        return Result;
    }

    /// <inheritdoc />
    /// <remarks>Rifle never feeds a program anything; recorded so nothing goes unnoticed.</remarks>
    public ProcessResult RunWithInput(ProcessRequest request, string input)
    {
        Requests.Add(request);
        return Result;
    }

    /// <inheritdoc />
    /// <remarks>Rifle never runs anything in the background; recorded like the rest.</remarks>
    public IBackgroundProcess? StartInBackground(ProcessRequest request)
    {
        Requests.Add(request);
        return null;
    }

    /// <inheritdoc />
    /// <remarks>Nothing in rifle captures output; recorded so the contract is satisfied.</remarks>
    public ProcessResult RunCapturingOutput(ProcessRequest request)
    {
        Requests.Add(request);
        return Result;
    }
}

public class RifleTests
{
    private static RifleContext Context(IFileSystem fs, string path, string? mime = null,
                                        string? label = null, bool graphical = true,
                                        bool terminal = true) =>
        new(path, mime, label, graphical, terminal, fs);

    // ---- Parsing ---------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsConditionsAndCommand()
    {
        RifleConfiguration config = RifleConfiguration.FromLines(["ext pdf, has zathura = zathura \"$@\""]);

        RifleRule rule = Assert.Single(config.Rules);
        Assert.Equal(2, rule.Conditions.Count);
        Assert.Equal("ext", rule.Conditions[0].Name);
        Assert.Equal("pdf", rule.Conditions[0].Argument);
        Assert.Equal("zathura \"$@\"", rule.Command);
    }

    [Fact]
    public void Parse_SplitsOnTheFirstEqualsOnly()
    {
        // Commands routinely contain an equals sign, so only the first can be the separator.
        RifleConfiguration config = RifleConfiguration.FromLines(["ext sh = sh -c 'x=1; echo $x'"]);

        Assert.Equal("sh -c 'x=1; echo $x'", config.Rules[0].Command);
    }

    [Fact]
    public void Parse_SkipsBlankLinesAndComments()
    {
        RifleConfiguration config = RifleConfiguration.FromLines(
            ["", "   ", "# a comment", "ext txt = cat \"$@\""]);

        Assert.Single(config.Rules);
        Assert.Empty(config.Errors);
    }

    [Fact]
    public void Parse_RecordsALineWithNoSeparator()
    {
        RifleConfiguration config = RifleConfiguration.FromLines(["ext pdf zathura"]);

        Assert.Empty(config.Rules);
        Assert.Single(config.Errors);
    }

    [Fact]
    public void Parse_ReadsNegation()
    {
        RifleConfiguration config = RifleConfiguration.FromLines(["!mime ^text = echo x"]);

        Assert.True(config.Rules[0].Conditions[0].Negated);
        Assert.Equal("mime", config.Rules[0].Conditions[0].Name);
    }

    // ---- Conditions ------------------------------------------------------------------

    [Fact]
    public void Ext_MatchesTheExtension()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/report.pdf");

        Assert.True(RifleCondition.Parse("ext pdf")
            .Evaluate(Context(fs, "/x/report.pdf")).Matched);
        Assert.False(RifleCondition.Parse("ext txt")
            .Evaluate(Context(fs, "/x/report.pdf")).Matched);
    }

    [Fact]
    public void Ext_AcceptsAlternatives()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/photo.jpeg");

        Assert.True(RifleCondition.Parse("ext jpg|jpeg|png")
            .Evaluate(Context(fs, "/x/photo.jpeg")).Matched);
    }

    [Fact]
    public void Ext_IsFalseForADirectorySoNegatingItIsTrue()
    {
        // Real configurations rely on this to write rules that apply to directories only.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/x/folder");

        Assert.False(RifleCondition.Parse("ext pdf")
            .Evaluate(Context(fs, "/x/folder")).Matched);
        Assert.True(RifleCondition.Parse("!ext pdf")
            .Evaluate(Context(fs, "/x/folder")).Matched);
    }

    [Fact]
    public void Ext_IgnoresALeadingDot()
    {
        // ".bashrc" has no extension; treating "bashrc" as one would match the wrong rules.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/.bashrc");

        Assert.False(RifleCondition.Parse("ext bashrc")
            .Evaluate(Context(fs, "/x/.bashrc")).Matched);
    }

    [Fact]
    public void Mime_MatchesTheMediaType()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes");

        Assert.True(RifleCondition.Parse("mime ^text")
            .Evaluate(Context(fs, "/x/notes", mime: "text/plain")).Matched);
        Assert.False(RifleCondition.Parse("mime ^image")
            .Evaluate(Context(fs, "/x/notes", mime: "text/plain")).Matched);
    }

    [Fact]
    public void Mime_DoesNotMatchWhenTheTypeIsUnknown()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes");

        Assert.False(RifleCondition.Parse("mime ^text")
            .Evaluate(Context(fs, "/x/notes", mime: null)).Matched);
    }

    [Fact]
    public void Name_And_Path_MatchWhatTheyName()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/projects/Makefile");

        Assert.True(RifleCondition.Parse("name ^[mM]akefile$")
            .Evaluate(Context(fs, "/projects/Makefile")).Matched);
        Assert.True(RifleCondition.Parse("path ^/projects")
            .Evaluate(Context(fs, "/projects/Makefile")).Matched);
    }

    [Fact]
    public void FileAndDirectory_DistinguishTheTwo()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/x/thing.txt")
            .AddDirectory("/x/folder");

        Assert.True(RifleCondition.Parse("file").Evaluate(Context(fs, "/x/thing.txt")).Matched);
        Assert.False(RifleCondition.Parse("file").Evaluate(Context(fs, "/x/folder")).Matched);
        Assert.True(RifleCondition.Parse("directory").Evaluate(Context(fs, "/x/folder")).Matched);
    }

    [Fact]
    public void Else_AlwaysMatches()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/anything");

        Assert.True(RifleCondition.Parse("else").Evaluate(Context(fs, "/x/anything")).Matched);
    }

    [Fact]
    public void X_And_Terminal_ReportTheEnvironment()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/a");

        Assert.True(RifleCondition.Parse("X")
            .Evaluate(Context(fs, "/x/a", graphical: true)).Matched);
        Assert.False(RifleCondition.Parse("X")
            .Evaluate(Context(fs, "/x/a", graphical: false)).Matched);
        Assert.True(RifleCondition.Parse("terminal")
            .Evaluate(Context(fs, "/x/a", terminal: true)).Matched);
    }

    [Fact]
    public void Flag_Label_And_Number_AlwaysPassAndCarryTheirValue()
    {
        // These say how to run the rule rather than whether it applies.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/a");

        Assert.Equal("f", RifleCondition.Parse("flag f")
            .Evaluate(Context(fs, "/x/a")).Flags);
        Assert.Equal("editor", RifleCondition.Parse("label editor")
            .Evaluate(Context(fs, "/x/a")).Label);
        Assert.Equal(11, RifleCondition.Parse("number 11")
            .Evaluate(Context(fs, "/x/a")).Number);
    }

    [Fact]
    public void Label_MatchesOnlyTheRequestedOne()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/a");

        Assert.True(RifleCondition.Parse("label editor")
            .Evaluate(Context(fs, "/x/a", label: "editor")).Matched);
        Assert.False(RifleCondition.Parse("label pager")
            .Evaluate(Context(fs, "/x/a", label: "editor")).Matched);
    }

    [Fact]
    public void AnUnknownCondition_NeverMatches()
    {
        // A typo disables its rule rather than enabling it for everything.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/a");

        Assert.False(RifleCondition.Parse("nonsense foo")
            .Evaluate(Context(fs, "/x/a")).Matched);
    }

    [Fact]
    public void AMalformedPattern_DisablesItsRuleRatherThanFailing()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/a.txt");

        Assert.False(RifleCondition.Parse("name [unclosed")
            .Evaluate(Context(fs, "/x/a.txt")).Matched);
    }

    // ---- Flag squashing --------------------------------------------------------------

    [Theory]
    [InlineData("abcC", "ab")]
    [InlineData("CabcAd", "bd")]
    [InlineData("f", "f")]
    [InlineData("fF", "")]
    [InlineData("", "")]
    public void Squash_RemovesFlagsCancelledByAnUppercaseCounterpart(string input, string expected)
    {
        // An uppercase letter removes both itself and its lowercase form. The first two cases
        // are ranger's own documented examples.
        Assert.Equal(expected, ProcessFlags.Squash(input));
    }

    [Fact]
    public void Flags_ReadTheirMeaning()
    {
        ProcessFlags flags = new("fs");

        Assert.True(flags.Fork);
        Assert.True(flags.Silent);
        Assert.False(flags.AsRoot);
    }

    [Fact]
    public void Flags_LetALaterSetCancelAnEarlierOne()
    {
        // A general rule can set a flag and a specific one override it, neither knowing about
        // the other.
        Assert.False(new ProcessFlags("f").Add(new ProcessFlags("F")).Fork);
    }

    // ---- Command construction --------------------------------------------------------

    [Fact]
    public void BuildCommand_PutsTheFilesInThePositionalParameters()
    {
        // Rules write "$@" and never have to think about quoting.
        Assert.Equal("set -- '/x/a.txt'; cat \"$@\"",
                     RifleLauncher.BuildCommand(["/x/a.txt"], "cat \"$@\""));
    }

    [Fact]
    public void BuildCommand_HandlesSeveralFiles()
    {
        Assert.Equal("set -- '/x/a' '/x/b'; cat \"$@\"",
                     RifleLauncher.BuildCommand(["/x/a", "/x/b"], "cat \"$@\""));
    }

    [Fact]
    public void BuildCommand_ContainsNamesWithSpacesAndQuotes()
    {
        Assert.Equal("set -- '/x/My Documents'; ls \"$@\"",
                     RifleLauncher.BuildCommand(["/x/My Documents"], "ls \"$@\""));

        Assert.Equal(@"set -- '/x/it'\''s'; ls ""$@""",
                     RifleLauncher.BuildCommand(["/x/it's"], "ls \"$@\""));
    }

    [Fact]
    public void BuildCommand_NeutralisesAnInjectionAttempt()
    {
        // A file named "; rm -rf ~" must not become a second command.
        string command = RifleLauncher.BuildCommand(["/x/; rm -rf ~"], "cat \"$@\"");

        Assert.Equal("set -- '/x/; rm -rf ~'; cat \"$@\"", command);
    }

    [Fact]
    public void BuildCommand_DropsNamesContainingANulByte()
    {
        // Such a name cannot survive the shell at all, so passing it would corrupt the line.
        Assert.Equal("set -- '/x/fine'; cat \"$@\"",
                     RifleLauncher.BuildCommand(["/x/fine", "/x/ba\0d"], "cat \"$@\""));
    }

    // ---- Choosing and running --------------------------------------------------------

    private static (RifleLauncher Launcher, RecordingRunner Runner, InMemoryFileSystem Fs) Build(
        params string[] lines)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/x/report.pdf")
            .AddFile("/x/notes.txt")
            .AddDirectory("/x/folder");

        RecordingRunner runner = new();
        return (new RifleLauncher(RifleConfiguration.FromLines(lines), runner, fs), runner, fs);
    }

    [Fact]
    public void Open_RunsTheFirstRuleThatApplies()
    {
        (RifleLauncher launcher, RecordingRunner runner, _) = Build(
            "ext txt = cat \"$@\"",
            "ext pdf = viewer \"$@\"",
            "else = fallback \"$@\"");

        RifleResult result = launcher.Open(["/x/report.pdf"]);

        Assert.True(result.Succeeded);
        Assert.Equal("set -- '/x/report.pdf'; viewer \"$@\"", runner.Requests[0].Command);
    }

    [Fact]
    public void Open_CanChooseAnAlternativeByNumber()
    {
        // This is what :open_with 1 does — "the second thing that could open this".
        (RifleLauncher launcher, RecordingRunner runner, _) = Build(
            "ext pdf = first \"$@\"",
            "ext pdf = second \"$@\"",
            "ext pdf = third \"$@\"");

        launcher.Open(["/x/report.pdf"], number: 1);

        Assert.Contains("second", runner.Requests[0].Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_CanChooseAnAlternativeByLabel()
    {
        (RifleLauncher launcher, RecordingRunner runner, _) = Build(
            "ext txt = cat \"$@\"",
            "ext txt, label editor = vim \"$@\"");

        launcher.Open(["/x/notes.txt"], label: "editor");

        Assert.Contains("vim", runner.Requests[0].Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_NumbersOnlyTheRulesThatApply()
    {
        // Numbering counts alternatives, not lines, so an uninstalled program does not leave a
        // gap that shifts every later choice.
        (RifleLauncher launcher, RecordingRunner runner, _) = Build(
            "ext pdf, has definitely-not-installed-xyz = never \"$@\"",
            "ext pdf = first \"$@\"",
            "ext pdf = second \"$@\"");

        launcher.Open(["/x/report.pdf"], number: 0);
        Assert.Contains("first", runner.Requests[0].Command, StringComparison.Ordinal);

        launcher.Open(["/x/report.pdf"], number: 1);
        Assert.Contains("second", runner.Requests[1].Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_LetsARuleClaimAParticularNumber()
    {
        (RifleLauncher launcher, RecordingRunner runner, _) = Build(
            "ext pdf = first \"$@\"",
            "ext pdf, number 11 = pinned \"$@\"");

        launcher.Open(["/x/report.pdf"], number: 11);

        Assert.Contains("pinned", runner.Requests[0].Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_PassesTheRulesFlagsToTheRunner()
    {
        (RifleLauncher launcher, RecordingRunner runner, _) = Build(
            "ext pdf, flag f = viewer \"$@\"");

        launcher.Open(["/x/report.pdf"]);

        Assert.True(runner.Requests[0].Flags.Fork);
    }

    [Fact]
    public void Open_ReportsWhenNothingApplies()
    {
        (RifleLauncher launcher, _, _) = Build("ext txt = cat \"$@\"");

        RifleResult result = launcher.Open(["/x/report.pdf"]);

        Assert.Equal(RifleOutcome.NoRuleMatched, result.Outcome);
    }

    [Fact]
    public void Open_ReportsWhenTheChosenAlternativeDoesNotExist()
    {
        (RifleLauncher launcher, _, _) = Build("ext pdf = viewer \"$@\"");

        Assert.Equal(RifleOutcome.NoSuchAlternative,
                     launcher.Open(["/x/report.pdf"], number: 9).Outcome);
    }

    [Fact]
    public void Open_ReportsWhenTheConfigurationSaysToAsk()
    {
        (RifleLauncher launcher, RecordingRunner runner, _) = Build("ext pdf = ask");

        RifleResult result = launcher.Open(["/x/report.pdf"]);

        Assert.Equal(RifleOutcome.AskUser, result.Outcome);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Open_ReportsWhenTheProgramCouldNotStart()
    {
        (RifleLauncher launcher, RecordingRunner runner, _) = Build("ext pdf = viewer \"$@\"");
        runner.Result = new ProcessResult(error: "no such program");

        Assert.Equal(RifleOutcome.Failed, launcher.Open(["/x/report.pdf"]).Outcome);
    }

    [Fact]
    public void Alternatives_ListsEveryWayToOpenAFile()
    {
        (RifleLauncher launcher, _, _) = Build(
            "ext pdf = first \"$@\"",
            "ext pdf, label second = second \"$@\"",
            "ext txt = irrelevant \"$@\"");

        IReadOnlyList<NumberedMatch> alternatives = launcher.Alternatives("/x/report.pdf");

        Assert.Equal(2, alternatives.Count);
        Assert.Equal("second", alternatives[1].Match.Label);
    }

    /// <summary>
    /// The shipped configuration must parse cleanly.
    /// </summary>
    [Fact]
    public void ShippedConfigurationParses()
    {
        string path = Path.Join(RepositoryRoot(), "config", "rifle.conf");
        Assert.SkipWhen(!File.Exists(path), "the shipped rifle.conf is not present");

        RifleConfiguration config = RifleConfiguration.FromFile(path);

        Assert.Empty(config.Errors);
        Assert.True(config.Rules.Count > 100,
                    $"expected the shipped rules, found {config.Rules.Count}");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("could not find the repository root");
    }
}
