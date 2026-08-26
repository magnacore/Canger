// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// A move removes the source only when everything under it arrived.
/// </summary>
/// <remarks>
/// <para>
/// Errors are deliberately collected rather than thrown, so one unreadable file does not abandon
/// the rest of a transfer. A move that then deleted the source regardless destroyed precisely the
/// files that had failed to copy — a destination out of space, a name the filesystem will not
/// accept, a file past FAT32's four gigabytes, and the originals were gone with only a line in
/// the task view to say so.
/// </para>
/// <para>
/// Ranger reaches the same rule from the other side: <c>copytree</c> raises when its error list is
/// non-empty, which puts <c>rmtree(src)</c> out of reach (<c>ext/shutil_generatorized.py:277-279</c>,
/// <c>:318-321</c>).
/// </para>
/// </remarks>
public class FailedMoveTests
{
    private static void Run(CopyJob job)
    {
        IEnumerator<Unit> steps = job.Steps();
        for (int i = 0; i < 100_000 && steps.MoveNext(); i++)
        {
        }
    }

    private static InMemoryFileSystem Tree() =>
        new InMemoryFileSystem()
            .AddFile("/src/keep.txt", "first")
            .AddFile("/src/doomed.txt", "second")
            .AddFile("/src/deep/buried.txt", "third")
            .AddDirectory("/dest");

    [Fact]
    public void Move_KeepsAFileThatCouldNotBeWritten()
    {
        // The headline. The destination refuses one file — out of space, a name exFAT will not
        // take, a file past FAT32's four gigabytes — and that file must still be at the source.
        InMemoryFileSystem fs = Tree().FailWritesTo("/dest/src/doomed.txt");

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.True(fs.Exists("/src/doomed.txt"),
                    "the file that failed to copy must not have been deleted");
        Assert.False(fs.Exists("/dest/src/doomed.txt"));
        Assert.NotEmpty(job.Errors);
    }

    [Fact]
    public void Move_KeepsTheSourceDirectoryItselfWhenAnythingFailed()
    {
        // Before the fix this was `DeleteRecursive`, which took the failed file with it.
        InMemoryFileSystem fs = Tree().FailWritesTo("/dest/src/doomed.txt");

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.True(fs.DirectoryExists("/src"));
    }

    [Fact]
    public void Move_KeepsEveryAncestorWhenTheFailureIsDeep()
    {
        // A failure three levels down has to protect the directories above it too, or the
        // outermost delete takes the lot.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/src/a/b/c/doomed.txt", "x")
            .AddFile("/src/a/b/c/fine.txt", "y")
            .AddDirectory("/dest")
            .FailWritesTo("/dest/src/a/b/c/doomed.txt");

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.True(fs.Exists("/src/a/b/c/doomed.txt"));
        Assert.True(fs.DirectoryExists("/src/a/b/c"));
        Assert.True(fs.DirectoryExists("/src/a"));
        Assert.True(fs.DirectoryExists("/src"));
    }

    [Fact]
    public void Move_StillRemovesTheSourceWhenEverythingArrived()
    {
        // The fix must not simply stop moves from working.
        InMemoryFileSystem fs = Tree();

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.False(fs.DirectoryExists("/src"), "a clean move still removes the source");
        Assert.True(fs.Exists("/dest/src/keep.txt"));
        Assert.True(fs.Exists("/dest/src/deep/buried.txt"));
        Assert.Empty(job.Errors);
    }

    [Fact]
    public void Move_CarriesOnPastAFailureRatherThanStopping()
    {
        // The collecting behaviour is deliberate and stays: everything that *can* be moved still
        // is, each file ending up in exactly one place.
        InMemoryFileSystem fs = Tree().FailWritesTo("/dest/src/doomed.txt");

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.True(fs.Exists("/dest/src/keep.txt"));
        Assert.False(fs.Exists("/src/keep.txt"));
        Assert.True(fs.Exists("/dest/src/deep/buried.txt"));
    }

    [Fact]
    public void Move_SaysWhyTheSourceWasKept()
    {
        // A silent refusal to finish a move is its own kind of surprise.
        InMemoryFileSystem fs = Tree().FailWritesTo("/dest/src/doomed.txt");

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.Contains(job.Errors, e => e.Contains("kept", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_OfASingleFileThatFailsKeepsIt()
    {
        // Already correct before the fix — pinned so it stays that way.
        InMemoryFileSystem fs = Tree().FailWritesTo("/dest/doomed.txt");

        CopyJob job = new(fs, ["/src/doomed.txt"], "/dest", TransferKind.Move);
        Run(job);

        Assert.True(fs.Exists("/src/doomed.txt"));
    }

    [Fact]
    public void Move_LeavesAFailedSubdirectoryEntirelyAlone()
    {
        // A subdirectory that cannot even be created at the destination: nothing under it was
        // copied, so nothing under it may be removed.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/src/inner/one.txt", "a")
            .AddFile("/src/inner/two.txt", "b")
            .AddFile("/src/outer.txt", "c")
            .AddDirectory("/dest")
            .MakeInaccessible("/src/inner");

        CopyJob job = new(fs, ["/src"], "/dest", TransferKind.Move);
        Run(job);

        Assert.True(fs.Exists("/src/inner/one.txt"));
        Assert.True(fs.Exists("/src/inner/two.txt"));
        Assert.True(fs.DirectoryExists("/src"));
    }
}

/// <summary>
/// One selected file must not destroy another.
/// </summary>
/// <remarks>
/// Overwriting is a policy about what is already at the destination, which is what <c>po</c> asks
/// for. It is not permission for two of the user's own selected files to collide: a flattened
/// listing, or a copy buffer built across directories, can hold two files with the same basename,
/// and both resolve to the same target. The second overwrote the first and, on a move, then
/// deleted its own source — leaving one of the two nowhere. Ranger has the same hole.
/// </remarks>
public class PasteCollisionTests
{
    private static void Run(CopyJob job)
    {
        IEnumerator<Unit> steps = job.Steps();
        for (int i = 0; i < 100_000 && steps.MoveNext(); i++)
        {
        }
    }

    private static InMemoryFileSystem TwoOfTheSameName() =>
        new InMemoryFileSystem()
            .AddFile("/src/one/a.txt", "from one")
            .AddFile("/src/two/a.txt", "from two")
            .AddDirectory("/dest");

    [Fact]
    public void Overwrite_KeepsBothWhenTwoSourcesShareAName()
    {
        InMemoryFileSystem fs = TwoOfTheSameName();

        CopyJob job = new(fs, ["/src/one/a.txt", "/src/two/a.txt"], "/dest",
                          TransferKind.Copy, ClashPolicy.Overwrite);
        Run(job);

        Assert.True(fs.Exists("/dest/a.txt"));
        Assert.True(fs.Exists("/dest/a.txt_0"), "the second must not have replaced the first");
    }

    [Fact]
    public void Overwrite_MovingTwoOfTheSameNameLosesNeither()
    {
        // The damaging shape: the second overwrote the first and then deleted its own source.
        InMemoryFileSystem fs = TwoOfTheSameName();

        CopyJob job = new(fs, ["/src/one/a.txt", "/src/two/a.txt"], "/dest",
                          TransferKind.Move, ClashPolicy.Overwrite);
        Run(job);

        List<string> contents =
        [
            .. new[] { "/dest/a.txt", "/dest/a.txt_0" }
                .Where(fs.Exists)
                .Select(p => new StreamReader(fs.OpenRead(p)).ReadToEnd())
        ];

        Assert.Contains("from one", contents);
        Assert.Contains("from two", contents);
    }

    [Fact]
    public void Overwrite_StillReplacesWhatWasAlreadyThere()
    {
        // The policy must still do what it was asked to do.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/src/a.txt", "new")
            .AddFile("/dest/a.txt", "old");

        CopyJob job = new(fs, ["/src/a.txt"], "/dest",
                          TransferKind.Copy, ClashPolicy.Overwrite);
        Run(job);

        Assert.False(fs.Exists("/dest/a.txt_0"), "an existing file is still replaced, not renamed");
        Assert.Equal("new", new StreamReader(fs.OpenRead("/dest/a.txt")).ReadToEnd());
    }
}
