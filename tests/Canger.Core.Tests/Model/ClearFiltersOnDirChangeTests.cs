// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Model.Filters;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// A filter belongs to the listing it was set on, and is dropped as you leave it.
/// </summary>
/// <remarks>
/// The <c>clear_filters_on_dir_change</c> setting, which did nothing: a <c>zf</c> set in one
/// place followed the user everywhere, which reads as the listing being wrong rather than
/// filtered. Ranger clears it on the way *out* (<c>core/tab.py:140-142</c>), so returning to a
/// directory shows it whole.
/// </remarks>
public class ClearFiltersOnDirChangeTests
{
    private static Tab Build(bool clearOnLeave)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/sub")
            .AddFile("/home/sub/x.txt")
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.txt");

        Tab tab = new(new DirectoryCache(fs), "/home", 20) { ClearFilterOnLeave = clearOnLeave };
        return tab;
    }

    private static void Filter(Tab tab, string name)
    {
        tab.Current.FilterStack.Push(new NameFilter(name, ignoreCase: true));
        tab.Current.Refilter();
    }

    [Fact]
    public void LeavingClearsTheFilterWhenAsked()
    {
        Tab tab = Build(clearOnLeave: true);
        Filter(tab, "alpha");
        Assert.Equal(1, tab.Current.Count);

        tab.Enter("/home/sub", cancellationToken: TestContext.Current.CancellationToken);
        tab.Enter("/home", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, tab.Current.Count);
    }

    [Fact]
    public void LeavingKeepsTheFilterOtherwise()
    {
        Tab tab = Build(clearOnLeave: false);
        Filter(tab, "alpha");

        tab.Enter("/home/sub", cancellationToken: TestContext.Current.CancellationToken);
        tab.Enter("/home", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, tab.Current.Count);
    }

    [Fact]
    public void ADeliberatelyBuiltStackSurvives()
    {
        // Only the topmost name filter goes — the sort `zf` and a search leave. Anything below
        // it was assembled one clause at a time with `:filter_stack` and is not something to
        // discard because someone pressed `l`.
        Tab tab = Build(clearOnLeave: true);
        tab.Current.FilterStack.Push(new InodeTypeFilter("f"));
        Filter(tab, "alpha");

        tab.Enter("/home/sub", cancellationToken: TestContext.Current.CancellationToken);
        tab.Enter("/home", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(tab.Current.FilterStack.Filters);
        Assert.Equal(3, tab.Current.Count);
    }

    [Fact]
    public void NothingHappensWithNoFilterSet()
    {
        Tab tab = Build(clearOnLeave: true);

        tab.Enter("/home/sub", cancellationToken: TestContext.Current.CancellationToken);
        tab.Enter("/home", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, tab.Current.Count);
        Assert.True(tab.Current.FilterStack.IsEmpty);
    }
}
