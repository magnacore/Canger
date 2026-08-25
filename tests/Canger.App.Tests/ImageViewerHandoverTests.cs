// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.App;
using Canger.TestSupport;

namespace Canger.App.Tests;

/// <summary>
/// Handing an image viewer the whole directory, opened at the file that was chosen.
/// </summary>
/// <remarks>
/// The <c>open_all_images</c> setting, which did nothing: opening one photograph opened exactly
/// one, with no way to page through the rest. Ranger installs the same rewrite as a rifle hook
/// (<c>core/fm.py:246-284</c>). Each viewer names its starting position differently and pqiv
/// counts from zero where the others count from one, which is the part worth pinning down —
/// getting it wrong opens the right set at the wrong picture.
/// </remarks>
public class ImageViewerHandoverTests
{
    private static FakeFileManager Manager(bool openAll = true, string cursor = "b.gif")
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/a.gif")
            .AddFile("/home/b.gif")
            .AddFile("/home/c.gif")
            .AddFile("/home/notes.txt");

        FakeFileManager manager = new(fs, "/home");
        manager.SettingsStore.Set("open_all_images", openAll);

        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.RelativePath == cursor));

        return manager;
    }

    [Fact]
    public void Rewrite_GivesSxivEveryImageAndAOneBasedPosition()
    {
        string result = ImageViewerHandover.Rewrite("sxiv -- \"$@\"", Manager());

        Assert.Equal("set -- 'a.gif' 'b.gif' 'c.gif'; sxiv -n 2 -- \"$@\"", result);
    }

    [Fact]
    public void Rewrite_HandlesNsxivToo()
    {
        string result = ImageViewerHandover.Rewrite("nsxiv -- \"$@\"", Manager());

        Assert.Equal("set -- 'a.gif' 'b.gif' 'c.gif'; nsxiv -n 2 -- \"$@\"", result);
    }

    [Fact]
    public void Rewrite_GivesFehTheNameRatherThanAPosition()
    {
        string result = ImageViewerHandover.Rewrite("feh -- \"$@\"", Manager());

        Assert.Equal("set -- 'a.gif' 'b.gif' 'c.gif'; feh --start-at 'b.gif' -- \"$@\"", result);
    }

    [Fact]
    public void Rewrite_CountsFromZeroForPqiv()
    {
        // pqiv is the odd one out, and the reason this is worth a test each.
        string result = ImageViewerHandover.Rewrite("pqiv -- \"$@\"", Manager());

        Assert.Equal(
            "set -- 'a.gif' 'b.gif' 'c.gif'; pqiv --action \"goto_file_byindex(1)\" -- \"$@\"",
            result);
    }

    [Fact]
    public void Rewrite_LeavesTheCommandAloneWhenTheSettingIsOff()
    {
        Assert.Equal("sxiv -- \"$@\"",
                     ImageViewerHandover.Rewrite("sxiv -- \"$@\"", Manager(openAll: false)));
    }

    [Fact]
    public void Rewrite_LeavesTheCommandAloneWhenFilesAreMarked()
    {
        // Marking is an explicit statement of what to open, so "everything here" is not wanted.
        FakeFileManager manager = Manager();
        manager.CurrentTab.Current.Entries[0].IsMarked = true;

        Assert.Equal("sxiv -- \"$@\"",
                     ImageViewerHandover.Rewrite("sxiv -- \"$@\"", manager));
    }

    [Fact]
    public void Rewrite_LeavesOtherProgramsAlone()
    {
        // Only viewers whose ordering ranger knows how to preserve are touched.
        Assert.Equal("gimp -- \"$@\"",
                     ImageViewerHandover.Rewrite("gimp -- \"$@\"", Manager()));
    }

    [Fact]
    public void Rewrite_LeavesTheCommandAloneWhenTheCursorIsNotOnAnImage()
    {
        Assert.Equal("sxiv -- \"$@\"",
                     ImageViewerHandover.Rewrite("sxiv -- \"$@\"", Manager(cursor: "notes.txt")));
    }

    [Fact]
    public void Rewrite_LeavesACommandWithNoFilePlaceholderAlone()
    {
        // Without "$@" there is nothing for the replaced argument list to reach.
        Assert.Equal("sxiv /fixed/path.gif",
                     ImageViewerHandover.Rewrite("sxiv /fixed/path.gif", Manager()));
    }
}
