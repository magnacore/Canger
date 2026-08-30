// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// Tags left pointing at files that are no longer there.
/// </summary>
/// <remarks>
/// A tag is a note the user made about a particular file. When the file goes the note should go
/// with it, and when the file moves the note should move with it — otherwise
/// <c>~/.local/share/canger/tagged</c> fills up with lines about paths that mean nothing, and the
/// tag someone put on a file yesterday is not on it today.
/// </remarks>
public class TagsOutlivingFilesTests
{
    private static FakeFileManager Manager()
    {
        FakeFileManager manager = new(
            new InMemoryFileSystem()
                .AddFile("/home/manuj/notes.md", "x")
                .AddFile("/home/manuj/keep.md", "y")
                .AddFile("/home/manuj/folder/inner.txt", "z")
                .AddDirectory("/home/manuj/folderly"),
            "/home/manuj");

        manager.SettingsStore.SetFromText("confirm_on_delete", "never");
        return manager;
    }

    private static void Select(FakeFileManager manager, string name) =>
        manager.CurrentTab.MoveCursorTo(manager.CurrentTab.Current.Entries.First(
            e => string.Equals(e.RelativePath, name, StringComparison.Ordinal)));

    [Fact]
    public void DeletingATaggedFileDropsItsTag()
    {
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/manuj/notes.md"]);

        Select(manager, "notes.md");
        manager.Execute("delete");

        Assert.False(manager.Tags.Contains("/home/manuj/notes.md"));
    }

    [Fact]
    public void DeletingATaggedFolderDropsTheTagsInsideItToo()
    {
        // Otherwise a single delete can strand any number of tags at once, and they are the ones
        // hardest to notice — nothing on screen ever refers to them again.
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/manuj/folder"]);
        manager.Tags.Add(["/home/manuj/folder/inner.txt"]);

        Select(manager, "folder");
        manager.Execute("delete");

        Assert.False(manager.Tags.Contains("/home/manuj/folder"));
        Assert.False(manager.Tags.Contains("/home/manuj/folder/inner.txt"));
    }

    [Fact]
    public void ANeighbourWhoseNameMerelyStartsTheSameIsLeftAlone()
    {
        // Ranger compares the two paths with `startswith`, which is a comparison of text rather
        // than of paths: deleting /home/manuj/folder there also untags /home/manuj/folderly.
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/manuj/folderly"]);

        Select(manager, "folder");
        manager.Execute("delete");

        Assert.True(manager.Tags.Contains("/home/manuj/folderly"));
    }

    [Fact]
    public void OtherFilesKeepTheirTags()
    {
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/manuj/notes.md"]);
        manager.Tags.Add(["/home/manuj/keep.md"]);

        Select(manager, "notes.md");
        manager.Execute("delete");

        Assert.True(manager.Tags.Contains("/home/manuj/keep.md"));
    }

    [Fact]
    public void ADeleteThatFailedLeavesTheTagAlone()
    {
        // A tag is something the user put there by hand. Throwing it away for a file that is
        // still on disc loses their work for nothing — which is what ranger does, untagging
        // before it deletes rather than after.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/manuj/notes.md", "x");
        FakeFileManager manager = new(fs, "/home/manuj");
        manager.SettingsStore.SetFromText("confirm_on_delete", "never");
        manager.Tags.Add(["/home/manuj/notes.md"]);

        fs.FailToDelete("/home/manuj/notes.md");
        Select(manager, "notes.md");
        manager.Execute("delete");

        Assert.True(manager.Tags.Contains("/home/manuj/notes.md"));
    }

    [Fact]
    public void RenamingATaggedFileTakesTheTagWithIt()
    {
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/manuj/notes.md"]);

        Select(manager, "notes.md");
        manager.Execute("rename thoughts.md");

        Assert.False(manager.Tags.Contains("/home/manuj/notes.md"));
        Assert.True(manager.Tags.Contains("/home/manuj/thoughts.md"));
    }

    [Fact]
    public void RenamingAFolderTakesTheTagsInsideItAlong()
    {
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/home/manuj/folder/inner.txt"]);

        Select(manager, "folder");
        manager.Execute("rename archive");

        Assert.True(manager.Tags.Contains("/home/manuj/archive/inner.txt"));
    }

    [Fact]
    public void TagsAreNotSweptJustBecauseAFileCannotBeSeen()
    {
        // A tag on a file on a drive that is not plugged in is not a stale tag. Anything that
        // tidied by existence would empty the file the first time somebody browsed without their
        // external disc.
        FakeFileManager manager = Manager();
        manager.Tags.Add(["/media/manuj/BACKUP/film.mkv"]);

        Select(manager, "notes.md");
        manager.Execute("delete");

        Assert.True(manager.Tags.Contains("/media/manuj/BACKUP/film.mkv"));
    }
}
