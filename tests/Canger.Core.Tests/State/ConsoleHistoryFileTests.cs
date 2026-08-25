// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;

namespace Canger.Core.Tests.State;

/// <summary>
/// The console history kept between sessions.
/// </summary>
/// <remarks>
/// Both settings that govern it — <c>save_console_history</c> and
/// <c>max_console_history_size</c> — existed and were read, but nothing ever wrote or read a
/// file, so every command typed at the prompt was lost on exit. The capacity was fixed at fifty
/// regardless of the setting, too.
/// </remarks>
public sealed class ConsoleHistoryFileTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-history-" + Path.GetRandomFileName());

    public ConsoleHistoryFileTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Path_ => Path.Join(_root, "history");

    private static History<string> Filled(params string[] entries)
    {
        History<string> history = new(50, unique: true);

        foreach (string entry in entries)
        {
            history.Add(entry);
        }

        return history;
    }

    [Fact]
    public void SaveThenLoad_RoundTripsInOrder()
    {
        Assert.Null(ConsoleHistoryFile.Save(Filled("echo one", "echo two", "echo three"), Path_));

        History<string> read = new(50, unique: true);
        Assert.Null(ConsoleHistoryFile.Load(read, Path_));

        Assert.Equal(["echo one", "echo two", "echo three"], read.Entries);
    }

    [Fact]
    public void Load_LeavesTheCursorOnTheNewestEntry()
    {
        // So the first press of Up offers the last thing that was run, not the oldest.
        ConsoleHistoryFile.Save(Filled("older", "newest"), Path_);

        History<string> read = new(50, unique: true);
        ConsoleHistoryFile.Load(read, Path_);

        Assert.Equal("newest", read.Current);
    }

    [Fact]
    public void Load_IsQuietWhenThereIsNoFileYet()
    {
        History<string> read = new(50, unique: true);

        Assert.Null(ConsoleHistoryFile.Load(read, Path_));
        Assert.True(read.IsEmpty);
    }

    [Fact]
    public void Load_SkipsBlankLines()
    {
        // An empty entry is something the user could scroll onto and could never have typed.
        File.WriteAllText(Path_, "one\n\n\ntwo\n");

        History<string> read = new(50, unique: true);
        ConsoleHistoryFile.Load(read, Path_);

        Assert.Equal(["one", "two"], read.Entries);
    }

    [Fact]
    public void Load_ReadsRangersOwnFileUnchanged()
    {
        // Same name and format, so the two programs can be pointed at each other's file.
        File.WriteAllText(Path_, "shell ls -la\nrename thing.txt\nflat -1\n");

        History<string> read = new(50, unique: true);
        ConsoleHistoryFile.Load(read, Path_);

        Assert.Equal(["shell ls -la", "rename thing.txt", "flat -1"], read.Entries);
    }

    [Fact]
    public void Save_ReportsWhyItCouldNotWrite()
    {
        string? error = ConsoleHistoryFile.Save(Filled("x"), "/proc/nowhere/history");

        Assert.NotNull(error);
    }

    [Fact]
    public void Save_ReplacesTheFileRatherThanAppending()
    {
        ConsoleHistoryFile.Save(Filled("first run"), Path_);
        ConsoleHistoryFile.Save(Filled("second run"), Path_);

        Assert.Equal(["second run"], File.ReadAllLines(Path_));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // Written aside and moved into place, so a session that dies partway through cannot
        // leave a truncated history.
        ConsoleHistoryFile.Save(Filled("a", "b"), Path_);

        Assert.False(File.Exists(Path_ + ".new"));
    }

    [Fact]
    public void History_KeepsOnlyTheMostRecentWithinItsCapacity()
    {
        History<string> history = new(3, unique: true);

        foreach (string entry in new[] { "one", "two", "three", "four" })
        {
            history.Add(entry);
        }

        ConsoleHistoryFile.Save(history, Path_);

        Assert.Equal(["two", "three", "four"], File.ReadAllLines(Path_));
    }

    [Fact]
    public void History_DoesNotRepeatACommandRunTwice()
    {
        History<string> history = Filled("ls", "cd /tmp", "ls");

        ConsoleHistoryFile.Save(history, Path_);

        Assert.Equal(["cd /tmp", "ls"], File.ReadAllLines(Path_));
    }
}
