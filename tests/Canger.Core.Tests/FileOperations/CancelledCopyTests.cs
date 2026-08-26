// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// What is left behind when a copy is abandoned part-way.
/// </summary>
/// <remarks>
/// A cancelled copy used to leave the bytes it had managed so far at the destination, under the
/// right name, with nothing to say it was a fragment. <c>cp</c> does the same on Ctrl-C, but a
/// file manager with a cancel key in its task view is a different proposition: the user pressed
/// something that says stop, and is entitled to assume nothing was left behind.
///
/// Cancelling from inside the progress callback lands mid-file every time, which is the only way
/// to test this reliably — on a machine where a copy runs at gigabytes a second, racing it with a
/// timer proves nothing.
/// </remarks>
public sealed class CancelledCopyTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-cancel-" + Path.GetRandomFileName());

    // The buffered path, deliberately: a reflink or a kernel copy moves the whole file in one
    // call and never reports progress, so there is no moment mid-file at which to cancel. What
    // is being tested is what happens when a copy *is* interrupted, which needs the path that
    // can be.
    private readonly CopyEngine _engine =
        new(LocalFileSystem.Instance) { AllowReflink = false, AllowKernelCopy = false };

    public CancelledCopyTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Source(int megabytes = 8)
    {
        string path = Path.Join(_root, "source.bin");
        File.WriteAllBytes(path, new byte[megabytes * 1024 * 1024]);
        return path;
    }

    [Fact]
    public void CancellingMidFileLeavesNothingBehind()
    {
        string source = Source();
        string destination = Path.Join(_root, "copy.bin");

        using CancellationTokenSource cancellation = new();

        Assert.Throws<OperationCanceledException>(() =>
            _engine.CopyFile(source, destination, _ => cancellation.Cancel(), cancellation.Token));

        Assert.False(File.Exists(destination),
                     "a cancelled copy must not leave a fragment under the destination's name");
    }

    [Fact]
    public void CancellingDoesNotRemoveAFileThatWasAlreadyThere()
    {
        // `po` overwrites deliberately. Opening for writing has already truncated it by the time
        // any of this runs, and deleting it as well would turn a damaged file into a missing one.
        string source = Source();
        string destination = Path.Join(_root, "copy.bin");
        File.WriteAllText(destination, "something the user already had");

        using CancellationTokenSource cancellation = new();

        Assert.Throws<OperationCanceledException>(() =>
            _engine.CopyFile(source, destination, _ => cancellation.Cancel(), cancellation.Token));

        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void AFinishedCopyIsKept()
    {
        string source = Source(megabytes: 1);
        string destination = Path.Join(_root, "copy.bin");

        FileCopyResult result = _engine.CopyFile(
            source, destination, null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new FileInfo(source).Length, new FileInfo(destination).Length);
    }

    [Fact]
    public void CancellingBeforeAnythingIsWrittenLeavesNothing()
    {
        string source = Source(megabytes: 1);
        string destination = Path.Join(_root, "copy.bin");

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        try
        {
            _engine.CopyFile(source, destination, null, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Whether it throws before or after opening the destination is an implementation
            // detail; that nothing is left behind is not.
        }

        Assert.False(File.Exists(destination));
    }
}
