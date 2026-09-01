// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;
using Canger.Vcs;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The repository block on the status line.
/// </summary>
/// <remarks>
/// Ranger draws five things there (<c>gui/widgets/statusbar.py:200-227</c>): <c>(git: main)</c>,
/// the repository's standing against its remote, the hovered entry's own status, then the head
/// commit's date and summary. Canger drew the last two. The branch you were on and whether you
/// had anything to push could not be read from the status line at all.
/// </remarks>
public class StatusBarVcsTests
{
    private static string Render(Action<StatusBar> configure)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/user/alpha.txt", "content here");

        ScreenBuffer screen = new(120, 1);
        StatusBar bar = new(new DefaultColorScheme())
        {
            Tab = new Tab(new DirectoryCache(fs), "/home/user"),
            ShowFreeSpace = false,
        };

        configure(bar);
        bar.Layout(new Rect(0, 0, 120, 1));
        bar.Render(screen);

        return screen.TextAt(0);
    }

    [Fact]
    public void NamesTheBackendAndTheBranch()
    {
        // Ranger names the backend because a machine with more than one installed gives no other
        // clue which is answering.
        Assert.Contains("(git: main)",
                        Render(bar => { bar.RepositoryType = "git"; bar.Branch = "main"; }),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheBackendAloneWhenThereIsNoBranch()
    {
        string line = Render(bar => bar.RepositoryType = "git");

        Assert.Contains("(git)", line, StringComparison.Ordinal);
        Assert.DoesNotContain("(git:", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsTheRemoteMarkAndTheEntrysOwnMark()
    {
        // The remote mark is the one thing here that appears nowhere else for a file inside a
        // repository: the column shows it only on rows that are repositories in their own right.
        string line = Render(bar =>
        {
            bar.RepositoryType = "git";
            bar.Branch = "main";
            bar.RemoteStatus = VcsRemoteStatus.Ahead;
            bar.FileStatus = VcsStatus.Changed;
        });

        Assert.Contains("(git: main) >+", line, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheSameMarksAsTheListing()
    {
        // Drawn from the listing's own tables, so the two can never come to disagree. Every
        // status, against ranger's table character for character.
        foreach ((VcsStatus status, string expected) in new[]
                 {
                     (VcsStatus.Conflict, "X"), (VcsStatus.Untracked, "?"),
                     (VcsStatus.Deleted, "-"), (VcsStatus.Changed, "+"),
                     (VcsStatus.Staged, "*"), (VcsStatus.Ignored, "·"),
                     (VcsStatus.Sync, "✓"), (VcsStatus.Unknown, "!"),
                 })
        {
            string line = Render(bar =>
            {
                bar.RepositoryType = "git";
                bar.FileStatus = status;
            });

            Assert.Contains("(git) " + expected, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShowsEveryRemoteMark()
    {
        foreach ((VcsRemoteStatus status, string expected) in new[]
                 {
                     (VcsRemoteStatus.Diverged, "Y"), (VcsRemoteStatus.Ahead, ">"),
                     (VcsRemoteStatus.Behind, "<"), (VcsRemoteStatus.Sync, "="),
                     (VcsRemoteStatus.None, "⌂"),
                 })
        {
            string line = Render(bar =>
            {
                bar.RepositoryType = "git";
                bar.RemoteStatus = status;
            });

            Assert.Contains("(git) " + expected, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SaysNothingWhenThereIsNoRepository()
    {
        // Outside a repository the line must read exactly as it always did.
        string line = Render(_ => { });

        Assert.DoesNotContain("(", line, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", line, StringComparison.Ordinal);
    }

    [Fact]
    public void StillShowsTheCommitAfterTheRepository()
    {
        // The half that already worked, and its position: ranger puts the commit last.
        string line = Render(bar =>
        {
            bar.RepositoryType = "git";
            bar.Branch = "main";
            bar.FileStatus = VcsStatus.Sync;
            bar.Head = new VcsCommit("abc1234", "abc1234full", "T <t@t>",
                                     new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                                     "the summary");
        });

        int repository = line.IndexOf("(git: main)", StringComparison.Ordinal);
        int summary = line.IndexOf("the summary", StringComparison.Ordinal);

        Assert.True(repository >= 0, "the repository block is missing");
        Assert.True(summary > repository, "the commit must come after the repository block");
    }
}
