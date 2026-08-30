// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui;

namespace Canger.Tui.Tests;

/// <summary>
/// Starting a program when Canger's own working directory has been deleted.
/// </summary>
/// <remarks>
/// Another instance of Canger removing the directory this one is sitting in is enough for
/// <c>getcwd(2)</c> to have nothing to return, and <see cref="Environment.CurrentDirectory"/>
/// throws rather than answering. The next external program of any kind — a preview, a plugin
/// announcing where you are — then took the whole browser down with an unhandled exception
/// instead of failing on its own.
/// </remarks>
public class DeletedWorkingDirectoryTests
{
    [Fact]
    public void AProgramStartsSomewhereRealWhenTheCurrentDirectoryHasGone()
    {
        string chosen = TerminalProcessRunner.StartIn(
            null, static () => throw new DirectoryNotFoundException("getcwd: no such directory"));

        Assert.True(Directory.Exists(chosen),
                    $"'{chosen}' has to be somewhere a program can actually be started");
    }

    [Fact]
    public void TheCallersChoiceIsUsedWhenThereIsOne()
    {
        Assert.Equal("/var/tmp",
                     TerminalProcessRunner.StartIn("/var/tmp", static () => "/should/not/be/asked"));
    }

    [Fact]
    public void TheCurrentDirectoryIsUsedWhenItCanBeRead()
    {
        Assert.Equal("/home/someone",
                     TerminalProcessRunner.StartIn(null, static () => "/home/someone"));
    }

    [Fact]
    public void ADeniedCurrentDirectoryIsSurvivedToo()
    {
        // A directory whose parent has had its search permission taken away reads the same way.
        string chosen = TerminalProcessRunner.StartIn(
            null, static () => throw new UnauthorizedAccessException("Permission denied"));

        Assert.True(Directory.Exists(chosen));
    }

    [Fact]
    public void SomethingUnexpectedIsStillAllowedToSurface()
    {
        // Only the two causes that mean "the directory is not there any more" are absorbed.
        // Swallowing everything would hide a bug in the runner behind a working directory.
        Assert.Throws<InvalidOperationException>(
            () => TerminalProcessRunner.StartIn(null, static () => throw new InvalidOperationException()));
    }
}
