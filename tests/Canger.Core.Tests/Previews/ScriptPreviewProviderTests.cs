// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Previews;
using Canger.Preview;
using Canger.TestSupport;

namespace Canger.Core.Tests.Previews;

/// <summary>
/// The preview provider, including the decisions it makes before any script is involved.
/// </summary>
public sealed class ScriptPreviewProviderTests : IDisposable
{
    private readonly string _scratch;

    public ScriptPreviewProviderTests()
    {
        _scratch = Path.Join(Path.GetTempPath(), "canger-preview-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    /// <summary>A provider with no usable script, so the built-in behaviour is exercised.</summary>
    private ScriptPreviewProvider Build(InMemoryFileSystem fileSystem) =>
        new(fileSystem, new ScopeScriptRunner(Path.Join(_scratch, "missing.sh"), _scratch));

    private static PreviewSize Size => new(40, 20);

    [Fact]
    public void Preview_ShowsAFilesText()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes.txt", "hello there");

        PreviewResult result = Build(fs).Preview("/x/notes.txt", Size, TestContext.Current.CancellationToken);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Contains("hello there", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_ListsADirectory()
    {
        // Handled directly rather than by a script: the browser already knows how to read a
        // directory, and shelling out would be slower and less consistent.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/x/dir/alpha.txt")
            .AddDirectory("/x/dir/sub");

        PreviewResult result = Build(fs).Preview("/x/dir", Size, TestContext.Current.CancellationToken);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Contains("sub/", result.Text, StringComparison.Ordinal);
        Assert.Contains("alpha.txt", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_SaysSoForAnEmptyDirectory()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/x/empty");

        Assert.Equal("empty", Build(fs).Preview("/x/empty", Size, TestContext.Current.CancellationToken).Text);
    }

    [Fact]
    public void Preview_ShowsNothingForBinaryContent()
    {
        // Filling the column with rubbish would be worse than showing nothing, and could
        // confuse the terminal.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/x/binary", "text\u0000\u0001 more");

        Assert.Equal(PreviewKind.None, Build(fs).Preview("/x/binary", Size, TestContext.Current.CancellationToken).Kind);
    }

    [Fact]
    public void Preview_RespectsTheSizeLimit()
    {
        // Moving the cursor over a very large file must not stall the browser.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFileOfSize("/x/huge.log", 100_000_000);
        ScriptPreviewProvider provider = Build(fs);
        provider.MaximumSize = 1_000_000;

        Assert.Equal(PreviewKind.None, provider.Preview("/x/huge.log", Size, TestContext.Current.CancellationToken).Kind);
    }

    [Fact]
    public void Preview_ShowsNothingWhenFilePreviewsAreTurnedOff()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes.txt", "content");
        ScriptPreviewProvider provider = Build(fs);
        provider.PreviewFiles = false;

        Assert.Equal(PreviewKind.None, provider.Preview("/x/notes.txt", Size, TestContext.Current.CancellationToken).Kind);
    }

    [Fact]
    public void Preview_ShowsNothingWhenDirectoryPreviewsAreTurnedOff()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/x/dir");
        ScriptPreviewProvider provider = Build(fs);
        provider.PreviewDirectories = false;

        Assert.Equal(PreviewKind.None, provider.Preview("/x/dir", Size, TestContext.Current.CancellationToken).Kind);
    }

    [Fact]
    public void Preview_ShowsNothingForAMissingFile() =>
        Assert.Equal(PreviewKind.None,
                     Build(new InMemoryFileSystem()).Preview("/x/nothing", Size, TestContext.Current.CancellationToken).Kind);

    [Fact]
    public void Preview_ShowsNothingWhenThereIsNoRoom()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes.txt", "content");

        Assert.Equal(PreviewKind.None,
                     Build(fs).Preview("/x/notes.txt", new PreviewSize(0, 0), TestContext.Current.CancellationToken).Kind);
    }

    [Fact]
    public void Invalidate_MakesTheNextPreviewFresh()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes.txt", "before");
        ScriptPreviewProvider provider = Build(fs);

        Assert.Contains("before", provider.Preview("/x/notes.txt", Size, TestContext.Current.CancellationToken).Text,
                        StringComparison.Ordinal);

        fs.AddFile("/x/notes.txt", "after");
        provider.Invalidate("/x/notes.txt");

        Assert.Contains("after", provider.Preview("/x/notes.txt", Size, TestContext.Current.CancellationToken).Text,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_IsRememberedRatherThanRegenerated()
    {
        // Regenerating on every cursor move would make the browser unusable.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/x/notes.txt", "before");
        ScriptPreviewProvider provider = Build(fs);

        provider.Preview("/x/notes.txt", Size, TestContext.Current.CancellationToken);
        fs.AddFile("/x/notes.txt", "after");

        // Without invalidating, the remembered text is what comes back.
        Assert.Contains("before", provider.Preview("/x/notes.txt", Size, TestContext.Current.CancellationToken).Text,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_ReportsAMissingScriptAsUnusable()
    {
        ScopeScriptRunner runner = new(Path.Join(_scratch, "nothing.sh"), _scratch);

        Assert.False(runner.IsUsable);
        Assert.Equal(PreviewKind.None, runner.Run("/x/file", Size, TestContext.Current.CancellationToken).Kind);
    }

    [Fact]
    public void Runner_ReportsANonExecutableScriptAsUnusable()
    {
        // A script without the executable bit cannot be run, and saying so is better than
        // failing once per keystroke.
        string script = Path.Join(_scratch, "not-executable.sh");
        File.WriteAllText(script, "#!/bin/sh\necho hello\n");

        Assert.False(new ScopeScriptRunner(script, _scratch).IsUsable);
    }

    [Fact]
    public void Runner_NamesTheImageCacheByAHashOfThePath()
    {
        // Two files of the same name in different directories must not collide, and the name
        // has to be safe whatever the original contained.
        ScopeScriptRunner runner = new(Path.Join(_scratch, "s.sh"), _scratch);

        string first = runner.CachePathFor("/a/photo.png");
        string second = runner.CachePathFor("/b/photo.png");

        Assert.NotEqual(first, second);
        Assert.Equal(first, runner.CachePathFor("/a/photo.png"));
    }

    [Fact]
    public void Runner_UsesARealScriptAndItsExitCode()
    {
        // The exit code is the whole protocol, so it is worth checking against a real script.
        string script = Path.Join(_scratch, "scope.sh");
        File.WriteAllText(script, "#!/bin/sh\necho \"previewing $1 at $2x$3\"\nexit 5\n");
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        ScopeScriptRunner runner = new(script, _scratch);
        PreviewResult result = runner.Run("/x/file.txt", new PreviewSize(40, 20), TestContext.Current.CancellationToken);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Equal(PreviewFit.AnySize, result.Fit);
        Assert.Contains("40x20", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_AbandonsAScriptThatHangs()
    {
        // A preview is a convenience; a script stuck on a network filesystem must not take the
        // browser with it.
        string script = Path.Join(_scratch, "slow.sh");
        File.WriteAllText(script, "#!/bin/sh\nsleep 30\n");
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        ScopeScriptRunner runner = new(script, _scratch)
        {
            Timeout = TimeSpan.FromMilliseconds(300),
        };

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        PreviewResult result = runner.Run("/x/file.txt", new PreviewSize(40, 20), TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(PreviewKind.None, result.Kind);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "the runner should have given up");
    }
}
