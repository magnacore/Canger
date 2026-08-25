// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What <c>q</c> and <c>Q</c> do.
/// </summary>
/// <remarks>
/// All four quit commands called <c>Quit()</c> and nothing else, so <c>q</c> closed the whole
/// program even with several tabs open — while <c>quit</c>'s own summary said it closed a tab —
/// and a copy in progress was abandoned silently. Ranger's rules are
/// <c>config/commands.py:654-710</c>: <c>quit</c> closes a tab when there are two or more,
/// otherwise it quits, and it refuses while the loader has work unless forced.
/// </remarks>
public class QuitCommandTests
{
    private static FakeFileManager Manager() =>
        new(new InMemoryFileSystem().AddFile("/home/a.txt"), "/home");

    /// <summary>A job that never finishes, so the queue can be found busy.</summary>
    private sealed class Endless : ILoadable
    {
        public string Description => "copying";

        public bool IsFinished { get; private set; }

        public double? Progress => 0.5;

        public IEnumerator<Unit> Steps()
        {
            while (!IsFinished)
            {
                yield return Unit.Value;
            }
        }
    }

    [Fact]
    public void Quit_ClosesTheTabWhenThereIsMoreThanOne()
    {
        FakeFileManager manager = Manager();
        manager.OpenTab(2, "/home");

        Assert.Equal(2, manager.Tabs.Count);

        manager.Execute("quit");

        Assert.False(manager.HasQuit);
        Assert.Single(manager.Tabs);
    }

    [Fact]
    public void Quit_QuitsWhenItIsTheLastTab()
    {
        FakeFileManager manager = Manager();

        manager.Execute("quit");

        Assert.True(manager.HasQuit);
    }

    [Fact]
    public void Quit_RefusesWhileWorkIsInProgress()
    {
        // Ranger names the escape hatch rather than abandoning a half-copied file.
        FakeFileManager manager = Manager();
        manager.Tasks.Add(new Endless());

        manager.Execute("quit");

        Assert.False(manager.HasQuit);
        Assert.Contains(manager.Messages,
                        m => m.Message.Contains("quit!", StringComparison.Ordinal));
    }

    [Fact]
    public void QuitBang_QuitsDespiteWorkInProgress()
    {
        FakeFileManager manager = Manager();
        manager.Tasks.Add(new Endless());

        manager.Execute("quit!");

        Assert.True(manager.HasQuit);
    }

    [Fact]
    public void QuitBang_StillClosesATabFirst()
    {
        // The forcing form forces the *quit*, not the tab handling.
        FakeFileManager manager = Manager();
        manager.OpenTab(2, "/home");

        manager.Execute("quit!");

        Assert.False(manager.HasQuit);
        Assert.Single(manager.Tabs);
    }

    [Fact]
    public void QuitAll_QuitsWithSeveralTabsOpen()
    {
        FakeFileManager manager = Manager();
        manager.OpenTab(2, "/home");

        manager.Execute("quitall");

        Assert.True(manager.HasQuit);
    }

    [Fact]
    public void QuitAll_RefusesWhileWorkIsInProgress()
    {
        FakeFileManager manager = Manager();
        manager.Tasks.Add(new Endless());

        manager.Execute("quitall");

        Assert.False(manager.HasQuit);
        Assert.Contains(manager.Messages,
                        m => m.Message.Contains("quitall!", StringComparison.Ordinal));
    }

    [Fact]
    public void QuitAllBang_QuitsDespiteWorkInProgress()
    {
        FakeFileManager manager = Manager();
        manager.Tasks.Add(new Endless());

        manager.Execute("quitall!");

        Assert.True(manager.HasQuit);
    }
}
