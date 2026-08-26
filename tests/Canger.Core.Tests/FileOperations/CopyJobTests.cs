// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

public class CopyJobTests
{
    /// <summary>Runs a job to completion, one step at a time.</summary>
    private static void Run(CopyJob job)
    {
        IEnumerator<Unit> steps = job.Steps();
        for (int i = 0; i < 100_000 && steps.MoveNext(); i++)
        {
            // Stepping rather than looping to completion is what the task queue does, so the
            // test exercises the same path.
        }
    }

    private static InMemoryFileSystem Tree() =>
        new InMemoryFileSystem()
            .AddFile("/src/one.txt", "first")
            .AddFile("/src/two.txt", "second")
            .AddFile("/src/sub/three.txt", "third")
            .AddDirectory("/dest");

    [Fact]
    public void Copy_PlacesFilesInTheDestination()
    {
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src/one.txt"], "/dest");

        Run(job);

        Assert.True(fs.Exists("/dest/one.txt"));
        Assert.True(fs.Exists("/src/one.txt"));
        Assert.Empty(job.Errors);
    }

    [Fact]
    public void Copy_CopiesAWholeDirectoryTree()
    {
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src"], "/dest");

        Run(job);

        Assert.True(fs.Exists("/dest/src/one.txt"));
        Assert.True(fs.Exists("/dest/src/sub/three.txt"));
    }

    [Fact]
    public void Move_RemovesTheOriginals()
    {
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src/one.txt"], "/dest", TransferKind.Move);

        Run(job);

        Assert.True(fs.Exists("/dest/one.txt"));
        Assert.False(fs.Exists("/src/one.txt"));
    }

    [Fact]
    public void Move_WithinOneFilesystemRenamesRatherThanCopying()
    {
        // No data moves at all, however large the file, which is why it is counted separately.
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src/one.txt"], "/dest", TransferKind.Move);

        Run(job);

        Assert.Equal(1, job.Progress.RenamedFiles);
        Assert.Equal(0, job.Progress.TransferredBytes);
    }

    [Fact]
    public void Copy_RenamesRatherThanOverwritingAnExistingFile()
    {
        // Silently destroying what is already there would be the worst possible default.
        InMemoryFileSystem fs = Tree().AddFile("/dest/one.txt", "do not lose me");
        CopyJob job = new(fs, ["/src/one.txt"], "/dest");

        Run(job);

        Assert.True(fs.Exists("/dest/one_0.txt") || fs.Exists("/dest/one.txt_0"));
        using StreamReader reader = new(fs.OpenRead("/dest/one.txt"));
        Assert.Equal("do not lose me", reader.ReadToEnd());
    }

    [Fact]
    public void Copy_CanKeepTheExtensionWhenRenaming()
    {
        // report_0.pdf still opens in the right program; report.pdf_0 does not.
        InMemoryFileSystem fs = Tree().AddFile("/dest/one.txt", "existing");
        CopyJob job = new(fs, ["/src/one.txt"], "/dest", TransferKind.Copy,
                          ClashPolicy.RenameKeepingExtension);

        Run(job);

        Assert.True(fs.Exists("/dest/one_0.txt"));
    }

    [Fact]
    public void Copy_CanBeAskedToOverwrite()
    {
        InMemoryFileSystem fs = Tree().AddFile("/dest/one.txt", "replace me");
        CopyJob job = new(fs, ["/src/one.txt"], "/dest", TransferKind.Copy, ClashPolicy.Overwrite);

        Run(job);

        using StreamReader reader = new(fs.OpenRead("/dest/one.txt"));
        Assert.Equal("first", reader.ReadToEnd());
    }

    [Fact]
    public void Copy_MeasuresTheTotalBeforeTransferring()
    {
        // The percentage and the estimate are meaningless until the total is known.
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src"], "/dest");

        Run(job);

        Assert.True(job.Progress.TotalBytes > 0);
        Assert.Equal(1, job.Progress.Fraction, 3);
    }

    [Fact]
    public void Copy_ReportsFailuresAndCarriesOn()
    {
        // One unreadable file must not abandon everything else.
        InMemoryFileSystem fs = Tree().MakeInaccessible("/src/two.txt");
        CopyJob job = new(fs, ["/src/one.txt", "/src/two.txt", "/src/sub"], "/dest");

        Run(job);

        Assert.True(fs.Exists("/dest/one.txt"));
        Assert.True(fs.Exists("/dest/sub/three.txt"));
        Assert.Single(job.Errors);
    }

    [Fact]
    public void Copy_SkipsAnUnreadableSubdirectoryWithoutAbandoningTheRest()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/src/fine.txt", "x")
            .AddDirectory("/src/locked")
            .AddDirectory("/dest")
            .MakeInaccessible("/src/locked");

        CopyJob job = new(fs, ["/src"], "/dest");
        Run(job);

        Assert.True(fs.Exists("/dest/src/fine.txt"));
        Assert.NotEmpty(job.Errors);
    }

    [Fact]
    public void Copy_RunsThroughTheTaskQueueAStepAtATime()
    {
        // The queue is what keeps the interface responsive during a large transfer.
        InMemoryFileSystem fs = Tree();
        TaskQueue queue = new();
        CopyJob job = new(fs, ["/src"], "/dest");

        queue.Add(job);

        for (int i = 0; i < 10_000 && queue.HasWork; i++)
        {
            queue.Work(TimeSpan.Zero);
        }

        Assert.True(job.IsFinished);
        Assert.True(fs.Exists("/dest/src/one.txt"));
    }

    [Fact]
    public void Copy_CanBeCancelledPartWay()
    {
        InMemoryFileSystem fs = Tree();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Copy, ClashPolicy.Rename,
                          cancellation.Token);

        Assert.Throws<OperationCanceledException>(() => Run(job));
    }

    [Fact]
    public void Description_ReadsAsSomethingTheTaskViewCanShow()
    {
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src/one.txt"], "/dest");

        Run(job);

        Assert.Contains("copying", job.Description, StringComparison.Ordinal);
        Assert.Contains("one.txt", job.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_NamesTheCountWhenThereAreSeveral()
    {
        InMemoryFileSystem fs = Tree();
        CopyJob job = new(fs, ["/src/one.txt", "/src/two.txt"], "/dest");

        Assert.Contains("2 items", job.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_RefusesToPasteADirectoryIntoItself()
    {
        // This destroyed the directory and everything under it: the move renamed it beneath a
        // path that was about to stop existing, and then removed the source.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/src/dest/keep.txt", "x");
        CopyJob job = new(fs, ["/src/dest"], "/src/dest", TransferKind.Move);

        Run(job);

        Assert.True(fs.Exists("/src/dest/keep.txt"));
        Assert.NotEmpty(job.Errors);
    }

    [Fact]
    public void Copy_RefusesToPasteADirectoryIntoItself()
    {
        // A copy would recurse into what it was writing.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/src/dest/keep.txt", "x");
        CopyJob job = new(fs, ["/src/dest"], "/src/dest");

        Run(job);

        Assert.True(fs.Exists("/src/dest/keep.txt"));
        Assert.NotEmpty(job.Errors);
    }

    [Fact]
    public void Move_RefusesToPasteADirectoryIntoSomethingBeneathIt()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/src/keep.txt", "x")
            .AddDirectory("/src/inner");

        CopyJob job = new(fs, ["/src"], "/src/inner", TransferKind.Move);

        Run(job);

        Assert.True(fs.Exists("/src/keep.txt"));
        Assert.NotEmpty(job.Errors);
    }

    [Fact]
    public void Refusal_DoesNotMistakeASiblingForAChild()
    {
        // "/srcx" starts with "/src" but is not inside it; only the separator distinguishes them.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/src/keep.txt", "x")
            .AddDirectory("/srcx");

        CopyJob job = new(fs, ["/src"], "/srcx", TransferKind.Move);

        Run(job);

        Assert.Empty(job.Errors);
        Assert.True(fs.Exists("/srcx/src/keep.txt"));
    }

    [Fact]
    public void Move_RefusesAFileThatIsAlreadyInTheDestination()
    {
        // Renaming a file onto itself and then deleting the source loses it.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/src/one.txt", "first");
        CopyJob job = new(fs, ["/src/one.txt"], "/src", TransferKind.Move);

        Run(job);

        Assert.True(fs.Exists("/src/one.txt"));
        Assert.NotEmpty(job.Errors);
    }

    [Fact]
    public void Copy_StillDuplicatesAFileIntoItsOwnDirectory()
    {
        // Unlike a move this is meaningful: the clash policy gives the copy a new name.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/src/one.txt", "first");
        CopyJob job = new(fs, ["/src/one.txt"], "/src");

        Run(job);

        Assert.Empty(job.Errors);
        Assert.True(fs.Exists("/src/one.txt"));
        Assert.True(fs.Exists("/src/one.txt_0"));
    }
}
