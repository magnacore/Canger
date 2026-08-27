// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.FileSystem;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// Copying a file a piece at a time, and what is left behind when it does not finish.
/// </summary>
/// <remarks>
/// The copy used to be one indivisible call, so a half-copied file was not a state anything could
/// observe: it either finished or threw, and the same <c>catch</c> removed the remains. Pausing
/// between pieces makes it reachable — the caller can simply stop asking — and that is the state
/// these exist to pin. Nothing may survive a copy that did not finish.
/// </remarks>
public sealed class ResumableCopyTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-resume-" + Path.GetRandomFileName());

    public ResumableCopyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    private string At(string name) => Path.Join(_root, name);

    /// <summary>
    /// An engine that moves one block per piece.
    /// </summary>
    /// <remarks>
    /// The budget is time, so on a fast disc a whole test file moves inside one piece and the
    /// pausing is never exercised — which is right for the program and useless for a test. Zero
    /// leaves one block per piece, because a piece always moves something.
    /// </remarks>
    private static CopyEngine Stepwise() =>
        new(new LocalFileSystem())
        {
            AllowReflink = false,
            StepBudget = TimeSpan.Zero,
            KernelRun = 64 * 1024,
        };

    /// <summary>A file of a size that takes several pieces to move.</summary>
    private string Source(int bytes = 6 * 1024 * 1024)
    {
        string path = At("source.bin");
        byte[] data = new byte[bytes];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void ACopyRunToTheEndIsIdenticalToTheSource()
    {
        // The thing that must not have changed.
        CopyEngine engine = new(new LocalFileSystem());
        string source = Source();

        FileCopyResult result = engine.CopyFile(source, At("copy.bin"), null,
                                                TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(At("copy.bin")));
    }

    [Fact]
    public void ItReportsMoreThanOnePieceForALargeFile()
    {
        // If the whole file still moved in one piece the freeze would be unchanged, and every
        // other test here would be passing for the wrong reason.
        CopyEngine engine = Stepwise();
        string source = Source();

        int pieces = 0;
        foreach (FileCopyResult? step in engine.CopyFileSteps(
                     source, At("copy.bin"), null, TestContext.Current.CancellationToken))
        {
            if (step is null)
            {
                pieces++;
            }
        }

        Assert.True(pieces > 0, "the copy never paused, so the interface would still freeze");
    }

    [Fact]
    public void AbandoningItPartWayLeavesNothingBehind()
    {
        // The new state. Stopping the enumeration is how a caller gives up — and a half-written
        // file left at the destination would look to everyone like a copy of the source.
        CopyEngine engine = Stepwise();
        string source = Source();
        string destination = At("abandoned.bin");

        using (IEnumerator<FileCopyResult?> steps = engine
                   .CopyFileSteps(source, destination, null, TestContext.Current.CancellationToken)
                   .GetEnumerator())
        {
            Assert.True(steps.MoveNext());
            Assert.Null(steps.Current);   // still working, so something is part-written
        }

        Assert.False(File.Exists(destination), "a partly written destination was left behind");
    }

    [Fact]
    public void CancellingItPartWayLeavesNothingBehind()
    {
        CopyEngine engine = Stepwise();
        string source = Source();
        string destination = At("cancelled.bin");

        using CancellationTokenSource cancellation = new();

        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (FileCopyResult? step in engine.CopyFileSteps(source, destination, null,
                                                                 cancellation.Token))
            {
                cancellation.Cancel();
            }
        });

        Assert.False(File.Exists(destination), "a cancelled copy left its remains");
    }

    [Fact]
    public void AnExistingDestinationIsNotRemovedWhenTheCopyIsAbandoned()
    {
        // The other half of the rule, and the one that would lose data if it were wrong: what was
        // already there is not ours to delete. `destinationIsOurs` is taken before anything is
        // opened, for exactly this.
        CopyEngine engine = Stepwise();
        string source = Source();
        string destination = At("existing.bin");
        File.WriteAllText(destination, "was already here");

        using (IEnumerator<FileCopyResult?> steps = engine
                   .CopyFileSteps(source, destination, null, TestContext.Current.CancellationToken)
                   .GetEnumerator())
        {
            Assert.True(steps.MoveNext());
        }

        Assert.True(File.Exists(destination), "a destination that was already there was removed");
    }

    [Fact]
    public void ProgressIsReportedAsItGoes()
    {
        // What the bar is drawn from. Reported per piece rather than once at the end, or the bar
        // would jump from nothing to done.
        CopyEngine engine = Stepwise();
        string source = Source();

        List<long> reports = [];
        engine.CopyFile(source, At("copy.bin"), reports.Add,
                        TestContext.Current.CancellationToken);

        Assert.True(reports.Count > 1, "progress arrived in one lump");
        Assert.Equal(new FileInfo(source).Length, reports.Sum());
    }

    [Fact]
    public void TheSameFileGuardStillRefusesBeforeAnythingIsOpened()
    {
        // Carried over from the audit: a copy onto a hard link of the source must not truncate it.
        LocalFileSystem fs = new();
        CopyEngine engine = new(fs);
        string source = Source(1024);
        string link = At("hardlink.bin");
        fs.CreateHardLink(link, source);

        FileCopyResult result = engine.CopyFile(source, link, null,
                                                TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(1024, new FileInfo(source).Length);
    }
}
