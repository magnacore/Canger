// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

public class CursorTests
{
    private static List<FsNode> Nodes(params string[] names)
    {
        InMemoryFileSystem fs = new();
        List<FsNode> nodes = [];

        foreach (string name in names)
        {
            string path = "/x/" + name;
            fs.AddFile(path);
            nodes.Add(new FileNode(fs, path, fs.GetStatus(path, followSymbolicLinks: true)));
        }

        return nodes;
    }

    [Fact]
    public void MoveTo_ClampsIntoTheList()
    {
        List<FsNode> items = Nodes("a", "b", "c");
        Cursor cursor = new();

        cursor.MoveTo(1, items);
        Assert.Equal(1, cursor.Index);

        cursor.MoveTo(99, items);
        Assert.Equal(2, cursor.Index);

        cursor.MoveTo(-5, items);
        Assert.Equal(0, cursor.Index);
    }

    [Fact]
    public void MoveTo_HandlesAnEmptyList()
    {
        Cursor cursor = new();

        cursor.MoveTo(3, []);

        Assert.Equal(0, cursor.Index);
        Assert.Null(cursor.Current);
    }

    [Fact]
    public void MoveToNode_FindsTheEntry()
    {
        List<FsNode> items = Nodes("a", "b", "c");
        Cursor cursor = new();

        Assert.True(cursor.MoveToNode(items[2], items));
        Assert.Equal(2, cursor.Index);
    }

    [Fact]
    public void MoveToNode_LeavesTheCursorAloneWhenTheEntryIsAbsent()
    {
        List<FsNode> items = Nodes("a", "b");
        Cursor cursor = new();
        cursor.MoveTo(1, items);

        Assert.False(cursor.MoveToNode(Nodes("elsewhere")[0], items));
        Assert.Equal(1, cursor.Index);
    }

    [Fact]
    public void Reconcile_KeepsTheCursorOnTheSameEntryWhenTheListShifts()
    {
        // Deleting something above the cursor must not make the selection jump to another file.
        List<FsNode> items = Nodes("a", "b", "c");
        Cursor cursor = new();
        cursor.MoveTo(2, items);

        List<FsNode> afterDeletion = [items[1], items[2]];
        cursor.Reconcile(afterDeletion);

        Assert.Equal("c", cursor.Current?.Basename);
        Assert.Equal(1, cursor.Index);
    }

    [Fact]
    public void Reconcile_FallsBackToTheIndexWhenTheEntryHasGone()
    {
        List<FsNode> items = Nodes("a", "b", "c");
        Cursor cursor = new();
        cursor.MoveTo(1, items);

        List<FsNode> afterDeletion = [items[0], items[2]];
        cursor.Reconcile(afterDeletion);

        Assert.Equal(1, cursor.Index);
        Assert.Equal("c", cursor.Current?.Basename);
    }

    [Fact]
    public void Reconcile_ClampsWhenTheListShrankBelowTheIndex()
    {
        List<FsNode> items = Nodes("a", "b", "c");
        Cursor cursor = new();
        cursor.MoveTo(2, items);

        cursor.Reconcile([items[0]]);

        Assert.Equal(0, cursor.Index);
        Assert.Equal("a", cursor.Current?.Basename);
    }

    [Fact]
    public void Reconcile_MatchesByPathNotByInstance()
    {
        // A rescan builds fresh objects, so restoring the cursor has to work across instances.
        List<FsNode> before = Nodes("a", "b", "c");
        List<FsNode> after = Nodes("a", "b", "c");

        Cursor cursor = new();
        cursor.MoveTo(1, before);
        cursor.Reconcile(after);

        Assert.Equal(1, cursor.Index);
        Assert.Same(after[1], cursor.Current);
    }

    [Fact]
    public void Reconcile_HandlesAListThatBecameEmpty()
    {
        List<FsNode> items = Nodes("a");
        Cursor cursor = new();
        cursor.MoveTo(0, items);

        cursor.Reconcile([]);

        Assert.Null(cursor.Current);
        Assert.Equal(0, cursor.Index);
    }
}
