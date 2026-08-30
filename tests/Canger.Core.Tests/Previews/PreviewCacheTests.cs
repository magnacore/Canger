// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Previews;

namespace Canger.Core.Tests.Previews;

public class PreviewExitCodeTests
{
    [Theory]
    [InlineData(PreviewExitCodes.Success, PreviewKind.Text, PreviewFit.Exact)]
    [InlineData(PreviewExitCodes.NoPreview, PreviewKind.None, PreviewFit.AnySize)]
    [InlineData(PreviewExitCodes.ReadFileAsText, PreviewKind.Text, PreviewFit.AnySize)]
    [InlineData(PreviewExitCodes.FixWidth, PreviewKind.Text, PreviewFit.AnyWidth)]
    [InlineData(PreviewExitCodes.FixHeight, PreviewKind.Text, PreviewFit.AnyHeight)]
    [InlineData(PreviewExitCodes.FixBoth, PreviewKind.Text, PreviewFit.AnySize)]
    [InlineData(PreviewExitCodes.ImageAtCachePath, PreviewKind.Image, PreviewFit.AnySize)]
    [InlineData(PreviewExitCodes.ImageIsTheFile, PreviewKind.DirectImage, PreviewFit.AnySize)]
    public void Interpret_ReadsEachDocumentedCode(int code, PreviewKind kind, PreviewFit fit)
    {
        (PreviewKind actualKind, PreviewFit actualFit) = PreviewExitCodes.Interpret(code);

        Assert.Equal(kind, actualKind);
        Assert.Equal(fit, actualFit);
    }

    [Fact]
    public void Interpret_ShowsNothingForAnUnrecognisedCode()
    {
        // A script exiting oddly should produce no preview rather than a guess.
        (PreviewKind kind, _) = PreviewExitCodes.Interpret(42);

        Assert.Equal(PreviewKind.None, kind);
    }
}

public class PreviewCacheTests
{
    /// <summary>
    /// One file that never changes.
    /// </summary>
    /// <remarks>
    /// These tests are about which entry answers for which size, so each concerns a single
    /// unchanged file. Whether an entry outlives its file is <c>StalePreviewTests</c>.
    /// </remarks>
    private static readonly PreviewStamp Unchanged = new(1000, 42);

    private static PreviewResult Text(string content, PreviewFit fit = PreviewFit.Exact) =>
        new(PreviewKind.Text, fit, content);

    [Fact]
    public void Find_ReturnsNothingWhenThereIsNoPreview() =>
        Assert.Null(new PreviewCache().Find("/x/file", 40, 20, Unchanged));

    [Fact]
    public void Store_RemembersAPreviewForItsExactSize()
    {
        PreviewCache cache = new();
        cache.Store("/x/file", 40, 20, Text("content"), Unchanged);

        Assert.Equal("content", cache.Find("/x/file", 40, 20, Unchanged)?.Text);
    }

    [Fact]
    public void Find_MissesWhenAnExactPreviewIsAskedForAtAnotherSize()
    {
        // The preview depends on both dimensions, so a different size needs a new one.
        PreviewCache cache = new();
        cache.Store("/x/file", 40, 20, Text("content"), Unchanged);

        Assert.Null(cache.Find("/x/file", 41, 20, Unchanged));
        Assert.Null(cache.Find("/x/file", 40, 21, Unchanged));
    }

    [Fact]
    public void Find_ReusesAPreviewThatDoesNotDependOnWidth()
    {
        // Syntax-highlighted source is the same however wide the column is, so making the
        // terminal wider should not rerun the highlighter.
        PreviewCache cache = new();
        cache.Store("/x/file", 40, 20, Text("content", PreviewFit.AnyWidth), Unchanged);

        Assert.Equal("content", cache.Find("/x/file", 200, 20, Unchanged)?.Text);
        Assert.Null(cache.Find("/x/file", 40, 21, Unchanged));
    }

    [Fact]
    public void Find_ReusesAPreviewThatDoesNotDependOnHeight()
    {
        PreviewCache cache = new();
        cache.Store("/x/file", 40, 20, Text("content", PreviewFit.AnyHeight), Unchanged);

        Assert.Equal("content", cache.Find("/x/file", 40, 100, Unchanged)?.Text);
        Assert.Null(cache.Find("/x/file", 41, 20, Unchanged));
    }

    [Fact]
    public void Find_ReusesAPreviewThatDoesNotDependOnSizeAtAll()
    {
        // A metadata listing never changes, so it is generated once and kept.
        PreviewCache cache = new();
        cache.Store("/x/file", 40, 20, Text("content", PreviewFit.AnySize), Unchanged);

        Assert.Equal("content", cache.Find("/x/file", 1, 1, Unchanged)?.Text);
        Assert.Equal("content", cache.Find("/x/file", 500, 500, Unchanged)?.Text);
    }

    [Fact]
    public void Find_KeepsPreviewsOfDifferentFilesApart()
    {
        PreviewCache cache = new();
        cache.Store("/x/one", 40, 20, Text("first", PreviewFit.AnySize), Unchanged);
        cache.Store("/x/two", 40, 20, Text("second", PreviewFit.AnySize), Unchanged);

        Assert.Equal("first", cache.Find("/x/one", 40, 20, Unchanged)?.Text);
        Assert.Equal("second", cache.Find("/x/two", 40, 20, Unchanged)?.Text);
    }

    [Fact]
    public void Invalidate_ForgetsEveryPreviewOfAFile()
    {
        // Needed when a file changes underneath a preview that is still cached.
        PreviewCache cache = new();
        cache.Store("/x/file", 40, 20, Text("old"), Unchanged);
        cache.Store("/x/file", 80, 20, Text("old wide"), Unchanged);
        cache.Store("/x/other", 40, 20, Text("keep", PreviewFit.AnySize), Unchanged);

        Assert.Equal(2, cache.Invalidate("/x/file"));
        Assert.Null(cache.Find("/x/file", 40, 20, Unchanged));
        Assert.NotNull(cache.Find("/x/other", 40, 20, Unchanged));
    }

    [Fact]
    public void Cache_DropsTheLeastRecentlyUsedOnceItIsFull()
    {
        // A long session moving through a large tree must not accumulate previews indefinitely.
        PreviewCache cache = new(capacity: 3);

        for (int i = 0; i < 5; i++)
        {
            cache.Store($"/x/file{i}", 40, 20, Text($"content {i}", PreviewFit.AnySize), Unchanged);
        }

        Assert.Equal(3, cache.Count);
        Assert.Null(cache.Find("/x/file0", 40, 20, Unchanged));
        Assert.NotNull(cache.Find("/x/file4", 40, 20, Unchanged));
    }

    [Fact]
    public void Cache_KeepsWhatIsStillBeingUsed()
    {
        PreviewCache cache = new(capacity: 3);
        cache.Store("/x/keep", 40, 20, Text("keep", PreviewFit.AnySize), Unchanged);

        for (int i = 0; i < 2; i++)
        {
            cache.Store($"/x/other{i}", 40, 20, Text("other", PreviewFit.AnySize), Unchanged);
            cache.Find("/x/keep", 40, 20, Unchanged);
        }

        cache.Store("/x/newest", 40, 20, Text("newest", PreviewFit.AnySize), Unchanged);

        Assert.NotNull(cache.Find("/x/keep", 40, 20, Unchanged));
    }
}
