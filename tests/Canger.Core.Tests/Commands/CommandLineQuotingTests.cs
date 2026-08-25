// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Putting a filename into a line that will be macro-expanded again.
/// </summary>
/// <remarks>
/// <para>
/// Quoting for the shell is not enough on its own. A command that builds a line and hands it to
/// <c>Execute</c> gets the whole line expanded, including the part just quoted — so a per cent in
/// a filename is read as the start of a macro, and the substituted value brings its own quotes,
/// which close the quoting around the name and leave the rest of it bare.
/// </para>
/// <para>
/// Seven copies of a shell quoter had grown up across the codebase and the plugins, and they did
/// not agree about this. The pair is now named and provided once.
/// </para>
/// </remarks>
public class CommandLineQuotingTests
{
    /// <summary>An expander over a directory holding one awkwardly named file.</summary>
    private static MacroExpander Expander(string file)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile(file);
        return new MacroExpander(new FakeFileManager(fs));
    }

    [Fact]
    public void ShellQuote_NeutralisesEveryShellMetacharacter()
    {
        // The property everything else rests on.
        Assert.Equal("'; rm -rf ~'", MacroExpander.ShellQuote("; rm -rf ~"));
        Assert.Equal("'$(rm -rf ~)'", MacroExpander.ShellQuote("$(rm -rf ~)"));
        Assert.Equal("'a\nb'", MacroExpander.ShellQuote("a\nb"));
    }

    [Fact]
    public void ShellQuote_EscapesAQuoteWithoutBreakingOut()
    {
        Assert.Equal(@"'it'\''s'", MacroExpander.ShellQuote("it's"));
    }

    [Fact]
    public void ShellQuote_LeavesAPerCentAlone()
    {
        // Correct for its own job: this one is for a command going straight to a runner, where
        // nothing expands anything and a per cent is just a per cent.
        Assert.Equal("'My%20Docs'", MacroExpander.ShellQuote("My%20Docs"));
    }

    [Fact]
    public void QuoteForCommandLine_DoublesAPerCentSoItMeansItself()
    {
        Assert.Equal("'My%%20Docs'", MacroExpander.QuoteForCommandLine("My%20Docs"));
    }

    [Fact]
    public void QuoteForCommandLine_SurvivesTheExpansionItIsMeantFor()
    {
        // The whole point, end to end: quote a name, expand the line, and get the name back.
        MacroExpander expander = Expander("/home/x.txt");
        string line = "shell tool " + MacroExpander.QuoteForCommandLine("My%20Docs");

        Assert.Equal("shell tool 'My%20Docs'", expander.Expand(line));
    }

    [Fact]
    public void QuoteForCommandLine_StopsAFilenameSubstitutingAnotherFilesName()
    {
        // The exploit. `%s` inside the quoted name used to be expanded, and the substituted
        // value's own opening quote closed the quoting around the name — leaving whatever
        // followed unquoted, and a shell metacharacter in a *second* file able to run.
        MacroExpander expander = Expander("/home/;id;.txt");
        string line = "shell tool " + MacroExpander.QuoteForCommandLine("x%sy.txt");

        string expanded = expander.Expand(line);

        Assert.Equal("shell tool 'x%sy.txt'", expanded);
        Assert.DoesNotContain(";id;", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteForCommandLine_StillNeutralisesTheShell()
    {
        // It must not have traded one hazard for another.
        Assert.Equal("'; rm -rf ~'", MacroExpander.QuoteForCommandLine("; rm -rf ~"));
    }
}
