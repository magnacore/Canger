// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Vcs;
using Canger.Vcs.Backends;

namespace Canger.Vcs.Tests;

/// <summary>
/// The git backend, against real repositories.
/// </summary>
/// <remarks>
/// These have to use real git: the whole job of a backend is to parse what the program actually
/// prints, and a fake that produces what the parser expects would test nothing. The tests skip
/// where git is not installed.
/// </remarks>
public sealed class GitBackendTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-git-" + Path.GetRandomFileName());

    private readonly GitBackend _git = new();

    public GitBackendTests()
    {
        Directory.CreateDirectory(_root);

        // Explicit identity and branch name, so the tests do not depend on the machine's git
        // configuration or on which default branch name that git happens to use.
        Git("init", "-q", "-b", "main", ".");
        Git("config", "user.email", "test@example.invalid");
        Git("config", "user.name", "Test");
    }

    public void Dispose()
    {
        try
        {
            // Git makes parts of .git read-only, which a plain recursive delete refuses.
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

    /// <summary>Runs git in the test repository.</summary>
    private void Git(params string[] arguments) => VcsProcess.Run("git", _root, arguments);

    /// <summary>Writes a file into the test repository.</summary>
    private void Write(string relativePath, string content)
    {
        string path = Path.Join(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Commits everything, so there is a history to compare against.</summary>
    private void Commit(string message)
    {
        Git("add", "--all");
        Git("commit", "-q", "-m", message);
    }

    private VcsStatus StatusOf(string relativePath, bool isDirectory = false) =>
        new VcsRepository(_root, _git).Also(r => r.Refresh())
                                      .StatusOf(Path.Join(_root, relativePath), isDirectory);

    [Fact]
    public void SubpathStatuses_ReportsNothingForACleanRepository()
    {
        // Only what differs is recorded: a clean repository of any size produces nothing.
        Write("a.txt", "content");
        Commit("first");

        Assert.Empty(_git.SubpathStatuses(_root));
    }

    [Fact]
    public void SubpathStatuses_DistinguishesStagedFromMerelyChanged()
    {
        // The two status columns are index and working tree, which is the difference that
        // matters most and the easiest one to get backwards.
        Write("staged.txt", "before");
        Write("changed.txt", "before");
        Commit("first");

        Write("staged.txt", "after");
        Write("changed.txt", "after");
        Git("add", "staged.txt");

        IReadOnlyDictionary<string, VcsStatus> statuses = _git.SubpathStatuses(_root);

        Assert.Equal(VcsStatus.Staged, statuses["staged.txt"]);
        Assert.Equal(VcsStatus.Changed, statuses["changed.txt"]);
    }

    [Fact]
    public void SubpathStatuses_ReportsUntrackedAndIgnoredSeparately()
    {
        Write(".gitignore", "ignored.log\n");
        Commit("first");

        Write("untracked.txt", "new");
        Write("ignored.log", "noise");

        IReadOnlyDictionary<string, VcsStatus> statuses = _git.SubpathStatuses(_root);

        Assert.Equal(VcsStatus.Untracked, statuses["untracked.txt"]);
        Assert.Equal(VcsStatus.Ignored, statuses["ignored.log"]);
    }

    [Fact]
    public void SubpathStatuses_ReportsADeletedFile()
    {
        Write("gone.txt", "content");
        Commit("first");
        File.Delete(Path.Join(_root, "gone.txt"));

        Assert.Equal(VcsStatus.Deleted, _git.SubpathStatuses(_root)["gone.txt"]);
    }

    [Fact]
    public void SubpathStatuses_KeepsTheSecondHalfOfARenameOutOfTheResults()
    {
        // Git reports a rename as two NUL-separated records, and only the first carries a
        // status. Reading the second as an entry would invent a status for a path.
        Write("before.txt", "content");
        Commit("first");
        Git("mv", "before.txt", "after.txt");

        IReadOnlyDictionary<string, VcsStatus> statuses = _git.SubpathStatuses(_root);

        Assert.Contains("after.txt", statuses.Keys);
        Assert.DoesNotContain("before.txt", statuses.Keys);
    }

    [Fact]
    public void SubpathStatuses_HandlesAFilenameContainingASpace()
    {
        // The reason every command asks for NUL-separated output.
        Write("a file with spaces.txt", "content");

        Assert.Equal(VcsStatus.Untracked, _git.SubpathStatuses(_root)["a file with spaces.txt"]);
    }

    [Fact]
    public void RootStatus_TakesTheMostImportantStatusPresent()
    {
        Write("clean.txt", "content");
        Commit("first");
        Write("untracked.txt", "new");

        // Untracked outranks sync, so the repository as a whole reads as untracked.
        Assert.Equal(VcsStatus.Untracked, _git.RootStatus(_root));
    }

    [Fact]
    public void RootStatus_IsInSyncWhenNothingDiffers()
    {
        Write("a.txt", "content");
        Commit("first");

        Assert.Equal(VcsStatus.Sync, _git.RootStatus(_root));
    }

    [Fact]
    public void Branch_ReportsTheCheckedOutBranch()
    {
        Write("a.txt", "content");
        Commit("first");

        Assert.Equal("main", _git.Branch(_root));
    }

    [Fact]
    public void Branch_SaysDetachedWhenTheHeadIsNotOnABranch()
    {
        Write("a.txt", "content");
        Commit("first");
        Git("checkout", "-q", "--detach");

        Assert.Equal("detached", _git.Branch(_root));
    }

    [Fact]
    public void Head_ReportsTheLatestCommit()
    {
        Write("a.txt", "content");
        Commit("the subject line");

        VcsCommit? head = _git.Head(_root);

        Assert.NotNull(head);
        Assert.Equal("the subject line", head.Summary);
        Assert.Contains("Test", head.Author, StringComparison.Ordinal);
        Assert.NotEmpty(head.ShortId);
        Assert.StartsWith(head.ShortId, head.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Head_ReportsNothingForARepositoryWithNoCommits()
    {
        // A repository that has just been created is not an error; it simply has no head.
        Assert.Null(_git.Head(_root));
    }

    [Fact]
    public void Head_ReplacesControlCharactersInTheMessage()
    {
        // A commit message is shown on the status line; an escape sequence in one must not be
        // able to repaint the screen.
        Write("a.txt", "content");
        Git("add", "--all");
        Git("commit", "-q", "-m", "before[31mafter");

        VcsCommit? head = _git.Head(_root);

        Assert.NotNull(head);
        Assert.DoesNotContain('', head.Summary);
    }

    [Fact]
    public void RemoteStatus_ReportsNoneWithoutAnUpstream()
    {
        Write("a.txt", "content");
        Commit("first");

        Assert.Equal(VcsRemoteStatus.None, _git.RemoteStatus(_root));
    }

    [Fact]
    public void Add_StagesTheNamedFilesOnly()
    {
        Write("wanted.txt", "content");
        Write("unwanted.txt", "content");

        _git.Add(_root, ["wanted.txt"]);

        IReadOnlyDictionary<string, VcsStatus> statuses = _git.SubpathStatuses(_root);
        Assert.Equal(VcsStatus.Staged, statuses["wanted.txt"]);
        Assert.Equal(VcsStatus.Untracked, statuses["unwanted.txt"]);
    }

    [Fact]
    public void Reset_TakesFilesBackOutOfTheIndex()
    {
        Write("a.txt", "content");
        Commit("first");
        Write("a.txt", "changed");
        _git.Add(_root, ["a.txt"]);

        _git.Reset(_root, ["a.txt"]);

        Assert.Equal(VcsStatus.Changed, _git.SubpathStatuses(_root)["a.txt"]);
    }

    [Fact]
    public void Repository_GivesADirectoryTheMostImportantStatusBeneathIt()
    {
        // This is what makes a collapsed tree still show that something inside needs attention.
        Write("sub/deep/changed.txt", "content");
        Write("sub/clean.txt", "content");
        Commit("first");
        Write("sub/deep/changed.txt", "edited");

        Assert.Equal(VcsStatus.Changed, StatusOf("sub", isDirectory: true));
    }

    [Fact]
    public void Repository_ReportsSyncForAFileItKnowsNothingAbout()
    {
        Write("a.txt", "content");
        Commit("first");

        Assert.Equal(VcsStatus.Sync, StatusOf("a.txt"));
    }

    [Fact]
    public void Repository_ReportsTheRootsOwnStatusForTheRootItself()
    {
        Write("a.txt", "content");
        Commit("first");
        Write("b.txt", "untracked");

        VcsRepository repository = new(_root, _git);
        repository.Refresh();

        Assert.Equal(VcsStatus.Untracked, repository.StatusOf(_root, isDirectory: true));
    }
}

/// <summary>A small helper so a repository can be built and refreshed in one expression.</summary>
internal static class TestExtensions
{
    /// <summary>Runs an action on a value and returns the value.</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
