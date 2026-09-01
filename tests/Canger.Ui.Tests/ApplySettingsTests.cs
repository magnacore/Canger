// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Ui;

namespace Canger.Ui.Tests;

/// <summary>
/// Which directories the listing settings reach.
/// </summary>
/// <remarks>
/// Reported as backspace — <c>set show_hidden!</c> — updating every column except the preview one
/// on the right, which kept the listing it already had. The settings were pushed onto
/// <c>Pathway</c>, which is the ancestry and the current directory and nothing else; the preview
/// directory got only <c>autoupdate_cumulative_size</c>. Ranger has no such gap because every
/// directory binds itself to <c>show_hidden</c>, <c>hidden_filter</c> and the sort options when it
/// is constructed (<c>container/directory.py:140-148</c>), so all of them refilter at once.
///
/// The same omission was silently doing it to the sort order.
/// </remarks>
public class ApplySettingsTests
{
    private const string HiddenFilter = @"^\.";

    private static Tab Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/work")
            .AddDirectory("/home/work/notes")
            .AddFile("/home/work/notes/visible.txt")
            .AddFile("/home/work/notes/another.txt")
            .AddFile("/home/work/notes/third.txt")
            .AddFile("/home/work/notes/.hidden.txt")
            .AddFile("/home/work/a.txt")
            .AddFile("/home/work/.b.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/work", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        // Cursor onto `notes`, which is what puts it in the preview column.
        IReadOnlyList<FsNode> entries = tab.Current.Entries;
        int index = entries.ToList().FindIndex(e => e.Basename == "notes");
        Assert.True(index >= 0, "notes is not in the listing");
        tab.Current.Cursor.MoveTo(index, entries);

        tab.SelectedDirectory?.Load(TestContext.Current.CancellationToken);

        return tab;
    }

    private static void Apply(Tab tab, bool showHidden, bool reverse = false) =>
        Browser.ApplySettings(
            tab,
            new Dictionary<int, Tab> { [1] = tab },
            viewmode: null,
            new SortOrder(SortKey.Basename, reverse, true, true, false),
            showHidden,
            HiddenFilter,
            autoupdate: false);

    private static IReadOnlyList<string> Names(DirectoryNode directory) =>
        [.. directory.Entries.Select(e => e.Basename)];

    [Fact]
    public void ShowHiddenReachesThePreviewColumn()
    {
        // The reported defect. The preview directory is on screen and must list what every other
        // column lists.
        Tab tab = Build();
        Apply(tab, showHidden: false);

        DirectoryNode preview = Assert.IsType<DirectoryNode>(tab.SelectedDirectory);
        Assert.DoesNotContain(".hidden.txt", Names(preview), StringComparer.Ordinal);

        Apply(tab, showHidden: true);

        Assert.Contains(".hidden.txt", Names(preview), StringComparer.Ordinal);
    }

    [Fact]
    public void ShowHiddenStillReachesTheCurrentDirectory()
    {
        // The half that always worked; it must keep working.
        Tab tab = Build();
        Apply(tab, showHidden: false);
        Assert.DoesNotContain(".b.txt", Names(tab.Current), StringComparer.Ordinal);

        Apply(tab, showHidden: true);
        Assert.Contains(".b.txt", Names(tab.Current), StringComparer.Ordinal);
    }

    [Fact]
    public void TheSortOrderReachesThePreviewColumnToo()
    {
        // Not what was reported, but the same omission: the preview column kept whatever order it
        // was loaded with however the `sort` settings changed.
        Tab tab = Build();
        Apply(tab, showHidden: true);

        DirectoryNode preview = Assert.IsType<DirectoryNode>(tab.SelectedDirectory);
        IReadOnlyList<string> ascending = Names(preview);

        // More than one name, or reversing it would prove nothing — the first version of this
        // test had a single visible entry and passed happily with the defect in place.
        Assert.True(ascending.Count > 1, "the preview needs several entries to have an order");

        Apply(tab, showHidden: true, reverse: true);

        Assert.Equal(ascending.Reverse(), Names(preview));
    }

    [Fact]
    public void EveryVisibleDirectoryIsReached()
    {
        // Stated as the rule rather than as three separate cases: whatever `VisibleDirectories`
        // returns is what gets configured, so a column added later cannot be missed.
        Tab tab = Build();
        Apply(tab, showHidden: true);

        Assert.All(Browser.VisibleDirectories(tab, new Dictionary<int, Tab> { [1] = tab }, null),
                   directory => Assert.True(directory.ShowHidden));
    }
}
