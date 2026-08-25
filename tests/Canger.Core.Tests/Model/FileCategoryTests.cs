// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// What a file is judged to be from its name, which is what decides its colour.
/// </summary>
/// <remarks>
/// The colourschemes had rules for media, images and archives from the beginning, but nothing ever
/// produced the context keys — so every file in a listing fell through to the terminal's default
/// and the whole thing was three colours instead of ranger's eight. These pin the classification
/// that feeds them.
/// </remarks>
public class FileCategoryTests
{
    private static FsNode Node(string name)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/" + name);

        return new FileNode(fs, "/home/" + name,
                            fs.GetStatus("/home/" + name, followSymbolicLinks: true));
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("scan.JPEG")]
    [InlineData("drawing.gif")]
    public void Images_AreImagesAndMedia(string name)
    {
        FsNode node = Node(name);

        Assert.True(node.IsImage);
        Assert.True(node.IsMedia);
        Assert.False(node.IsVideo);
        Assert.False(node.IsAudio);
    }

    [Theory]
    [InlineData("film.mkv")]
    [InlineData("clip.mp4")]
    [InlineData("old.avi")]
    public void Videos_AreVideosAndMedia(string name)
    {
        FsNode node = Node(name);

        Assert.True(node.IsVideo);
        Assert.True(node.IsMedia);
        Assert.False(node.IsImage);
    }

    [Theory]
    [InlineData("song.mp3")]
    [InlineData("track.flac")]
    [InlineData("voice.ogg")]
    public void Audio_IsAudioAndMedia(string name)
    {
        FsNode node = Node(name);

        Assert.True(node.IsAudio);
        Assert.True(node.IsMedia);
    }

    [Theory]
    [InlineData("bundle.tar")]
    [InlineData("archive.zip")]
    [InlineData("thing.7z")]
    [InlineData("package.deb")]
    [InlineData("image.iso")]
    public void Archives_AreContainers(string name) => Assert.True(Node(name).IsContainer);

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("paper.pdf")]
    [InlineData("page.html")]
    [InlineData("sheet.ods")]
    [InlineData("readme.md")]
    public void Documents_AreDocuments(string name) => Assert.True(Node(name).IsDocument);

    [Theory]
    [InlineData("README")]
    [InlineData("LICENSE")]
    [InlineData("ChangeLog")]
    [InlineData("TODO")]
    public void KnownBarenames_AreDocumentsWithoutAnExtension(string name) =>
        Assert.True(Node(name).IsDocument);

    [Fact]
    public void APartialDownload_IsWhateverItWillBecome()
    {
        // Ranger drops a `.part` extension before classifying, so a half-downloaded film is
        // still coloured as a film rather than as nothing at all.
        FsNode node = Node("film.mkv.part");

        Assert.True(node.IsVideo);
        Assert.True(node.IsMedia);
    }

    [Fact]
    public void AnUnknownExtension_IsNothingInParticular()
    {
        FsNode node = Node("data.qqq");

        Assert.False(node.IsMedia);
        Assert.False(node.IsContainer);
        Assert.False(node.IsDocument);
        Assert.Equal(FileCategory.None, node.Category);
    }

    [Fact]
    public void ACompressedTarball_IsAnArchiveRatherThanNothing()
    {
        // .gz is on ranger's container list, so this is red either way; worth pinning because the
        // extension of `x.tar.gz` is `gz`, not `tar.gz`.
        Assert.True(Node("bundle.tar.gz").IsContainer);
    }

    [Fact]
    public void TheListsAreRangersOwn()
    {
        // Ranger's judgement rather than anything canonical: .gz counts as an archive and .md as
        // a document. Matching exactly is the point.
        // Cast away the frozen set, or xunit cannot decide which overload it wants.
        Assert.Contains("gz", (IEnumerable<string>)FileCategories.ContainerExtensions);
        Assert.Contains("md", (IEnumerable<string>)FileCategories.DocumentExtensions);
        Assert.Contains("readme", (IEnumerable<string>)FileCategories.DocumentBasenames);
        Assert.Equal(27, FileCategories.ContainerExtensions.Count);
        Assert.Equal(28, FileCategories.DocumentExtensions.Count);
        Assert.Equal(10, FileCategories.DocumentBasenames.Count);
    }
}
