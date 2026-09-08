// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// <c>:setlocal</c> and its siblings typed at the console.
/// </summary>
/// <remarks>
/// The parsing is covered where the parsing lives; what is pinned here is the part only the
/// command can get wrong — that it passes the directory the user is actually in, and that the
/// line reaches the parser at all. The command used to answer "setlocal is only available in the
/// configuration file so far".
/// </remarks>
public class ScopedSettingCommandTests
{
    private static FakeFileManager Build()
    {
        InMemoryFileSystem files = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/videos")
            .AddDirectory("/home/other")
            .AddFile("/home/videos/a.mkv");

        FakeFileManager manager = new(files, "/home/videos");
        manager.SettingsStore.SetFromText("sort", "natural");

        return manager;
    }

    [Theory]
    [InlineData("setlocal sort mtime")]
    [InlineData("setlocal sort=mtime")]
    [InlineData("setinpath sort mtime")]
    [InlineData("setinregex sort mtime")]
    public void ScopesToTheDirectoryTheUserIsIn(string line)
    {
        FakeFileManager manager = Build();

        Assert.True(manager.Execute(line));

        Assert.Equal("mtime", manager.SettingsStore.Get<string>("sort", "/home/videos"));
        Assert.Equal("natural", manager.SettingsStore.Get<string>("sort", "/home/other"));
    }

    [Fact]
    public void UsesWhereTheUserIsNowRatherThanWhereTheyStarted()
    {
        // The seam: a command that passed a remembered path, or the tab's starting directory,
        // would put the rule on the wrong folder and look right in a test that never moved.
        FakeFileManager manager = Build();

        manager.Execute("cd /home/other");
        Assert.Equal("/home/other", manager.CurrentTab.Path);

        Assert.True(manager.Execute("setlocal sort mtime"));

        Assert.Equal("mtime", manager.SettingsStore.Get<string>("sort", "/home/other"));
        Assert.Equal("natural", manager.SettingsStore.Get<string>("sort", "/home/videos"));
    }

    [Fact]
    public void AnExplicitPathIsObeyedInsteadOfTheCurrentOne()
    {
        FakeFileManager manager = Build();

        Assert.True(manager.Execute("setlocal path=/home/other sort mtime"));

        Assert.Equal("mtime", manager.SettingsStore.Get<string>("sort", "/home/other"));
        Assert.Equal("natural", manager.SettingsStore.Get<string>("sort", "/home/videos"));
    }

    [Fact]
    public void SaysWhichSettingIsMissingRatherThanDoingNothing()
    {
        FakeFileManager manager = Build();

        manager.Execute("setlocal");

        Assert.Contains("which setting", string.Join(" | ", manager.Messages.Select(m => m.Message)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsABadValueInsteadOfThrowing()
    {
        FakeFileManager manager = Build();

        manager.Execute("setlocal column_ratios not-a-number");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void NoLongerRefusesAtTheConsole()
    {
        // The message this replaced, named so a revert cannot pass quietly.
        FakeFileManager manager = Build();

        manager.Execute("setlocal sort mtime");

        Assert.DoesNotContain(
            "only available in the configuration file",
            string.Join(" | ", manager.Messages.Select(m => m.Message)),
            StringComparison.Ordinal);
    }
}
