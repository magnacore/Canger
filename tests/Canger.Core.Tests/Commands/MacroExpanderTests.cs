// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

public class MacroExpanderTests
{
    private static (MacroExpander Expander, FakeFileManager Manager) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.txt")
            .AddFile("/other/elsewhere.txt");

        FakeFileManager manager = new(fs);
        return (new MacroExpander(manager), manager);
    }

    [Fact]
    public void Expand_LeavesALineWithNoMacrosAlone()
    {
        (MacroExpander expander, _) = Build();

        Assert.Equal("echo hello", expander.Expand("echo hello"));
    }

    [Fact]
    public void Expand_ResolvesTheFileUnderTheCursor()
    {
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.CurrentTab.MoveCursor(0);

        Assert.Equal("edit alpha.txt", expander.Expand("edit %f"));
    }

    [Fact]
    public void Expand_ResolvesTheCurrentDirectory()
    {
        (MacroExpander expander, _) = Build();

        Assert.Equal("cd /home", expander.Expand("cd %d"));
    }

    [Fact]
    public void Expand_ResolvesTheSelectionAsNamesOrPaths()
    {
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.CurrentTab.MoveCursor(1);

        Assert.Equal("rm beta.txt", expander.Expand("rm %s"));
        Assert.Equal("rm /home/beta.txt", expander.Expand("rm %p"));
    }

    [Fact]
    public void Expand_ResolvesSeveralMarkedFilesAtOnce()
    {
        // This is what makes one binding act on a whole selection.
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.CurrentTab.Current.Entries[0].IsMarked = true;
        manager.CurrentTab.Current.Entries[2].IsMarked = true;

        Assert.Equal("rm alpha.txt gamma.txt", expander.Expand("rm %s"));
    }

    [Fact]
    public void Expand_ResolvesTheCopyBuffer()
    {
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.SetCopyBuffer([manager.CurrentTab.Current.Entries[0]], cut: false);

        Assert.Equal("cp /home/alpha.txt .", expander.Expand("cp %c ."));
    }

    [Fact]
    public void Expand_ResolvesAParticularTab()
    {
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.AddTab(2, "/other");

        Assert.Equal("cp x /other", expander.Expand("cp x %2d"));
    }

    [Fact]
    public void Expand_ResolvesTheNextTab()
    {
        // %D is what makes "copy this to the other pane" a single binding.
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.AddTab(2, "/other");

        Assert.Equal("cp x /other", expander.Expand("cp x %D"));
    }

    [Fact]
    public void Expand_TreatsDoublePercentAsALiteral()
    {
        (MacroExpander expander, _) = Build();

        Assert.Equal("echo 50% done", expander.Expand("echo 50%% done"));
    }

    [Fact]
    public void Expand_ResolvesSpaceSoConfigurationCanContainOne()
    {
        // Configuration lines are split on whitespace, so a trailing space in a pre-filled
        // command has to be written as a macro to survive being read.
        (MacroExpander expander, _) = Build();

        Assert.Equal("console shell ", expander.Expand("console shell%space"));
    }

    [Fact]
    public void Expand_ResolvesAWildcardCapturedByTheBinding()
    {
        (MacroExpander expander, _) = Build();

        Assert.Equal("tag_toggle tag=x", expander.Expand("tag_toggle tag=%any", ['x']));
    }

    [Fact]
    public void Expand_ResolvesNumberedWildcards()
    {
        (MacroExpander expander, _) = Build();

        Assert.Equal("do a b", expander.Expand("do %any0 %any1", ['a', 'b']));
    }

    [Fact]
    public void Expand_RefusesToRunWhenAMacroHasNoValue()
    {
        // Expanding to nothing would turn "rm -rf %c" into "rm -rf", which is exactly the
        // accident this prevents.
        (MacroExpander expander, _) = Build();

        MacroException error = Assert.Throws<MacroException>(() => expander.Expand("rm -rf %c"));
        Assert.Contains("%c", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_RefusesToResolveATabThatIsNotOpen()
    {
        (MacroExpander expander, _) = Build();

        Assert.Throws<MacroException>(() => expander.Expand("cp x %5d"));
    }

    [Fact]
    public void Expand_LeavesAnUnknownMacroAlone()
    {
        (MacroExpander expander, _) = Build();

        Assert.Equal("echo 100%", expander.Expand("echo 100%"));
    }

    // ---- Shell quoting ---------------------------------------------------------------

    [Fact]
    public void Expand_QuotesValuesForTheShellWhenAsked()
    {
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.CurrentTab.MoveCursor(0);

        Assert.Equal("cat 'alpha.txt'", expander.Expand("cat %f", escapeForShell: true));
    }

    [Fact]
    public void Expand_QuotesEachSelectedFileSeparately()
    {
        // Joining first and quoting after would turn several files into one argument.
        (MacroExpander expander, FakeFileManager manager) = Build();
        manager.CurrentTab.Current.Entries[0].IsMarked = true;
        manager.CurrentTab.Current.Entries[1].IsMarked = true;

        Assert.Equal("rm 'alpha.txt' 'beta.txt'",
                     expander.Expand("rm %s", escapeForShell: true));
    }

    [Fact]
    public void ShellQuote_ContainsNamesWithSpaces() =>
        Assert.Equal("'My Documents'", MacroExpander.ShellQuote("My Documents"));

    [Fact]
    public void ShellQuote_NeutralisesShellMetacharacters()
    {
        // A file named "; rm -rf ~" must not become a second command.
        string quoted = MacroExpander.ShellQuote("; rm -rf ~");

        Assert.Equal("'; rm -rf ~'", quoted);
        Assert.StartsWith("'", quoted, StringComparison.Ordinal);
        Assert.EndsWith("'", quoted, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellQuote_HandlesAQuoteInTheNameItself()
    {
        // The only character single quoting cannot contain is a single quote, so the quoting is
        // closed, an escaped quote emitted, and the quoting reopened.
        Assert.Equal(@"'it'\''s'", MacroExpander.ShellQuote("it's"));
    }

    [Fact]
    public void Expand_QuotesAFileNamedLikeAShellInjection()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/; rm -rf ~");
        FakeFileManager manager = new(fs);
        MacroExpander expander = new(manager);
        manager.CurrentTab.MoveCursor(0);

        string result = expander.Expand("cat %f", escapeForShell: true);

        Assert.Equal("cat '; rm -rf ~'", result);
    }
}
