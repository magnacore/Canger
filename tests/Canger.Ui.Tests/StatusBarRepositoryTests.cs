// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Ui;
using Canger.Vcs;

namespace Canger.Ui.Tests;

/// <summary>
/// Which repository the status line describes, and what it says about the hovered entry.
/// </summary>
/// <remarks>
/// Ranger takes the hovered entry's repository when that entry is a directory, and the containing
/// one otherwise (<c>gui/widgets/statusbar.py:199-201</c>). Canger always took the directory the
/// cursor stood in — which in a listing of projects is not a repository at all, so the line said
/// nothing exactly where it is most wanted.
///
/// Against real git repositories: the rule is about which root a path resolves to, and a fake that
/// answered the way the rule expects would test nothing. Skipped where git is not installed.
/// </remarks>
public sealed class StatusBarRepositoryTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-sbvcs-" + Path.GetRandomFileName());

    private readonly VcsService _vcs = new();

    public StatusBarRepositoryTests()
    {
        // A parent that is not itself a repository, holding two that are — the listing where the
        // old behaviour showed nothing.
        Directory.CreateDirectory(_root);
        MakeRepository("alpha", "feature-a");
        MakeRepository("beta", "release-b");
    }

    public void Dispose()
    {
        _vcs.Dispose();

        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*",
                                                             SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.UserWrite);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private void MakeRepository(string name, string branch)
    {
        string path = Path.Join(_root, name);
        Directory.CreateDirectory(path);

        VcsProcess.Run("git", path, "init", "-q", "-b", branch, ".");
        VcsProcess.Run("git", path, "config", "user.email", "test@example.invalid");
        VcsProcess.Run("git", path, "config", "user.name", "Test");
        File.WriteAllText(Path.Join(path, "tracked.txt"), "one\n");
        VcsProcess.Run("git", path, "add", "-A");
        VcsProcess.Run("git", path, "commit", "-qm", "first");
    }

    /// <summary>A tab over the real tree, with the cursor on the named entry.</summary>
    private static Tab TabPointingAt(string directory, string basename)
    {
        Tab tab = new(new DirectoryCache(Canger.Core.FileSystem.LocalFileSystem.Instance),
                      directory);
        tab.Current.Load(TestContext.Current.CancellationToken);

        // The tab's own cursor, not the directory's: `Tab.Selected` reads the former, and the
        // two are separate because one directory can be open in several tabs.
        FsNode? entry = tab.Current.Entries.FirstOrDefault(e => e.Basename == basename);

        Assert.NotNull(entry);
        tab.MoveCursorTo(entry);

        return tab;
    }

    /// <summary>Loads a repository synchronously, so nothing here waits on the worker.</summary>
    private void Load(string path) => _vcs.RepositoryFor(path)?.Refresh();

    [Fact]
    public void HoveringAProjectDescribesThatProject()
    {
        // The reported gap: standing in a listing of projects, moving the cursor must read each
        // project's own branch. The containing directory is not a repository at all.
        Load(Path.Join(_root, "alpha"));

        VcsRepository? shown = Browser.VcsForStatusBar(_vcs, TabPointingAt(_root, "alpha"));

        Assert.NotNull(shown);
        Assert.Equal(Path.Join(_root, "alpha"), shown.Root);
        Assert.Equal("feature-a", shown.Branch);
    }

    [Fact]
    public void MovingToTheNextProjectDescribesTheNextOne()
    {
        Load(Path.Join(_root, "beta"));

        VcsRepository? shown = Browser.VcsForStatusBar(_vcs, TabPointingAt(_root, "beta"));

        Assert.Equal("release-b", shown?.Branch);
    }

    [Fact]
    public void HoveringAFileDescribesTheRepositoryItSitsIn()
    {
        // The other half of ranger's rule: a file answers to its containing directory.
        string alpha = Path.Join(_root, "alpha");
        Load(alpha);

        VcsRepository? shown = Browser.VcsForStatusBar(_vcs, TabPointingAt(alpha, "tracked.txt"));

        Assert.Equal(alpha, shown?.Root);
    }

    [Fact]
    public void SaysNothingOutsideAnyRepository()
    {
        Directory.CreateDirectory(Path.Join(_root, "plain"));

        Assert.Null(Browser.VcsForStatusBar(_vcs, TabPointingAt(_root, "plain")));
    }

    [Fact]
    public void SaysNothingWhenVcsAwareIsOff()
    {
        Assert.Null(Browser.VcsForStatusBar(null, TabPointingAt(_root, "alpha")));
        Assert.Null(Browser.VcsStatusForStatusBar(null, TabPointingAt(_root, "alpha")));
    }

    [Fact]
    public void AProjectRowReportsItsOwnAggregateStatus()
    {
        // A directory that is a repository in its own right answers about itself — the worst
        // thing anywhere inside it — rather than about the directory the cursor stands in.
        File.WriteAllText(Path.Join(_root, "alpha", "loose.txt"), "untracked\n");
        Load(Path.Join(_root, "alpha"));

        Assert.Equal(VcsStatus.Untracked,
                     Browser.VcsStatusForStatusBar(_vcs, TabPointingAt(_root, "alpha")));
    }

    [Fact]
    public void AFileReportsItsOwnStatusFromTheRepositoryAroundIt()
    {
        string alpha = Path.Join(_root, "alpha");
        File.WriteAllText(Path.Join(alpha, "tracked.txt"), "one\ntwo\n");
        Load(alpha);

        Assert.Equal(VcsStatus.Changed,
                     Browser.VcsStatusForStatusBar(_vcs, TabPointingAt(alpha, "tracked.txt")));
    }
}
