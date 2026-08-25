// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Marking the whole listing while a visual selection is running.
/// </summary>
/// <remarks>
/// <c>uv</c> is <c>mark_files all=True val=False</c>. Acting on the whole listing ends a visual
/// selection, because the selection is the thing being replaced — ranger does it at
/// <c>core/actions.py:761-763</c>. Without it <c>uv</c> could not unmark at all: it cleared the
/// marks and the still-running visual range put them straight back, leaving no way out of the
/// mode short of quitting.
/// </remarks>
public class VisualModeTests
{
    private static FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt")
            .AddFile("/home/b.txt")
            .AddFile("/home/c.txt")
            .AddFile("/home/d.txt");

        return new FakeFileManager(fs, "/home");
    }

    [Fact]
    public void MarkFilesAll_LeavesVisualMode()
    {
        FakeFileManager manager = Manager();
        manager.ChangeMode("visual");

        manager.Execute("mark_files all=True val=False");

        Assert.False(manager.IsVisualMode);
    }

    [Fact]
    public void MarkFilesAll_UnmarksEverything()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.Current.SetAllMarked(true);
        manager.ChangeMode("visual");

        manager.Execute("mark_files all=True val=False");

        Assert.Empty(manager.CurrentTab.Current.MarkedEntries);
    }

    [Fact]
    public void MarkFilesAllToggle_AlsoLeavesVisualMode()
    {
        // `v` is `mark_files all=True toggle=True`, and inverts the same selection.
        FakeFileManager manager = Manager();
        manager.ChangeMode("visual");

        manager.Execute("mark_files all=True toggle=True");

        Assert.False(manager.IsVisualMode);
        Assert.Equal(4, manager.CurrentTab.Current.MarkedEntries.Count);
    }

    [Fact]
    public void MarkFilesOne_DoesNotLeaveVisualMode()
    {
        // Only the whole-listing forms end the mode. `<Space>` marks one and moves on, which is
        // a perfectly ordinary thing to do part-way through a selection.
        FakeFileManager manager = Manager();
        manager.ChangeMode("visual");

        manager.Execute("mark_files toggle=True");

        Assert.True(manager.IsVisualMode);
    }

    [Fact]
    public void MarkFilesAll_IsHarmlessOutsideVisualMode()
    {
        FakeFileManager manager = Manager();
        manager.CurrentTab.Current.SetAllMarked(true);

        manager.Execute("mark_files all=True val=False");

        Assert.False(manager.IsVisualMode);
        Assert.Empty(manager.CurrentTab.Current.MarkedEntries);
    }
}
