// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands.Builtin;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What <c>?</c> then <c>m</c> runs.
/// </summary>
/// <remarks>
/// It ran <c>man canger</c>, which asks the system for an <em>installed</em> page. Canger is
/// normally run from wherever it was unpacked or built, nothing installs a page there, and man
/// answers with exit 16 — its code for "no such page". The one key whose whole job is to explain
/// the program failed for anybody who had not packaged it.
/// </remarks>
public class HelpManualTests
{
    [Fact]
    public void TheManualComesFromTheRunningBinaryRatherThanAnInstalledPage()
    {
        string command = HelpCommand.ManualCommand("/opt/canger/canger");

        Assert.Contains("/opt/canger/canger", command, StringComparison.Ordinal);
        Assert.Contains("--man", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ItDoesNotAskTheSystemForAPageByName()
    {
        // `man canger` is the thing that failed. Anything that reaches for an installed page
        // fails the same way on an unpacked build, and shows a stale page on an installed one.
        string command = HelpCommand.ManualCommand("/opt/canger/canger");

        Assert.DoesNotContain("man canger", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageIsFormattedByManRatherThanShownRaw()
    {
        // Raw roff is unreadable. `man -l FILE` is understood by every implementation, where
        // `man -l -` reading standard input is man-db's own extension.
        Assert.Contains("man -l", HelpCommand.ManualCommand("/opt/canger/canger"),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageIsNamedSoTheHeaderReadsRight()
    {
        // man titles the page after the file it was given, so a bare temporary name would put
        // something like TMP.XYZ(1) across the top of the manual.
        Assert.Contains("canger.1", HelpCommand.ManualCommand("/opt/canger/canger"),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void TheTemporaryDirectoryIsRemovedWhateverHappens()
    {
        // After a semicolon rather than an `&&`, so quitting the pager or a failure part-way
        // still clears up.
        string command = HelpCommand.ManualCommand("/opt/canger/canger");

        Assert.Contains("; rm -rf", command, StringComparison.Ordinal);
    }

    [Fact]
    public void APathWithSpacesInItIsQuoted()
    {
        // Canger unpacked into "Program Files"-shaped directory, or simply a home directory with
        // a space in it, would otherwise run the wrong thing or nothing.
        string command = HelpCommand.ManualCommand("/home/some one/canger");

        Assert.Contains("'/home/some one/canger'", command, StringComparison.Ordinal);
    }
}
