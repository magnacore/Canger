// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;

namespace Canger.Core.Tests.State;

/// <summary>
/// The copy buffer on disk, which is how one running Canger hands a copy to another.
/// </summary>
/// <remarks>
/// Against a real temporary directory, because the whole point is what a second process would
/// find there — atomic replacement, a symbolic link followed rather than broken, and a failed
/// read that changes nothing.
/// </remarks>
public sealed class SharedCopyBufferTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-scb-" + Path.GetRandomFileName());

    private string File_ => Path.Join(_root, "copybuffer");

    public SharedCopyBufferTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_root))
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void RoundTripsACopy()
    {
        SharedCopyBuffer buffer = new(File_);
        buffer.Write(["/a/one.txt", "/a/two.txt"], cut: false);

        SharedCopy read = Assert.NotNull(new SharedCopyBuffer(File_).Read());

        Assert.Equal(["/a/one.txt", "/a/two.txt"], read.Paths);
        Assert.False(read.Cut);
    }

    [Fact]
    public void RoundTripsACut()
    {
        new SharedCopyBuffer(File_).Write(["/a/one.txt"], cut: true);

        Assert.True(Assert.NotNull(new SharedCopyBuffer(File_).Read()).Cut);
    }

    [Fact]
    public void KeepsTheOrderTheFilesWereChosenIn()
    {
        // A paste happens in the order the user watched them gathered.
        new SharedCopyBuffer(File_).Write(["/z.txt", "/a.txt", "/m.txt"], cut: false);

        Assert.Equal(["/z.txt", "/a.txt", "/m.txt"],
                     Assert.NotNull(new SharedCopyBuffer(File_).Read()).Paths);
    }

    [Fact]
    public void AMissingFileIsAnEmptyBufferRatherThanAFailure()
    {
        // That is what a completed move leaves behind, and it must not read as an error.
        SharedCopy read = Assert.NotNull(new SharedCopyBuffer(File_).Read());

        Assert.Empty(read.Paths);
        Assert.False(read.Cut);
    }

    [Fact]
    public void AnUnreadableFileReportsNothingRatherThanEmptiness()
    {
        // The distinction `Tags.CouldNotBeRead` exists for: answering "empty" on a transient
        // error would quietly disarm a pending cut in every window.
        File.WriteAllText(File_, "cut\n/a/one.txt\n");
        File.SetUnixFileMode(File_, UnixFileMode.None);

        // Root reads anything, so there would be nothing to test there.
        Assert.SkipWhen(CanStillBeRead(), "the file is readable anyway; probably running as root");

        Assert.Null(new SharedCopyBuffer(File_).Read());
    }

    private bool CanStillBeRead()
    {
        try
        {
            _ = File.ReadAllText(File_);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void ClearingEmptiesTheBuffer()
    {
        SharedCopyBuffer buffer = new(File_);
        buffer.Write(["/a/one.txt"], cut: true);
        buffer.Clear();

        SharedCopy read = Assert.NotNull(new SharedCopyBuffer(File_).Read());

        Assert.Empty(read.Paths);
        Assert.False(read.Cut);
    }

    [Fact]
    public void WritesNothingWhenNotPersistent()
    {
        // What `--clean` relies on.
        new SharedCopyBuffer(File_) { Persistent = false }.Write(["/a/one.txt"], cut: false);

        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void ReplacesThroughASymbolicLinkRatherThanBreakingIt()
    {
        // Keeping this file as a link into a dotfiles repository is a common arrangement, and
        // `rename(2)` replaces the link rather than what it points at.
        string real = Path.Join(_root, "real-copybuffer");
        File.WriteAllText(real, "copy\n");
        File.CreateSymbolicLink(File_, real);

        new SharedCopyBuffer(File_).Write(["/a/one.txt"], cut: true);

        Assert.NotNull(new FileInfo(File_).LinkTarget);
        Assert.Contains("/a/one.txt", File.ReadAllText(real), StringComparison.Ordinal);
    }

    [Fact]
    public void NoticesAnotherInstancesWriteButNotItsOwn()
    {
        // What the browser asks on every draw. An instance re-reading its own write would rebuild
        // everything it already has on every copy; missing another's would leave the two windows
        // disagreeing about what is dimmed.
        //
        // This is why the comparison is the file's contents and not its modification time:
        // measured here, five rewrites in quick succession left the mtime identical every time,
        // so a timestamp check failed exactly the case it existed for.
        SharedCopyBuffer mine = new(File_);
        SharedCopyBuffer theirs = new(File_);

        mine.Write(["/a/one.txt"], cut: false);
        Assert.Null(mine.ReadIfChanged());
        Assert.NotNull(theirs.ReadIfChanged());
        Assert.Null(theirs.ReadIfChanged());

        // Immediately afterwards, well inside one tick of the filesystem clock.
        theirs.Write(["/a/two.txt"], cut: true);

        SharedCopy taken = Assert.NotNull(mine.ReadIfChanged());
        Assert.Equal(["/a/two.txt"], taken.Paths);
        Assert.True(taken.Cut);
    }
}
