// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;

namespace Canger.Core.Tests.State;

public sealed class TagsTests : IDisposable
{
    private readonly string _root;
    private readonly string _file;

    public TagsTests()
    {
        _root = Path.Join(Path.GetTempPath(), "canger-tags-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _file = Path.Join(_root, "tagged");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    private Tags Build() => new(_file);

    [Fact]
    public void Add_TagsFiles()
    {
        Tags tags = Build();

        tags.Add(["/x/one.txt", "/x/two.txt"]);

        Assert.True(tags.Contains("/x/one.txt"));
        Assert.Equal(Tags.DefaultTag, tags.TagOf("/x/one.txt"));
    }

    [Fact]
    public void Add_AppliesANamedTag()
    {
        // Several independent categories at once is the point of naming them.
        Tags tags = Build();

        tags.Add(["/x/todo.txt"], 'w');
        tags.Add(["/x/done.txt"], 'd');

        Assert.Equal('w', tags.TagOf("/x/todo.txt"));
        Assert.Equal('d', tags.TagOf("/x/done.txt"));
    }

    [Fact]
    public void Remove_ClearsTags()
    {
        Tags tags = Build();
        tags.Add(["/x/one.txt", "/x/two.txt"]);

        tags.Remove(["/x/one.txt"]);

        Assert.False(tags.Contains("/x/one.txt"));
        Assert.True(tags.Contains("/x/two.txt"));
    }

    [Fact]
    public void Toggle_AddsThenRemoves()
    {
        Tags tags = Build();

        tags.Toggle(["/x/one.txt"]);
        Assert.True(tags.Contains("/x/one.txt"));

        tags.Toggle(["/x/one.txt"]);
        Assert.False(tags.Contains("/x/one.txt"));
    }

    [Fact]
    public void Toggle_WithTheDefaultTagClearsWhateverTagIsThere()
    {
        // One key always means "untag this", whatever was applied.
        Tags tags = Build();
        tags.Add(["/x/one.txt"], 'w');

        tags.Toggle(["/x/one.txt"]);

        Assert.False(tags.Contains("/x/one.txt"));
    }

    [Fact]
    public void Toggle_WithANamedTagReplacesADifferentOne()
    {
        Tags tags = Build();
        tags.Add(["/x/one.txt"], 'w');

        tags.Toggle(["/x/one.txt"], 'd');

        Assert.Equal('d', tags.TagOf("/x/one.txt"));
    }

    [Fact]
    public void Tagged_ListsFilesByTag()
    {
        Tags tags = Build();
        tags.Add(["/x/a"], 'w');
        tags.Add(["/x/b"], 'd');
        tags.Add(["/x/c"], 'w');

        Assert.Equal(2, tags.Tagged(['w']).Count);
        Assert.Equal(3, tags.Tagged().Count);
    }

    [Fact]
    public void SaveAndReload_RoundTripThroughTheFile()
    {
        Tags tags = Build();
        tags.Add(["/x/plain.txt"]);
        tags.Add(["/x/named.txt"], 'w');

        Tags reader = Build();
        reader.Reload();

        Assert.Equal(Tags.DefaultTag, reader.TagOf("/x/plain.txt"));
        Assert.Equal('w', reader.TagOf("/x/named.txt"));
    }

    [Fact]
    public void File_UsesRangersFormatSoExistingTagsAreRead()
    {
        // A default-tagged path is written bare; anything else as tag, colon, path.
        File.WriteAllLines(_file, ["/x/plain.txt", "w:/x/named.txt"]);

        Tags tags = Build();
        tags.Reload();

        Assert.Equal(Tags.DefaultTag, tags.TagOf("/x/plain.txt"));
        Assert.Equal('w', tags.TagOf("/x/named.txt"));
    }

    [Fact]
    public void Changes_AreVisibleToAnotherInstance()
    {
        // Each change re-reads before writing, so two windows do not overwrite each other.
        Tags first = Build();
        first.Add(["/x/from-first"]);

        Tags second = Build();
        second.Add(["/x/from-second"]);

        Tags reader = Build();
        reader.Reload();

        Assert.True(reader.Contains("/x/from-first"));
        Assert.True(reader.Contains("/x/from-second"));
    }

    [Fact]
    public void MovePath_FollowsARenamedFile()
    {
        Tags tags = Build();
        tags.Add(["/x/old.txt"], 'w');

        tags.MovePath("/x/old.txt", "/x/new.txt");

        Assert.False(tags.Contains("/x/old.txt"));
        Assert.Equal('w', tags.TagOf("/x/new.txt"));
    }

    [Fact]
    public void MovePath_FollowsEverythingUnderARenamedDirectory()
    {
        // Tagging files and then moving the directory must not lose them.
        Tags tags = Build();
        tags.Add(["/x/dir/one.txt", "/x/dir/sub/two.txt"], 'w');

        tags.MovePath("/x/dir", "/y/moved");

        Assert.Equal('w', tags.TagOf("/y/moved/one.txt"));
        Assert.Equal('w', tags.TagOf("/y/moved/sub/two.txt"));
    }

    [Theory]
    [InlineData('*', true)]
    [InlineData('w', true)]
    [InlineData('1', true)]
    [InlineData(' ', false)]
    public void IsValidTag_RejectsWhatCannotBeWrittenOrRead(char tag, bool expected) =>
        Assert.Equal(expected, Tags.IsValidTag(tag));

    [Fact]
    public void Reload_SurvivesAMissingFile()
    {
        Tags tags = Build();

        tags.Reload();

        Assert.Equal(0, tags.Count);
    }
}
