// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What Ctrl-R does.
/// </summary>
/// <remarks>
/// <c>map &lt;C-r&gt; reset</c> ships in ranger's configuration and in Canger's, and Canger had no
/// such command — the name resolved by unambiguous-prefix abbreviation to
/// <c>reset_previews</c>, so Ctrl-R discarded previews and nothing else. Ranger's
/// (<c>core/actions.py:64-77</c>) also drops every cached directory, which is what makes a
/// measured cumulative size go away and the entry count return.
/// </remarks>
public class ResetCommandTests
{
    private static FakeFileManager Manager() =>
        new(new InMemoryFileSystem()
                .AddFile("/home/sub/a.txt")
                .AddFile("/home/b.txt"),
            "/home");

    [Fact]
    public void Reset_IsACommandOfItsOwn()
    {
        // Not an abbreviation of reset_previews, which is what it silently was.
        FakeFileManager manager = Manager();

        Assert.Equal("reset", manager.Commands.Find("reset")?.Name);
    }

    [Fact]
    public void Reset_DiscardsTheCachedDirectories()
    {
        FakeFileManager manager = Manager();
        DirectoryNode before = manager.Directories.GetLoaded(
            "/home/sub", TestContext.Current.CancellationToken);

        manager.Execute("reset");

        // The path comes straight back, because re-entering /home scans it and interns what it
        // finds. What matters is that it is a *new* node: everything the old one had recorded —
        // a measured size, a cursor, marks — is gone, which is the point of the command.
        Assert.NotSame(before, manager.Directories.Get("/home/sub"));
    }

    [Fact]
    public void Reset_LosesAMeasuredSizeSoTheCountComesBack()
    {
        FakeFileManager manager = Manager();
        DirectoryNode sub = manager.Directories.GetLoaded(
            "/home/sub", TestContext.Current.CancellationToken);
        sub.CumulativeSize = 4500;

        manager.Execute("reset");

        DirectoryNode again = manager.Directories.GetLoaded(
            "/home/sub", TestContext.Current.CancellationToken);

        Assert.NotSame(sub, again);
        Assert.Null(again.CumulativeSize);
        Assert.Equal("1", LinemodeText.Size(again));
    }

    [Fact]
    public void Reset_StaysWhereItWas()
    {
        FakeFileManager manager = Manager();

        manager.Execute("reset");

        Assert.Equal("/home", manager.CurrentTab.Path);
    }

    [Fact]
    public void Reset_DiscardsThePreviews()
    {
        FakeFileManager manager = Manager();

        manager.Execute("reset");

        Assert.True(manager.PreviewsInvalidated);
    }
}
