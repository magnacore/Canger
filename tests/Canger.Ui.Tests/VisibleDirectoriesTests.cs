// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Which directories count as on screen, and therefore get re-read when a program that may have
/// changed files finishes.
/// </summary>
/// <remarks>
/// The set has to stay small. The directory cache never evicts, so "everything loaded" grows all
/// session; re-scanning that synchronously would stall the interface and would reach paths on
/// media that has since gone away. What is visible is a handful of columns, and they are by
/// definition the ones being looked at.
/// </remarks>
public class VisibleDirectoriesTests
{
    private static (Tab Tab, DirectoryCache Cache) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/work")
            .AddDirectory("/home/work/notes")
            .AddFile("/home/work/a.txt")
            .AddDirectory("/elsewhere");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/work", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        return (tab, cache);
    }

    /// <summary>Puts the cursor on the named entry.</summary>
    private static void PointAt(Tab tab, string basename)
    {
        IReadOnlyList<FsNode> entries = tab.Current.Entries;
        int index = entries.ToList().FindIndex(e => e.Basename == basename);

        Assert.True(index >= 0, basename + " is not in the listing");
        tab.Current.Cursor.MoveTo(index, entries);
    }

    private static IReadOnlyList<string> Visible(Tab tab, IReadOnlyDictionary<int, Tab> tabs,
                                                 string? viewmode = null) =>
        [.. Browser.VisibleDirectories(tab, tabs, viewmode).Select(d => d.Path)];

    [Fact]
    public void IncludesTheCurrentDirectoryAndItsAncestors()
    {
        // The ancestry columns to the left are on screen and can go stale exactly as the current
        // one can — a command given a path in one of them changed it, and nothing said so.
        (Tab tab, _) = Build();

        IReadOnlyList<string> visible = Visible(tab, new Dictionary<int, Tab> { [1] = tab });

        // The pathway runs from the filesystem root down to the directory the cursor is in.
        Assert.Contains("/home/work", visible);
        Assert.Contains("/home", visible);
        Assert.True(visible.ToList().IndexOf("/home") < visible.ToList().IndexOf("/home/work"),
                    "ancestors come before the directory the cursor is in");
    }

    [Fact]
    public void IncludesThePreviewColumnWhenTheCursorIsOnADirectory()
    {
        // The case behind the report: a tool that walks a tree changes what the right-hand column
        // is showing, and that column kept its old listing until a manual reload.
        (Tab tab, _) = Build();
        PointAt(tab, "notes");

        Assert.Contains("/home/work/notes", Visible(tab, new Dictionary<int, Tab> { [1] = tab }));
    }

    [Fact]
    public void LeavesOutThePreviewWhenTheCursorIsOnAFile()
    {
        (Tab tab, _) = Build();
        PointAt(tab, "a.txt");

        Assert.DoesNotContain("/home/work/a.txt",
                              Visible(tab, new Dictionary<int, Tab> { [1] = tab }));
    }

    [Fact]
    public void LeavesOutDirectoriesThatAreMerelyRemembered()
    {
        // The whole point of the narrowing. /elsewhere has been visited, so the cache — which
        // never evicts — is still holding it, but no column is showing it and it is not rescanned.
        (Tab tab, DirectoryCache cache) = Build();
        cache.Get("/elsewhere").Load(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/elsewhere", Visible(tab, new Dictionary<int, Tab> { [1] = tab }));
    }

    [Fact]
    public void IncludesEveryTabOnlyInMultipane()
    {
        // In multipane each tab is a column, so each tab's directory is on screen. In the miller
        // view the other tabs are not drawn and must not be paid for.
        (Tab tab, DirectoryCache cache) = Build();
        Tab other = new(cache, "/home", 20);
        other.Current.Load(TestContext.Current.CancellationToken);

        Tab far = new(cache, "/elsewhere", 20);
        far.Current.Load(TestContext.Current.CancellationToken);

        Dictionary<int, Tab> tabs = new() { [1] = tab, [2] = other, [3] = far };

        // The miller view draws one tab, so the others cost nothing.
        Assert.DoesNotContain("/elsewhere", Visible(tab, tabs));

        // Multipane draws them all, so they are all on screen and all re-read.
        Assert.Contains("/elsewhere", Visible(tab, tabs, "multipane"));
    }

    [Fact]
    public void ReturnsEachDirectoryOnlyOnce()
    {
        // Two tabs on the same directory, or an ancestor that is also another tab's current
        // directory, must not be scanned twice.
        (Tab tab, DirectoryCache cache) = Build();
        Tab same = new(cache, "/home/work", 20);
        same.Current.Load(TestContext.Current.CancellationToken);

        IReadOnlyList<string> visible =
            Visible(tab, new Dictionary<int, Tab> { [1] = tab, [2] = same }, "multipane");

        Assert.Equal(visible.Distinct().Count(), visible.Count);
    }
}
