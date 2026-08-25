// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.App;

namespace Canger.App.Tests;

/// <summary>
/// Tabs remembered from one session to the next.
/// </summary>
/// <remarks>
/// <c>save_tabs_on_exit</c> did nothing at all: no file was written and nothing was restored.
/// The file is a queue of records rather than a snapshot, which is what lets several sessions be
/// open at once — each quitting session appends, each starting session consumes the first.
/// Ranger's format (<c>core/fm.py:551-555</c>, <c>core/main.py:159-180</c>).
/// </remarks>
public sealed class SavedTabsTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-tabs-" + Path.GetRandomFileName());

    private string File_ => Path.Join(_root, "tabs");

    public SavedTabsTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void SaveThenTake_RoundTrips()
    {
        SavedTabs.Save(File_, [_root, Path.GetTempPath()]);

        Assert.Equal([_root, Path.GetTempPath()], SavedTabs.Take(File_, filterDead: false));
    }

    [Fact]
    public void Save_DeclinesASingleTab()
    {
        // Reopening Canger gives you one anyway, and saving it would leave a record behind on
        // every ordinary quit for the next start to consume.
        SavedTabs.Save(File_, [_root]);

        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void Take_ConsumesOneRecordAtATime()
    {
        // Two sessions quitting, then two starting: each gets its own tabs back.
        SavedTabs.Save(File_, ["/one", "/two"]);
        SavedTabs.Save(File_, ["/three", "/four"]);

        Assert.Equal(["/one", "/two"], SavedTabs.Take(File_, filterDead: false));
        Assert.Equal(["/three", "/four"], SavedTabs.Take(File_, filterDead: false));
        Assert.Empty(SavedTabs.Take(File_, filterDead: false));
    }

    [Fact]
    public void Take_RemovesTheFileOnceItIsEmpty()
    {
        SavedTabs.Save(File_, ["/one", "/two"]);
        SavedTabs.Take(File_, filterDead: false);

        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void Take_DropsPathsThatAreGoneWhenAsked()
    {
        SavedTabs.Save(File_, [_root, "/definitely/not/here"]);

        Assert.Equal([_root], SavedTabs.Take(File_, filterDead: true));
    }

    [Fact]
    public void Take_KeepsPathsThatAreGoneOtherwise()
    {
        SavedTabs.Save(File_, [_root, "/definitely/not/here"]);

        Assert.Equal([_root, "/definitely/not/here"], SavedTabs.Take(File_, filterDead: false));
    }

    [Fact]
    public void Take_ReportsNothingWhenThereIsNoFile()
    {
        Assert.Empty(SavedTabs.Take(File_, filterDead: false));
    }

    [Fact]
    public void Take_ConsumesAMalformedRecordRatherThanStickingOnIt()
    {
        // A file with no terminator is still consumed, so one bad record does not stop every
        // future session as well.
        File.WriteAllText(File_, "/one\0/two");

        Assert.Equal(["/one", "/two"], SavedTabs.Take(File_, filterDead: false));
        Assert.False(File.Exists(File_));
    }
}
