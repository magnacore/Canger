// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Model.Filters;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

public class FilterStackTests
{
    private static FsNode Node(string name, bool directory = false)
    {
        InMemoryFileSystem fs = new();
        string path = "/x/" + name;

        if (directory)
        {
            fs.AddDirectory(path);
            return new DirectoryNode(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
        }

        fs.AddFile(path);
        return new FileNode(fs, path, fs.GetStatus(path, followSymbolicLinks: true));
    }

    [Fact]
    public void EmptyStack_AcceptsEverything()
    {
        FilterStack stack = new();

        Assert.True(stack.IsEmpty);
        Assert.True(stack.Accepts(Node("anything")));
    }

    [Fact]
    public void Filters_Accumulate_AndAllMustAccept()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter("report"));
        stack.Push(new NameFilter(@"\.txt$"));

        Assert.True(stack.Accepts(Node("report.txt")));
        Assert.False(stack.Accepts(Node("report.pdf")));
        Assert.False(stack.Accepts(Node("summary.txt")));
    }

    [Fact]
    public void Pop_RemovesTheMostRecentFilter()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter("a"));
        stack.Push(new NameFilter("b"));

        Assert.Equal("name b", stack.Pop()?.Description);
        Assert.Single(stack.Filters);
    }

    [Fact]
    public void Pop_OnAnEmptyStackReturnsNothing() => Assert.Null(new FilterStack().Pop());

    [Fact]
    public void Combine_ReplacesTheTopTwoFiltersWithTheCombination()
    {
        // The combinator consumes its operands rather than sitting beside them, which is what
        // makes the reverse Polish style work from the keyboard.
        FilterStack stack = new();
        stack.Push(new NameFilter(@"\.txt$"));
        stack.Push(new NameFilter(@"\.md$"));

        Assert.True(stack.Combine((left, right) => new OrFilter(left, right)));

        Assert.Single(stack.Filters);
        Assert.True(stack.Accepts(Node("notes.txt")));
        Assert.True(stack.Accepts(Node("notes.md")));
        Assert.False(stack.Accepts(Node("notes.pdf")));
    }

    [Fact]
    public void Combine_NeedsTwoOperands()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter("only"));

        Assert.False(stack.Combine((left, right) => new AndFilter(left, right)));
        Assert.Single(stack.Filters);
    }

    [Fact]
    public void Negate_InvertsTheTopFilter()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter(@"\.bak$"));

        Assert.True(stack.Negate());

        Assert.True(stack.Accepts(Node("notes.txt")));
        Assert.False(stack.Accepts(Node("notes.bak")));
    }

    [Fact]
    public void Decompose_UndoesOneCombination()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter(@"\.txt$"));
        stack.Push(new NameFilter(@"\.md$"));
        stack.Combine((left, right) => new OrFilter(left, right));

        Assert.True(stack.Decompose());

        Assert.Equal(2, stack.Filters.Count);
        // Back to both applying, so nothing can match both extensions at once.
        Assert.False(stack.Accepts(Node("notes.txt")));
    }

    [Fact]
    public void Decompose_DoesNothingToASimpleFilter()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter("plain"));

        Assert.False(stack.Decompose());
        Assert.Single(stack.Filters);
    }

    [Fact]
    public void Rotate_MovesTheTopFilterToTheBottom()
    {
        // Rotating is what lets a combinator be applied to a different pair without retyping.
        FilterStack stack = new();
        stack.Push(new NameFilter("a"));
        stack.Push(new NameFilter("b"));
        stack.Push(new NameFilter("c"));

        stack.Rotate();

        Assert.Equal(["name c", "name a", "name b"], stack.Describe());
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter("a"));
        stack.Push(new NameFilter("b"));

        stack.Clear();

        Assert.True(stack.IsEmpty);
    }

    [Fact]
    public void InodeTypeFilter_SelectsByKind()
    {
        FsNode file = Node("plain.txt");
        FsNode directory = Node("sub", directory: true);

        Assert.True(new InodeTypeFilter("d").Accepts(directory));
        Assert.False(new InodeTypeFilter("d").Accepts(file));

        Assert.True(new InodeTypeFilter("f").Accepts(file));
        Assert.False(new InodeTypeFilter("f").Accepts(directory));

        // The letters combine, so "df" accepts both.
        Assert.True(new InodeTypeFilter("df").Accepts(file));
        Assert.True(new InodeTypeFilter("df").Accepts(directory));
    }

    [Fact]
    public void NarrowFilter_KeepsOnlyTheNamedEntries()
    {
        NarrowFilter filter = new(["keep.txt", "also.txt"]);

        Assert.True(filter.Accepts(Node("keep.txt")));
        Assert.True(filter.Accepts(Node("also.txt")));
        Assert.False(filter.Accepts(Node("other.txt")));
    }

    [Fact]
    public void Describe_ShowsTheStackBottomFirst()
    {
        FilterStack stack = new();
        stack.Push(new NameFilter(@"\.txt$"));
        stack.Push(new InodeTypeFilter("f"));
        stack.Combine((left, right) => new AndFilter(left, right));

        Assert.Equal([@"(name \.txt$ AND type f)"], stack.Describe());
    }
}
