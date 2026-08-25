// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Stepping between sibling directories, which is what <c>]</c> and <c>[</c> are for.
/// </summary>
public class MoveParentCommandTests
{
    /// <summary>Three sibling directories with a file between two of them.</summary>
    private static FakeFileManager Manager(string start = "/top/b")
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/top/a/one.txt")
            .AddFile("/top/b/two.txt")
            .AddFile("/top/loose.txt")
            .AddFile("/top/c/three.txt");

        return new FakeFileManager(fs, start);
    }

    [Fact]
    public void MoveParent_StepsForwardToTheNextSibling()
    {
        FakeFileManager manager = Manager();

        manager.Execute("move_parent 1");

        Assert.Equal("/top/c", manager.CurrentTab.Path);
    }

    [Fact]
    public void MoveParent_StepsBackwardToThePreviousSibling()
    {
        FakeFileManager manager = Manager();

        manager.Execute("move_parent -1");

        Assert.Equal("/top/a", manager.CurrentTab.Path);
    }

    [Fact]
    public void MoveParent_StepsOverAFileRatherThanStoppingOnIt()
    {
        // Ranger indexes straight into the parent's listing and silently does nothing when it
        // lands on a file, which makes the key a dead end wherever the siblings are mixed.
        FakeFileManager manager = Manager("/top/a");

        manager.Execute("move_parent 1");

        Assert.Equal("/top/b", manager.CurrentTab.Path);
    }

    [Fact]
    public void MoveParent_StaysPutAtTheEndOfTheParentsListing()
    {
        FakeFileManager manager = Manager("/top/c");

        manager.Execute("move_parent 1");

        Assert.Equal("/top/c", manager.CurrentTab.Path);
    }

    [Fact]
    public void MoveParent_MultipliesByTheQuantifier()
    {
        FakeFileManager manager = Manager("/top/a");

        manager.Execute("move_parent 1", quantifier: 2);

        Assert.Equal("/top/c", manager.CurrentTab.Path);
    }

    [Fact]
    public void MoveParent_DoesNothingAtTheFilesystemRoot()
    {
        FakeFileManager manager = new(new InMemoryFileSystem().AddFile("/a.txt"), "/");

        manager.Execute("move_parent 1");

        Assert.Equal("/", manager.CurrentTab.Path);
    }

    [Fact]
    public void MoveParent_MovesTheParentsOwnCursorSoTheAncestryColumnFollows()
    {
        // The parent is the same interned object the ancestry column draws, which is what makes
        // the move visible rather than only changing where we are.
        FakeFileManager manager = Manager();

        manager.Execute("move_parent 1");

        DirectoryNode parent = manager.CurrentTab.Parent!;
        Assert.Equal("c", parent.Entries[parent.Cursor.Index].RelativePath);
    }
}
