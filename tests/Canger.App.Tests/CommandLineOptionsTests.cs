// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.App;

namespace Canger.App.Tests;

/// <summary>
/// Reading the command line. The names follow ranger's, so an existing shell function or desktop
/// entry works against Canger unchanged.
/// </summary>
public class CommandLineOptionsTests
{
    private static CommandLineOptions Parse(params string[] args) =>
        CommandLineOptions.Parse(args);

    [Fact]
    public void Parse_TreatsBareArgumentsAsPaths()
    {
        CommandLineOptions options = Parse("/home", "/tmp");

        Assert.Equal(["/home", "/tmp"], options.Paths);
        Assert.Empty(options.Unknown);
    }

    [Theory]
    [InlineData("--choosefile=/out")]
    [InlineData("--choosefile", "/out")]
    public void Parse_AcceptsBothSpellingsOfAValue(params string[] args)
    {
        // Ranger's own documentation uses each in places, so both have to work.
        Assert.Equal("/out", Parse(args).ChooseFile);
    }

    [Fact]
    public void Parse_CollectsRepeatedCommandsInOrder()
    {
        // The order matters: a later --cmd is meant to override an earlier one.
        CommandLineOptions options = Parse("--cmd=set sort size", "--cmd", "set sort_reverse true");

        Assert.Equal(["set sort size", "set sort_reverse true"], options.Commands);
    }

    [Theory]
    [InlineData("-c")]
    [InlineData("--clean")]
    public void Parse_AcceptsShortAndLongFormsOfClean(string argument)
    {
        Assert.True(Parse(argument).Clean);
    }

    [Fact]
    public void Parse_ReadsTheDirectoryOverrides()
    {
        CommandLineOptions options = Parse("-r", "/conf", "--datadir=/data", "--cachedir", "/cache");

        Assert.Equal("/conf", options.ConfigDirectory);
        Assert.Equal("/data", options.DataDirectory);
        Assert.Equal("/cache", options.CacheDirectory);
    }

    [Fact]
    public void Parse_DefaultsCopyConfigToEverything()
    {
        Assert.Equal("all", Parse("--copy-config").CopyConfig);
    }

    [Fact]
    public void Parse_DefaultsTaggedFileListingToTheDefaultTag()
    {
        Assert.Equal("*", Parse("--list-tagged-files").ListTaggedFiles);
    }

    [Fact]
    public void Parse_ReportsAnUnrecognisedOptionRatherThanIgnoringIt()
    {
        // A mistyped flag that silently does nothing is worse than one that says so.
        CommandLineOptions options = Parse("--chosefile=/out");

        Assert.Equal(["--chosefile=/out"], options.Unknown);
        Assert.Null(options.ChooseFile);
    }

    [Fact]
    public void Parse_TreatsALoneDashAsAPathRatherThanAnOption()
    {
        Assert.Equal(["-"], Parse("-").Paths);
        Assert.Empty(Parse("-").Unknown);
    }

    [Fact]
    public void Parse_KnowsWhenItIsActingAsAChooser()
    {
        Assert.True(Parse("--choosefile=/out").IsChooser);
        Assert.True(Parse("--choosefiles=/out").IsChooser);

        // A directory chooser is different: it reports on the way out rather than replacing what
        // opening a file does.
        Assert.False(Parse("--choosedir=/out").IsChooser);
        Assert.False(Parse("/home").IsChooser);
    }

    [Fact]
    public void Parse_HandlesAnOptionWithAMissingValueWithoutThrowing()
    {
        // "canger --choosefile" with nothing after it should not be a crash.
        CommandLineOptions options = Parse("--choosefile");

        Assert.Null(options.ChooseFile);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void Parse_KeepsPathsAndOptionsSeparateWhateverTheOrder()
    {
        CommandLineOptions options = Parse("/home", "--clean", "/tmp", "--cmd=echo hi");

        Assert.Equal(["/home", "/tmp"], options.Paths);
        Assert.True(options.Clean);
        Assert.Equal(["echo hi"], options.Commands);
    }

    [Fact]
    public void Usage_MentionsEveryOptionItAccepts()
    {
        // The help is the only place a user finds these, so it has to stay in step.
        string usage = CommandLineOptions.Usage;

        foreach (string option in (string[])
                 [
                     "--clean", "--confdir", "--datadir", "--cachedir", "--copy-config", "--cmd",
                     "--selectfile", "--show-only-dirs", "--choosefile", "--choosefiles",
                     "--choosedir", "--config", "--list", "--key-probe", "--list-tagged-files",
                 ])
        {
            Assert.Contains(option, usage, StringComparison.Ordinal);
        }
    }
}
