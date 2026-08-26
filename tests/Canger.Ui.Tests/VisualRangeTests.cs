// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// A visual selection is a range between two ends. These cover what happens to that range when
/// the listing beneath it changes — a file written into the middle of it, or the anchor's own
/// file going away.
/// </summary>
public class VisualRangeTests
{
    /// <summary>Loads a directory holding the named files.</summary>
    private static DirectoryNode Load(InMemoryFileSystem fs, params string[] names)
    {
        foreach (string name in names)
        {
            fs.AddFile($"/home/{name}");
        }

        DirectoryNode directory = new DirectoryCache(fs).Get("/home");
        directory.Load(TestContext.Current.CancellationToken);
        return directory;
    }

    private static string Marks(DirectoryNode directory) =>
        string.Join(",", directory.MarkedEntries.Select(e => e.Basename));

    [Fact]
    public void Anchor_FollowsItsFileWhenTheListingShiftsBeneathIt()
    {
        // A file arriving above the anchor pushes every row below it down. Remembering the row
        // number alone would leave the anchor naming whatever moved into it.
        InMemoryFileSystem fs = new();
        DirectoryNode directory = Load(fs, "b.txt", "c.txt");

        Assert.Equal(0, VisualRange.AnchorIndex(directory.Entries, "/home/b.txt", 0));

        fs.AddFile("/home/a.txt");
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal(1, VisualRange.AnchorIndex(directory.Entries, "/home/b.txt", 0));
    }

    [Fact]
    public void Anchor_FallsBackToTheRememberedRowWhenItsFileIsGone()
    {
        // Ranger only ever has the row, clamped to the listing (core/actions.py:525). With the
        // file deleted there is nothing better to go on.
        InMemoryFileSystem fs = new();
        DirectoryNode directory = Load(fs, "a.txt", "b.txt");

        Assert.Equal(1, VisualRange.AnchorIndex(directory.Entries, "/home/gone.txt", 1));
        Assert.Equal(1, VisualRange.AnchorIndex(directory.Entries, "/home/gone.txt", 9));
        Assert.Equal(0, VisualRange.AnchorIndex(directory.Entries, null, -3));
    }

    [Fact]
    public void Apply_MarksTheRangeAndRestoresWhatWasOutsideIt()
    {
        InMemoryFileSystem fs = new();
        DirectoryNode directory = Load(fs, "a.txt", "b.txt", "c.txt", "d.txt");

        // d was marked before the mode began, so it must survive being outside the range.
        HashSet<string> before = ["/home/d.txt"];
        directory.Entries[3].IsMarked = true;

        VisualRange.Apply(directory.Entries, anchor: 0, cursor: 1, reverse: false, before);

        Assert.Equal("a.txt,b.txt,d.txt", Marks(directory));
    }

    [Fact]
    public void Apply_SweepsTheSameRangeWhicheverEndTheCursorIsAt()
    {
        InMemoryFileSystem fs = new();
        DirectoryNode directory = Load(fs, "a.txt", "b.txt", "c.txt");

        VisualRange.Apply(directory.Entries, anchor: 2, cursor: 0, reverse: false, null);

        Assert.Equal("a.txt,b.txt,c.txt", Marks(directory));
    }

    [Fact]
    public void Apply_UnmarksTheRangeInReverse()
    {
        // Reverse mode starts from an existing selection and takes files out of it, so what was
        // marked beforehand is exactly what the rows outside the range revert to.
        InMemoryFileSystem fs = new();
        DirectoryNode directory = Load(fs, "a.txt", "b.txt", "c.txt");

        HashSet<string> before = [];
        foreach (FsNode entry in directory.Entries)
        {
            entry.IsMarked = true;
            before.Add(entry.Path);
        }

        VisualRange.Apply(directory.Entries, anchor: 0, cursor: 1, reverse: true, before);

        Assert.Equal("c.txt", Marks(directory));
    }

    [Fact]
    public void Apply_IsNotRunAgainWhenAFileAppearsInsideTheRange()
    {
        // The reported defect, at the level the arithmetic can show it: a selection swept once
        // must not be swept again just because the listing gained a file. The browser enforces
        // that by only calling Apply when the cursor moved; here it simply is not called again,
        // and the new file must be left alone.
        InMemoryFileSystem fs = new();
        DirectoryNode directory = Load(fs, "a.txt", "c.txt");

        VisualRange.Apply(directory.Entries, anchor: 0, cursor: 1, reverse: false, null);
        Assert.Equal("a.txt,c.txt", Marks(directory));

        fs.AddFile("/home/b.txt");
        directory.Load(TestContext.Current.CancellationToken);

        Assert.Equal("a.txt,c.txt", Marks(directory));

        // And when the user does move, the anchor is still on a.txt rather than on the row it
        // used to occupy, so the range that gets swept is the one they can see.
        VisualRange.Apply(directory.Entries,
                          VisualRange.AnchorIndex(directory.Entries, "/home/a.txt", 0),
                          cursor: 2, reverse: false, null);

        Assert.Equal("a.txt,b.txt,c.txt", Marks(directory));
    }
}
