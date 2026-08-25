// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Working out a file's kind from its name.
/// </summary>
/// <remarks>
/// This is consulted before <c>file(1)</c>, which matters more than it sounds: an MP3 whose
/// header <c>file</c> cannot place reads as <c>application/octet-stream</c>, matches no audio
/// rule, and gets handed to whatever the desktop calls a default handler — which on a machine
/// with video-editing software installed is not a music player.
/// </remarks>
public class MimeTypesTests
{
    [Theory]
    [InlineData("song.mp3", "audio/")]
    [InlineData("song.flac", "audio/")]
    [InlineData("song.wav", "audio/")]
    [InlineData("song.ogg", "audio/")]
    [InlineData("song.m4a", "audio/")]
    [InlineData("clip.mkv", "video/")]
    [InlineData("clip.mp4", "video/")]
    [InlineData("clip.avi", "video/")]
    [InlineData("photo.jpg", "image/")]
    [InlineData("photo.png", "image/")]
    public void FromExtension_PlacesTheKindsAFileManagerIsAskedToOpen(string name, string prefix)
    {
        string? type = MimeTypes.FromExtension(name);

        Assert.NotNull(type);
        Assert.StartsWith(prefix, type, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExtension_IgnoresTheCaseOfTheExtension()
    {
        Assert.Equal(MimeTypes.FromExtension("song.mp3"), MimeTypes.FromExtension("SONG.MP3"));
    }

    [Fact]
    public void FromExtension_UsesTheLastExtensionOfSeveral()
    {
        Assert.StartsWith("audio/", MimeTypes.FromExtension("my.album.song.mp3")!,
                          StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plainname")]
    [InlineData("archive.qqzz")]
    [InlineData("")]
    [InlineData(".")]
    public void FromExtension_SaysNothingRatherThanGuessing(string name)
    {
        // A wrong answer here would be worse than none: the rules fall back to `ext` matching
        // and to file(1), both of which are better than a fabricated type.
        Assert.Null(MimeTypes.FromExtension(name));
    }

    [Fact]
    public void FromExtension_TreatsADotfileAsHavingNoExtension()
    {
        Assert.Null(MimeTypes.FromExtension(".bashrc"));
    }

    [Fact]
    public void FromExtension_WorksOnAFullPath()
    {
        Assert.StartsWith("audio/",
                          MimeTypes.FromExtension("/home/someone/Music/track.mp3")!,
                          StringComparison.Ordinal);
    }
}
