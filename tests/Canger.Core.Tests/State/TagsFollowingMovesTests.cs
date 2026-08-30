// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.State;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// Tags following the files they are about when those files move.
/// </summary>
/// <remarks>
/// Cutting a tagged file and pasting it elsewhere stranded the tag on the old path and left the
/// file untagged where it had gone. Ranger rewrites each tag's path as the file lands
/// (<c>core/loader.py:110-124</c>).
/// </remarks>
public sealed class TagsFollowingMovesTests : IDisposable
{
    private readonly string _root;
    private readonly Tags _tags;

    public TagsFollowingMovesTests()
    {
        _root = Path.Join(Path.GetTempPath(), "canger-tagmove-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _tags = new Tags(Path.Join(_root, "tagged"));
    }

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

    /// <summary>Runs a transfer to completion and hands back the queued job.</summary>
    private static QueuedTask Run(CopyJob job)
    {
        TaskQueue queue = new();
        QueuedTask task = queue.Add(job);

        for (int i = 0; i < 100_000 && queue.HasWork; i++)
        {
            queue.Work(TimeSpan.Zero);
        }

        return task;
    }

    private static InMemoryFileSystem Filesystem() =>
        new InMemoryFileSystem()
            .AddDirectory("/home/manuj")
            .AddDirectory("/home/manuj/archive")
            .AddFile("/home/manuj/notes.md", "x");

    [Fact]
    public void ATagFollowsAMovedFile()
    {
        InMemoryFileSystem fs = Filesystem();
        _tags.Add(["/home/manuj/notes.md"]);

        QueuedTask task = Run(new CopyJob(fs, ["/home/manuj/notes.md"], "/home/manuj/archive",
                                          TransferKind.Move));

        FinishedWork.CarryTags([task], _tags);

        Assert.False(_tags.Contains("/home/manuj/notes.md"));
        Assert.True(_tags.Contains("/home/manuj/archive/notes.md"));
    }

    [Fact]
    public void ATagFollowsToWhereTheFileActuallyLandedNotWhereItWasAimed()
    {
        // The reason a transfer has to report its landings. A file pasted beside one of the same
        // name is renamed on arrival, and a tag filed under the name it was aimed at would point
        // at a file nobody moved.
        InMemoryFileSystem fs = Filesystem().AddFile("/home/manuj/archive/notes.md", "already");
        _tags.Add(["/home/manuj/notes.md"]);

        CopyJob job = new(fs, ["/home/manuj/notes.md"], "/home/manuj/archive", TransferKind.Move);
        QueuedTask task = Run(job);

        string landed = Assert.Single(job.Landings).Destination;

        Assert.NotEqual("/home/manuj/archive/notes.md", landed);
        FinishedWork.CarryTags([task], _tags);
        Assert.True(_tags.Contains(landed));
    }

    [Fact]
    public void TagsInsideAMovedFolderFollowItToo()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/manuj/archive")
            .AddFile("/home/manuj/work/report.md", "x");

        _tags.Add(["/home/manuj/work/report.md"]);

        QueuedTask task = Run(new CopyJob(fs, ["/home/manuj/work"], "/home/manuj/archive",
                                          TransferKind.Move));

        FinishedWork.CarryTags([task], _tags);

        Assert.True(_tags.Contains("/home/manuj/archive/work/report.md"));
    }

    [Fact]
    public void ACopyLeavesTheTagOnTheOriginalAndDoesNotInventOneForTheNewFile()
    {
        // The original is still there and still the file the note was about. The new file is a
        // different file nobody has said anything about.
        InMemoryFileSystem fs = Filesystem();
        _tags.Add(["/home/manuj/notes.md"]);

        QueuedTask task = Run(new CopyJob(fs, ["/home/manuj/notes.md"], "/home/manuj/archive",
                                          TransferKind.Copy));

        FinishedWork.CarryTags([task], _tags);

        Assert.True(_tags.Contains("/home/manuj/notes.md"));
        Assert.False(_tags.Contains("/home/manuj/archive/notes.md"));
    }

    [Fact]
    public void UntaggedFilesAreNotGivenTags()
    {
        InMemoryFileSystem fs = Filesystem();

        QueuedTask task = Run(new CopyJob(fs, ["/home/manuj/notes.md"], "/home/manuj/archive",
                                          TransferKind.Move));

        Assert.Equal(0, _tags.Count);
        FinishedWork.CarryTags([task], _tags);
        Assert.Equal(0, _tags.Count);
    }

    [Fact]
    public void ASourceThatCouldNotBeMovedIsNotReportedAsHavingLanded()
    {
        // A tag must not be rewritten to point at somewhere its file never reached.
        InMemoryFileSystem fs = Filesystem();

        CopyJob job = new(fs, ["/home/manuj/missing.md"], "/home/manuj/archive",
                          TransferKind.Move);
        Run(job);

        Assert.Empty(job.Landings);
    }

    [Fact]
    public void EachSourceOfAMultipleMoveIsReportedSeparately()
    {
        InMemoryFileSystem fs = Filesystem().AddFile("/home/manuj/other.md", "y");
        _tags.Add(["/home/manuj/notes.md"]);
        _tags.Add(["/home/manuj/other.md"]);

        QueuedTask task = Run(new CopyJob(fs, ["/home/manuj/notes.md", "/home/manuj/other.md"],
                                          "/home/manuj/archive", TransferKind.Move));

        FinishedWork.CarryTags([task], _tags);

        Assert.True(_tags.Contains("/home/manuj/archive/notes.md"));
        Assert.True(_tags.Contains("/home/manuj/archive/other.md"));
    }
}
