// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;

namespace Canger.Core.Tests.Commands;

public class CommandLineTests
{
    [Fact]
    public void Name_IsTheFirstWord() => Assert.Equal("mkdir", new CommandLine("mkdir foo").Name);

    [Fact]
    public void Word_ReadsIndividualWords()
    {
        CommandLine line = new("set sort size");

        Assert.Equal("set", line.Word(0));
        Assert.Equal("sort", line.Word(1));
        Assert.Equal("size", line.Word(2));
        Assert.Equal(string.Empty, line.Word(9));
    }

    [Fact]
    public void Rest_KeepsTheOriginalSpacing()
    {
        // A directory name may contain spaces, so :mkdir has to see them exactly as typed.
        Assert.Equal("My Documents", new CommandLine("mkdir My Documents").Rest(1));
        Assert.Equal("a   b", new CommandLine("echo a   b").Rest(1));
    }

    [Fact]
    public void Rest_FromLaterWords()
    {
        CommandLine line = new("one two three four");

        Assert.Equal("two three four", line.Rest(1));
        Assert.Equal("three four", line.Rest(2));
        Assert.Equal(string.Empty, line.Rest(9));
    }

    [Fact]
    public void Rest_HandlesALineWithNoArguments() =>
        Assert.Equal(string.Empty, new CommandLine("quit").Rest(1));

    [Fact]
    public void ParseFlags_PullsOffLeadingFlagGroups()
    {
        (string flags, string rest) = new CommandLine("shell -f ls -l").ParseFlags();

        Assert.Equal("f", flags);
        Assert.Equal("ls -l", rest);
    }

    [Fact]
    public void ParseFlags_AccumulatesSeveralGroups()
    {
        // -f -r and -fr mean the same thing.
        Assert.Equal("fr", new CommandLine("shell -f -r cmd").ParseFlags().Flags);
        Assert.Equal("fr", new CommandLine("shell -fr cmd").ParseFlags().Flags);
    }

    [Fact]
    public void ParseFlags_StopsAtADoubleDash()
    {
        // This is how a command is given an argument that itself starts with a dash.
        (string flags, string rest) = new CommandLine("shell -f -- -l foo").ParseFlags();

        Assert.Equal("f", flags);
        Assert.Equal("-l foo", rest);
    }

    [Fact]
    public void ParseFlags_LeavesALineWithNoFlagsAlone()
    {
        (string flags, string rest) = new CommandLine("shell ls -l").ParseFlags();

        Assert.Equal(string.Empty, flags);
        Assert.Equal("ls -l", rest);
    }

    [Fact]
    public void EmptyLine_HasNoName() => Assert.Equal(string.Empty, new CommandLine("").Name);
}
