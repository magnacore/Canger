// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Vcs.Backends;

namespace Canger.Vcs.Tests;

/// <summary>
/// The Subversion backend, against a real working copy.
/// </summary>
/// <remarks>
/// Against real svn, because the backend's whole job is to parse what the program prints, and a
/// fake producing what the parser expects would test nothing. Skipped where svn is not installed.
/// </remarks>
public sealed class SubversionBackendTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-svn-" + Path.GetRandomFileName());

    private readonly SubversionBackend _svn = new();

    private string Repository => Path.Join(_root, "repo");

    private string WorkingCopy => Path.Join(_root, "wc");

    private static bool Installed =>
        Environment.GetEnvironmentVariable("PATH")?
                   .Split(Path.PathSeparator)
                   .Any(d => d.Length > 0 && File.Exists(Path.Join(d, "svn"))) == true;

    public SubversionBackendTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
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

    /// <summary>Builds a working copy holding a real, unresolved text conflict.</summary>
    /// <remarks>
    /// A conflict is what makes <c>svn status</c> print its trailing <c>Summary of conflicts:</c>
    /// block, which is the whole point of these tests: without one the defect cannot appear.
    /// </remarks>
    private void BuildConflictedWorkingCopy()
    {
        VcsProcess.Run("svnadmin", _root, "create", Repository);
        string url = "file://" + Repository;

        VcsProcess.Run("svn", _root, "-q", "checkout", url, WorkingCopy);
        File.WriteAllText(Path.Join(WorkingCopy, "conflict.txt"), "base\n");
        File.WriteAllText(Path.Join(WorkingCopy, "changed.txt"), "base\n");
        VcsProcess.Run("svn", WorkingCopy, "-q", "add", "conflict.txt", "changed.txt");
        VcsProcess.Run("svn", WorkingCopy, "-q", "commit", "-m", "base");

        // A second working copy commits over the same line, so updating leaves a conflict.
        string other = Path.Join(_root, "other");
        VcsProcess.Run("svn", _root, "-q", "checkout", url, other);
        File.WriteAllText(Path.Join(other, "conflict.txt"), "theirs\n");
        VcsProcess.Run("svn", other, "-q", "commit", "-m", "theirs");

        File.WriteAllText(Path.Join(WorkingCopy, "conflict.txt"), "mine\n");
        File.WriteAllText(Path.Join(WorkingCopy, "changed.txt"), "base\nedited\n");
        File.WriteAllText(Path.Join(WorkingCopy, "untracked.txt"), "free\n");
        VcsProcess.Run("svn", WorkingCopy, "-q", "update", "--accept", "postpone");
    }

    [Fact]
    public void ReadsNoStatusFromTheTrailingSummaryBlock()
    {
        // The defect, which ranger has too (`ext/vcs/svn.py:100-116`): `svn status` ends with
        //
        //     Summary of conflicts:
        //       Text conflicts: 1
        //
        // and reading column eight onwards of the first line gives the "path" `of conflicts:`.
        Assert.SkipUnless(Installed, "svn is not installed");
        BuildConflictedWorkingCopy();

        IReadOnlyDictionary<string, VcsStatus> statuses = _svn.SubpathStatuses(WorkingCopy);

        Assert.DoesNotContain(statuses.Keys,
                              key => key.Contains("conflicts:", StringComparison.Ordinal));
        Assert.All(statuses.Keys,
                   key => Assert.True(File.Exists(Path.Join(WorkingCopy, key)) ||
                                      Directory.Exists(Path.Join(WorkingCopy, key)),
                                      $"'{key}' is not a path in the working copy"));
    }

    [Fact]
    public void StillReadsEveryRealStatus()
    {
        // Rejecting the summary must not reject anything real with it.
        Assert.SkipUnless(Installed, "svn is not installed");
        BuildConflictedWorkingCopy();

        IReadOnlyDictionary<string, VcsStatus> statuses = _svn.SubpathStatuses(WorkingCopy);

        Assert.Equal(VcsStatus.Conflict, statuses["conflict.txt"]);
        Assert.Equal(VcsStatus.Changed, statuses["changed.txt"]);
        Assert.Equal(VcsStatus.Untracked, statuses["untracked.txt"]);
    }

    [Fact]
    public void TheRootIsConflictedWithoutBorrowingUnknown()
    {
        // The summary line also put `unknown` into the root's set of statuses. Conflict outranks
        // it, so the answer was right by luck rather than by reading the working copy correctly.
        Assert.SkipUnless(Installed, "svn is not installed");
        BuildConflictedWorkingCopy();

        Assert.Equal(VcsStatus.Conflict, _svn.RootStatus(WorkingCopy));
    }
}
