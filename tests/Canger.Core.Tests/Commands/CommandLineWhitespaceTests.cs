// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Any whitespace separates a command line's words, not just a space.
/// </summary>
/// <remarks>
/// <para>
/// A real configuration lines its comments up with tabs:
/// <c>map edn directories_number_highlight&lt;TAB&gt;&lt;TAB&gt;# Number Highlighted Directory</c>.
/// Ranger keeps the trailing comment in the command — <c>source</c> skips only lines that
/// <em>start</em> with <c>#</c> (<c>core/actions.py:378-381</c>) — and splits with
/// <c>str.split()</c>, which treats a tab as a separator, so the name comes out clean.
/// </para>
/// <para>
/// Canger split on <c>' '</c> alone, so the tabs and the comment stayed inside the first word and
/// the command name became <c>directories_number_highlight\t\t\t#</c>. Thirty-six bindings in one
/// configuration were dead that way, silently — the ones spelled <c>shell …</c> appeared to work
/// only because <c>sh</c> ignores the comment itself.
/// </para>
/// </remarks>
public class CommandLineWhitespaceTests
{
    [Fact]
    public void ATabSeparatesTheNameFromWhatFollows()
    {
        CommandLine line = new("directories_number_highlight\t\t\t# Number Highlighted Directory");

        Assert.Equal("directories_number_highlight", line.Name);
    }

    [Fact]
    public void ATabSeparatesArgumentsToo()
    {
        CommandLine line = new("shell\tbpytop");

        Assert.Equal("shell", line.Name);
        Assert.Equal("bpytop", line.Word(1));
    }

    [Fact]
    public void RestSkipsATabSeparator()
    {
        // `shell` takes everything after its name, and used to get the tabs with it.
        CommandLine line = new("shell\t\t\tbpytop");

        Assert.Equal("bpytop", line.Rest(1));
    }

    [Fact]
    public void RestKeepsTheSpacingInsideWhatItReturns()
    {
        // The reason Rest exists: a directory name may contain spaces, and `:shell echo  a   b`
        // should pass its spacing through.
        Assert.Equal("My  Documents", new CommandLine("mkdir My  Documents").Rest(1));
    }

    [Fact]
    public void ACommentAfterACommandIsLeftOnTheLine()
    {
        // Kept, not stripped, as ranger keeps it: for a `shell` binding it reaches sh, which
        // ignores it, and elsewhere it is documentation the hint window shows.
        CommandLine line = new("shell\tbpytop\t\t\t# System Processes");

        Assert.Equal("shell", line.Name);
        Assert.Equal("bpytop\t\t\t# System Processes", line.Rest(1));
    }

    [Fact]
    public void FlagsAreStillPulledOffAcrossTabs()
    {
        CommandLine line = new("shell\t-w\tmedia-length\t\t\t# Media Length");

        (string flags, string arguments) = line.ParseFlags();

        Assert.Equal("w", flags);
        Assert.Equal("media-length\t\t\t# Media Length", arguments);
    }

    [Fact]
    public void AnOrdinarySpacedLineIsUnaffected()
    {
        CommandLine line = new("set sort mtime");

        Assert.Equal("set", line.Name);
        Assert.Equal("sort mtime", line.Rest(1));
        Assert.Equal(3, line.Count);
    }
}
