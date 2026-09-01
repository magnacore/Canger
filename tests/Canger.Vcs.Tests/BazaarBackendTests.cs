// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Vcs.Backends;

namespace Canger.Vcs.Tests;

/// <summary>
/// The Bazaar backend, against a real working tree.
/// </summary>
/// <remarks>
/// On a current Debian, <c>bzr</c> is an alternatives symlink to Breezy, which is what these run
/// against. Skipped where it is not installed or cannot start — its launcher picks up whichever
/// <c>python3</c> comes first on the PATH, and finds no <c>breezy</c> module under some of them.
/// </remarks>
public sealed class BazaarBackendTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-bzr-" + Path.GetRandomFileName());

    private readonly BazaarBackend _bzr = new();

    private string Tree => Path.Join(_root, "main");

    /// <summary>Whether bzr is present <em>and</em> able to run.</summary>
    private static bool Usable()
    {
        try
        {
            return VcsProcess.Run("bzr", Path.GetTempPath(), "--version").Length > 0;
        }
        catch (Exception e) when (e is VcsException or IOException)
        {
            return false;
        }
    }

    public BazaarBackendTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Builds a tree left mid-merge with an unresolved text conflict.</summary>
    private void BuildConflictedTree()
    {
        VcsProcess.Run("bzr", _root, "init", "-q", "main");
        VcsProcess.Run("bzr", Tree, "whoami", "T <t@t>");
        File.WriteAllText(Path.Join(Tree, "f.txt"), "base\n");
        VcsProcess.Run("bzr", Tree, "add", "-q", "f.txt");
        VcsProcess.Run("bzr", Tree, "commit", "-q", "-m", "base");

        string other = Path.Join(_root, "other");
        VcsProcess.Run("bzr", _root, "branch", "-q", "main", "other");
        VcsProcess.Run("bzr", other, "whoami", "T <t@t>");
        File.WriteAllText(Path.Join(other, "f.txt"), "theirs\n");
        VcsProcess.Run("bzr", other, "commit", "-q", "-m", "theirs");

        File.WriteAllText(Path.Join(Tree, "f.txt"), "mine\n");
        VcsProcess.Run("bzr", Tree, "commit", "-q", "-m", "mine");

        // Leaves the conflict in place; `merge` exits non-zero on conflicts, which is not an error.
        try
        {
            VcsProcess.Run("bzr", Tree, "merge", "-q", "../other");
        }
        catch (VcsException)
        {
            // Expected: a conflicted merge is the state under test.
        }
    }

    [Fact]
    public void ReadsNoStatusFromAConflictDescriptionOrAPendingMerge()
    {
        // The defect, which ranger has too (`ext/vcs/bzr.py:110-125`). Mid-merge, `bzr status
        // --short` prints lines whose payload is prose rather than a path:
        //
        //     C   Text conflict in f.txt
        //     P   T 2026-09-01 theirs
        //
        // Read as records they become the subpaths `Text conflict in f.txt` and
        // `T 2026-09-01 theirs`.
        Assert.SkipUnless(Usable(), "bzr is not installed or cannot start");
        BuildConflictedTree();

        IReadOnlyDictionary<string, VcsStatus> statuses = _bzr.SubpathStatuses(Tree);

        Assert.All(statuses.Keys,
                   key => Assert.True(File.Exists(Path.Join(Tree, key)) ||
                                      Directory.Exists(Path.Join(Tree, key)),
                                      $"'{key}' is not a path in the tree"));
    }

    [Fact]
    public void ReportsAConflictedFileAsConflicted()
    {
        // The other half. `C` is in no translation table, so a conflicted file was reported by
        // its status line alone — as modified — while the conflict itself became a phantom entry.
        Assert.SkipUnless(Usable(), "bzr is not installed or cannot start");
        BuildConflictedTree();

        Assert.Equal(VcsStatus.Conflict, _bzr.SubpathStatuses(Tree)["f.txt"]);
        Assert.Equal(VcsStatus.Conflict, _bzr.RootStatus(Tree));
    }

    [Fact]
    public void StillReadsOrdinaryStatusesWithNoMergeInProgress()
    {
        // Rejecting those two codes must not reject anything real.
        Assert.SkipUnless(Usable(), "bzr is not installed or cannot start");

        VcsProcess.Run("bzr", _root, "init", "-q", "main");
        VcsProcess.Run("bzr", Tree, "whoami", "T <t@t>");
        File.WriteAllText(Path.Join(Tree, "tracked.txt"), "one\n");
        VcsProcess.Run("bzr", Tree, "add", "-q", "tracked.txt");
        VcsProcess.Run("bzr", Tree, "commit", "-q", "-m", "base");
        File.WriteAllText(Path.Join(Tree, "tracked.txt"), "one\ntwo\n");
        File.WriteAllText(Path.Join(Tree, "loose.txt"), "free\n");

        IReadOnlyDictionary<string, VcsStatus> statuses = _bzr.SubpathStatuses(Tree);

        Assert.Equal(VcsStatus.Staged, statuses["tracked.txt"]);
        Assert.Equal(VcsStatus.Untracked, statuses["loose.txt"]);
    }
}
