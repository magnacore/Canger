// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Guards on the commands that can destroy something.
/// </summary>
public class DestructiveGuardTests
{
    private static FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/one.txt", "a")
            .AddFile("/home/two.txt", "b")
            .AddDirectory("/home/sub");

        return new FakeFileManager(fs, "/home");
    }

    [Fact]
    public void Delete_RefusesAnArgumentRatherThanDeletingTheSelectionInstead()
    {
        // `dD` opens the console on `:delete `, which invites typing a name. The name used to be
        // discarded and the selection removed instead — with the default setting, a single
        // selection goes without any prompt at all.
        FakeFileManager manager = Manager();

        manager.Execute("delete one.txt");

        Assert.True(manager.FileSystem.Exists("/home/one.txt"));
        Assert.True(manager.FileSystem.Exists("/home/two.txt"));
        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void Trash_RefusesAnArgumentTheSameWay()
    {
        FakeFileManager manager = Manager();

        manager.Execute("trash one.txt");

        Assert.True(manager.FileSystem.Exists("/home/one.txt"));
        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void Delete_StillWorksWithNoArgument()
    {
        // The refusal must not stop the command doing its job.
        FakeFileManager manager = Manager();
        string under = manager.CurrentTab.Selected!.Path;

        manager.Execute("delete");

        Assert.False(manager.FileSystem.Exists(under) ||
                     manager.FileSystem.DirectoryExists(under));
    }

    [Fact]
    public void Edit_PassesDoubleDashSoAFilenameCannotBecomeAnOption()
    {
        // Quoting stops the shell, not the editor. A file called `+!rm -rf ~/Documents` is read
        // by vim as a command to run at startup.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/+!rm -rf x", "x");
        FakeFileManager manager = new(fs, "/home");

        manager.Execute("edit");

        (string command, _) = Assert.Single(manager.LaunchedPrograms);
        Assert.Contains(" -- ", command, StringComparison.Ordinal);
        Assert.EndsWith("'+!rm -rf x'", command, StringComparison.Ordinal);
    }
}

/// <summary>
/// Whether a destination lies inside what is being put there.
/// </summary>
/// <remarks>
/// The guard lived inside <c>CopyJob</c>, so the three linking pastes — which recurse just as
/// happily — had none. It also compared paths as typed, which misses a destination that reaches
/// the source by another name.
/// </remarks>
public class PathRelationTests
{
    [Fact]
    public void ADirectoryIsInsideItself()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/src");

        Assert.True(PathRelation.IsSameOrInside(fs, "/src", "/src"));
    }

    [Fact]
    public void ASubdirectoryIsInside()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/src/inner");

        Assert.True(PathRelation.IsSameOrInside(fs, "/src/inner", "/src"));
    }

    [Fact]
    public void ASiblingIsNot()
    {
        // The separator matters: without it `/srcx` would count as inside `/src`.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/srcx");

        Assert.False(PathRelation.IsSameOrInside(fs, "/srcx", "/src"));
    }

    [Fact]
    public void AnUnrelatedDirectoryIsNot()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/dest");

        Assert.False(PathRelation.IsSameOrInside(fs, "/dest", "/src"));
    }
}
