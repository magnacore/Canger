// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Previews;

namespace Canger.Core.Tests.Previews;

/// <summary>
/// A preview outliving the file it is a picture of.
/// </summary>
/// <remarks>
/// Edit a file, move the cursor back onto it, and the preview showed what the file used to say —
/// until the whole cache was reset by hand. The cache was keyed by path and size and nothing else,
/// so there was no version of the file for an entry to belong to. The per-file
/// <c>Invalidate(path)</c> that would have fixed it existed already and had no callers anywhere.
/// </remarks>
public class StalePreviewTests
{
    private static PreviewResult Text(string body, PreviewFit fit = PreviewFit.Exact) =>
        new(PreviewKind.Text, fit, body);

    private static readonly PreviewStamp Before = new(1000, 42);
    private static readonly PreviewStamp After = new(2000, 42);

    [Fact]
    public void AnEditedFileDoesNotServeItsOldPreview()
    {
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("old first line"), Before);

        Assert.Null(cache.Find("/x/notes.md", 40, 20, After));
    }

    [Fact]
    public void AnUnchangedFileStillServesIts()
    {
        // The other half: a stamp that is doing its job must not throw away work needlessly, or
        // every cursor movement runs the preview script again.
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("first line"), Before);

        Assert.Equal("first line", cache.Find("/x/notes.md", 40, 20, Before)?.Text);
    }

    [Fact]
    public void AnEditThatKeepsTheLengthIsStillNoticed()
    {
        // Changing one character for another leaves the size alone, which is why the time is
        // part of the stamp and not just the size.
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("aaa"), new PreviewStamp(1000, 3));

        Assert.Null(cache.Find("/x/notes.md", 40, 20, new PreviewStamp(2000, 3)));
    }

    [Fact]
    public void AChangeOfLengthAloneIsNoticedToo()
    {
        // And a copy that preserves timestamps leaves the time alone, which is why the size is
        // part of it and not just the time.
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("aaa"), new PreviewStamp(1000, 3));

        Assert.Null(cache.Find("/x/notes.md", 40, 20, new PreviewStamp(1000, 9)));
    }

    [Fact]
    public void EveryRememberedSizeOfAChangedFileGoesAtOnce()
    {
        // A preview is asked for at whatever size the column happens to be. Dropping only the
        // entry that was looked up would leave the others to be found after the next resize.
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("old"), Before);
        cache.Store("/x/notes.md", 80, 20, Text("old, wider"), Before);

        cache.Find("/x/notes.md", 40, 20, After);

        Assert.Null(cache.Find("/x/notes.md", 80, 20, Before));
    }

    [Fact]
    public void OtherFilesAreLeftAlone()
    {
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("notes"), Before);
        cache.Store("/x/other.md", 40, 20, Text("other"), Before);

        cache.Find("/x/notes.md", 40, 20, After);

        Assert.Equal("other", cache.Find("/x/other.md", 40, 20, Before)?.Text);
    }

    [Fact]
    public void APreviewGoodAtAnySizeIsStillDroppedWhenTheFileChanges()
    {
        // The wildcard entry is the one most likely to be stale, being the one that survives
        // every resize.
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20,
                    Text("old", PreviewFit.AnySize), Before);

        Assert.Null(cache.Find("/x/notes.md", 99, 99, After));
    }

    [Fact]
    public void AFileThatCanNoLongerBeExaminedServesNothing()
    {
        // Deleted, or unreadable. Whatever the cache holds is of something that is not there.
        PreviewCache cache = new();
        cache.Store("/x/notes.md", 40, 20, Text("old"), Before);

        Assert.Null(cache.Find("/x/notes.md", 40, 20, PreviewStamp.Unknown));
    }

    [Fact]
    public void AStampIsTakenFromTheFilesTimeAndSize()
    {
        // C# has no octal literal, so the mode is written the way the struct stores it.
        FileStatus status = new(
            FileKind.Regular, Mode: 0x81A4, Size: 1234, HardLinkCount: 1, Uid: 0, Gid: 0,
            Inode: 0, Device: 0,
            AccessTime: DateTimeOffset.UnixEpoch,
            ModifyTime: DateTimeOffset.UnixEpoch.AddSeconds(90),
            ChangeTime: DateTimeOffset.UnixEpoch);

        PreviewStamp stamp = PreviewStamp.Of(status);

        Assert.Equal(new PreviewStamp(DateTimeOffset.UnixEpoch.AddSeconds(90).UtcTicks, 1234),
                     stamp);
    }

    [Fact]
    public void AFileWithNoMetadataStampsAsUnknown()
    {
        Assert.Equal(PreviewStamp.Unknown, PreviewStamp.Of(null));
    }
}
