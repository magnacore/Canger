// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Where the cursor goes when a scout runs.
/// </summary>
/// <remarks>
/// <para>
/// The reported symptom was that <c>fm</c> — <c>console mark</c>, and <c>mark</c> is an alias for
/// <c>scout -mr</c> — marked the matching files but left the cursor where it was, so the search
/// looked as though it had missed.
/// </para>
/// <para>
/// Ranger opens its <c>execute</c> with <c>count = self._count(move=True)</c>, before it marks,
/// before it filters, before anything (<c>config/commands.py</c>). Canger moved only in the case
/// where it was doing nothing else, and returned early from the others.
/// </para>
/// </remarks>
public class ScoutCursorTests
{
    private static FakeFileManager Manager()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.txt")
            .AddFile("/home/delta.txt");

        return new FakeFileManager(fs, "/home");
    }

    private static void PutCursorOn(FakeFileManager manager, string name) =>
        manager.CurrentTab.MoveCursorTo(manager.CurrentTab.Current.Entries.First(
            e => string.Equals(e.RelativePath, name, StringComparison.Ordinal)));

    private static string? Cursor(FakeFileManager manager) =>
        manager.CurrentTab.Selected?.RelativePath;

    [Fact]
    public void MarkingAlsoMovesTheCursorToTheFirstMatch()
    {
        // The reported bug. Marking and moving are not alternatives: ranger does both.
        FakeFileManager manager = Manager();

        manager.Execute("scout -mr gamma");

        Assert.Equal("gamma.txt", Cursor(manager));
    }

    [Fact]
    public void MarkingStillMarks()
    {
        // The move must not have replaced the marking.
        FakeFileManager manager = Manager();

        manager.Execute("scout -mr a");

        Assert.NotEmpty(manager.CurrentTab.Current.MarkedEntries);
    }

    [Fact]
    public void APlainSearchMovesTheCursorAsItAlwaysDid()
    {
        FakeFileManager manager = Manager();

        manager.Execute("scout -rs delta");

        Assert.Equal("delta.txt", Cursor(manager));
    }

    [Fact]
    public void TheSearchStartsFromTheCursorRatherThanTheTopOfTheListing()
    {
        // Ranger rotates the entries by the cursor's position before looking, so a search finds
        // the next match rather than jumping backwards to an earlier one. Starting at the top
        // would walk backwards every time on a pattern with a match above.
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "delta.txt");

        // Both alpha.txt and gamma.txt contain "a"; so does delta.txt itself.
        manager.Execute("scout -rs a");

        Assert.Equal("delta.txt", Cursor(manager));
    }

    [Fact]
    public void ItWrapsPastTheEndOfTheListing()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "gamma.txt");

        // Sorted: alpha, beta, delta, gamma. From gamma, the only "beta" is behind us.
        manager.Execute("scout -rs beta");

        Assert.Equal("beta.txt", Cursor(manager));
    }

    [Fact]
    public void NothingMovesWhenNothingMatches()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "beta.txt");

        manager.Execute("scout -rs zzzz");

        Assert.Equal("beta.txt", Cursor(manager));
        Assert.Contains("no match", manager.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedMarkDoesNotComplainAboutNoMatch()
    {
        // `no match` belongs to a search. A mark that matched nothing has marked nothing, which
        // the listing shows for itself.
        FakeFileManager manager = Manager();

        manager.Execute("scout -mr zzzz");

        Assert.DoesNotContain(manager.Messages,
                              m => m.Message.Contains("no match", StringComparison.Ordinal));
    }

    [Fact]
    public void AFilteringScoutMovesToo()
    {
        // Ranger moves before it applies the filter, so this holds for -p as well.
        FakeFileManager manager = Manager();

        manager.Execute("scout -prs gamma");

        Assert.Equal("gamma.txt", Cursor(manager));
    }
}
