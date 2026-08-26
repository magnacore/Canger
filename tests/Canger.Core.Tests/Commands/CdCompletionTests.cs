// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Completing a path typed after <c>:cd</c>.
/// </summary>
/// <remarks>
/// Only names in the current directory used to complete, so <c>:cd /usr/lo</c> and
/// <c>:cd ~/Doc</c> did nothing at all — which is most of the typing a <c>:cd</c> saves. Ranger
/// splits the typed text into the directory part and the partial name and lists the former
/// (<c>config/commands.py:291-297</c>).
///
/// These run against the real filesystem, because completion reads directories directly rather
/// than through the listing — a path being completed is usually one Canger has never loaded.
/// </remarks>
public sealed class CdCompletionTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-cd-" + Path.GetRandomFileName());

    public CdCompletionTests()
    {
        Directory.CreateDirectory(Path.Join(_root, "alpha", "inner"));
        Directory.CreateDirectory(Path.Join(_root, "alberta"));
        Directory.CreateDirectory(Path.Join(_root, "beta"));
        File.WriteAllText(Path.Join(_root, "afile.txt"), string.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory(_root);
        return new FakeFileManager(fs, _root);
    }

    private static IReadOnlyList<string> Complete(FakeFileManager manager, string line) =>
        manager.Dispatcher.Build(line)?.Complete(1) ?? [];

    [Fact]
    public void Complete_OffersDirectoriesInTheCurrentDirectory()
    {
        Assert.Equal(["cd alberta/", "cd alpha/"], Complete(Manager(), "cd al"));
    }

    [Fact]
    public void Complete_OffersNoFiles()
    {
        // `:cd` goes to directories, so a file among the candidates is only ever a wrong guess.
        Assert.DoesNotContain("cd afile.txt", Complete(Manager(), "cd a"),
                              StringComparer.Ordinal);
    }

    [Fact]
    public void Complete_FollowsAnAbsolutePathBeingTyped()
    {
        string typed = "cd " + Path.Join(_root, "al");

        Assert.Equal([$"cd {Path.Join(_root, "alberta")}/", $"cd {Path.Join(_root, "alpha")}/"],
                     Complete(Manager(), typed));
    }

    [Fact]
    public void Complete_DescendsIntoADirectoryAlreadyNamed()
    {
        Assert.Equal(["cd alpha/inner/"], Complete(Manager(), "cd alpha/"));
    }

    [Fact]
    public void Complete_EchoesBackWhatWasTypedRatherThanRewritingIt()
    {
        // The head is repeated verbatim, so a `~` stays a `~` and a relative path stays relative
        // rather than being turned into something else under the user's fingers.
        IReadOnlyList<string> lines = Complete(Manager(), "cd ./al");

        Assert.Equal(["cd ./alberta/", "cd ./alpha/"], lines);
    }

    [Fact]
    public void Complete_OffersNothingForAPathThatDoesNotExist()
    {
        Assert.Empty(Complete(Manager(), "cd /definitely/not/here/x"));
    }

    [Fact]
    public void Complete_OffersABookmarkUnderACandidate()
    {
        // `cd_bookmarks`, on by default: a place named once is reachable without typing the rest
        // of the way to it. Ranger puts these first (`config/commands.py:277-282`).
        FakeFileManager manager = Manager();
        manager.SettingsStore.Set("cd_bookmarks", true);
        manager.Bookmarks.Set('i', Path.Join(_root, "alpha", "inner"));

        Assert.Equal(["cd alpha/inner", "cd alberta/", "cd alpha/"],
                     Complete(manager, "cd al"));
    }

    [Fact]
    public void Complete_LeavesBookmarksOutWhenTheSettingIsOff()
    {
        FakeFileManager manager = Manager();
        manager.SettingsStore.Set("cd_bookmarks", false);
        manager.Bookmarks.Set('i', Path.Join(_root, "alpha", "inner"));

        Assert.Equal(["cd alberta/", "cd alpha/"], Complete(manager, "cd al"));
    }

    [Fact]
    public void Complete_IgnoresABookmarkOutsideEveryCandidate()
    {
        FakeFileManager manager = Manager();
        manager.SettingsStore.Set("cd_bookmarks", true);
        manager.Bookmarks.Set('b', Path.Join(_root, "beta"));

        Assert.Equal(["cd alberta/", "cd alpha/"], Complete(manager, "cd al"));
    }
}
