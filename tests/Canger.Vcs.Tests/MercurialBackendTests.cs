// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Vcs.Backends;

namespace Canger.Vcs.Tests;

/// <summary>
/// The Mercurial backend, against a real repository.
/// </summary>
/// <remarks>
/// It was the last backend with no tests of its own. It had been checked once — ranger's <c>Hg</c>
/// class and this one, over the same repository, producing identical output — but that was a run
/// in a scratch directory, which nothing regresses against. Skipped where hg is not installed.
/// </remarks>
public sealed class MercurialBackendTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-hg-" + Path.GetRandomFileName());

    private readonly MercurialBackend _hg = new();

    /// <summary>Whether hg is present and able to run.</summary>
    private static bool Usable()
    {
        try
        {
            return VcsProcess.Run("hg", Path.GetTempPath(), "--version").Length > 0;
        }
        catch (Exception e) when (e is VcsException or IOException)
        {
            return false;
        }
    }

    public MercurialBackendTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Runs hg in the repository, with an identity that does not depend on the machine.</summary>
    private void Hg(params string[] arguments) =>
        VcsProcess.Run("hg", _root, ["--config", "ui.username=Test <test@example.invalid>",
                                     .. arguments]);

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Join(_root, name), content);

    /// <summary>A repository holding one file of every status Mercurial reports.</summary>
    private void BuildRepository()
    {
        Hg("init", ".");
        Write(".hgignore", "syntax: glob\nignored.txt\n");
        Write("changed.txt", "base\n");
        Write("removed.txt", "base\n");
        Write("missing.txt", "base\n");
        Write("clean.txt", "base\n");
        Hg("add", ".");
        Hg("commit", "-m", "base");

        Write("changed.txt", "base\nedited\n");
        Hg("remove", "removed.txt");
        File.Delete(Path.Join(_root, "missing.txt"));
        Write("added.txt", "new\n");
        Hg("add", "added.txt");
        Write("untracked.txt", "free\n");
        Write("ignored.txt", "ignored\n");
    }

    [Fact]
    public void ReadsEveryStatusMercurialReports()
    {
        Assert.SkipUnless(Usable(), "hg is not installed");
        BuildRepository();

        IReadOnlyDictionary<string, VcsStatus> statuses = _hg.SubpathStatuses(_root);

        Assert.Equal(VcsStatus.Changed, statuses["changed.txt"]);
        Assert.Equal(VcsStatus.Untracked, statuses["untracked.txt"]);
        Assert.Equal(VcsStatus.Ignored, statuses["ignored.txt"]);
        Assert.Equal(VcsStatus.Deleted, statuses["missing.txt"]);

        // Mercurial has no index, so both an addition and a removal are work already recorded
        // against the next commit. Ranger maps `A` and `R` alike (`ext/vcs/hg.py:19-26`).
        Assert.Equal(VcsStatus.Staged, statuses["added.txt"]);
        Assert.Equal(VcsStatus.Staged, statuses["removed.txt"]);
    }

    [Fact]
    public void SaysNothingAboutACleanFile()
    {
        // `C` is clean, which is the overwhelming majority of a repository and says nothing.
        Assert.SkipUnless(Usable(), "hg is not installed");
        BuildRepository();

        Assert.DoesNotContain("clean.txt", _hg.SubpathStatuses(_root).Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void TheRootTakesTheMostImportantStatusUnderIt()
    {
        Assert.SkipUnless(Usable(), "hg is not installed");
        BuildRepository();

        // Untracked outranks changed and staged in the directory precedence.
        Assert.Equal(VcsStatus.Untracked, _hg.RootStatus(_root));
    }

    [Fact]
    public void ReadsTheBranchAndTheHeadCommit()
    {
        Assert.SkipUnless(Usable(), "hg is not installed");
        BuildRepository();

        Assert.Equal("default", _hg.Branch(_root));

        VcsCommit? head = _hg.Head(_root);
        Assert.NotNull(head);
        Assert.Equal("base", head.Summary);
        Assert.Contains("Test", head.Author, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsNoRemoteForARepositoryThatHasNone()
    {
        Assert.SkipUnless(Usable(), "hg is not installed");
        BuildRepository();

        Assert.Equal(VcsRemoteStatus.None, _hg.RemoteStatus(_root));
    }
}
