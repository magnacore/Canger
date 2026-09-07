// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.Processes;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Plugins.Tests;

/// <summary>
/// The audio playback plugin Canger ships, compiled and driven.
/// </summary>
/// <remarks>
/// The command line is the half of this that is easy to get wrong and impossible to notice: mpv
/// prints its status line only under a particular combination of flags, and two of the obvious
/// ways to quieten it — <c>--no-terminal</c> and any <c>--msg-level=all=…</c> — silence the status
/// line along with everything else. Both were measured; both are pinned here, because a change
/// that reintroduced either would leave a plugin that plays audio perfectly and shows nothing.
/// </remarks>
public sealed class ShippedMkaTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-mka-" + Path.GetRandomFileName());

    public ShippedMkaTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    /// <summary>Finds the shipped plugins beside the repository root.</summary>
    private static string PluginDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Join(directory.FullName, "artifacts", "plugins");

            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")) &&
                Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    /// <summary>A browser holding one audio file and one text file, with the plugin loaded.</summary>
    private FakeFileManager Build(string selected = "book.mka")
    {
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        InMemoryFileSystem files = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/audio")
            .AddDirectory("/home/audio/folder")
            .AddFile("/home/audio/book.mka")
            .AddFile("/home/audio/notes.txt");

        FakeFileManager manager = new(files, "/home/audio");
        PluginHost host = new(manager.Commands, null, new ScriptCompiler());

        string configured = Path.Join(_root, "config");
        Directory.CreateDirectory(Path.Join(configured, "plugins"));
        File.Copy(Path.Join(plugins, "mka.cs"), Path.Join(configured, "plugins", "mka.cs"));
        host.LoadFrom(configured);

        PluginLoad load = Assert.Single(host.Loads);
        Assert.True(load.Succeeded,
                    "the shipped mka.cs did not compile: " +
                    string.Join("; ", load.Diagnostics ?? []));

        IReadOnlyList<Canger.Core.Model.FsNode> entries = manager.CurrentTab.Current.Entries;
        int index = entries.ToList().FindIndex(e => e.Basename == selected);
        Assert.True(index >= 0, $"{selected} is not in the listing");
        manager.CurrentTab.MoveCursor(index);

        return manager;
    }

    private static string CommandFor(FakeFileManager manager) =>
        Assert.Single(((FakeFileManager.RecordingProcessRunner)manager.Runner).Requests).Command;

    [Fact]
    public void EnterOnAudioStartsMpvWithoutAWindowOrTheKeyboard()
    {
        FakeFileManager manager = Build();

        Assert.True(manager.Execute("mka_open"));

        string command = CommandFor(manager);

        Assert.Contains("mpv ", command, StringComparison.Ordinal);
        Assert.Contains("book.mka", command, StringComparison.Ordinal);
        Assert.Contains("--no-video", command, StringComparison.Ordinal);
        Assert.Contains("--input-terminal=no", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ItAsksMpvForTheStatusLineItThenReads()
    {
        FakeFileManager manager = Build();
        manager.Execute("mka_open");

        string command = CommandFor(manager);

        Assert.Contains("--term-status-msg=", command, StringComparison.Ordinal);
        Assert.Contains("canger-mka:", command, StringComparison.Ordinal);
        Assert.Contains("${time-pos}", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ItDoesNotUseTheFlagsThatSilenceThatLine()
    {
        // Measured: `--no-terminal` gave no status readings at all where `--input-terminal=no`
        // gave sixteen in two seconds, and every `--msg-level=all=…` value tried gave none,
        // because the status message rides that log level. Either would leave playback working
        // and the bar empty.
        FakeFileManager manager = Build();
        manager.Execute("mka_open");

        string command = CommandFor(manager);

        Assert.DoesNotContain("--no-terminal", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--msg-level", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ItAsksForAControlSocketShortEnoughToBind()
    {
        // A unix socket address is about 108 bytes and mpv says only "Could not create IPC
        // socket" past that, leaving no way to pause. The runtime directory is short; a
        // temporary directory under a session path is not.
        FakeFileManager manager = Build();
        manager.Execute("mka_open");

        string command = CommandFor(manager);
        int at = command.IndexOf("--input-ipc-server=", StringComparison.Ordinal);

        Assert.True(at >= 0, "no control socket was asked for");

        string rest = command[(at + "--input-ipc-server=".Length)..];
        string socket = rest[..rest.IndexOf(' ', StringComparison.Ordinal)].Trim('\'');

        Assert.True(socket.Length < 100, $"the socket path is {socket.Length} bytes: {socket}");
    }

    [Fact]
    public void EnterOnAnythingElseIsStillJustEnter()
    {
        // The binding adds a case rather than replacing the key: a text file goes to rifle and a
        // directory opens, exactly as `move right=1` always did.
        FakeFileManager manager = Build(selected: "notes.txt");

        manager.Execute("mka_open");

        Assert.DoesNotContain(((FakeFileManager.RecordingProcessRunner)manager.Runner).Requests,
                              r => r.Command.Contains("mpv", StringComparison.Ordinal));
    }

    [Fact]
    public void EnterOnADirectoryOpensIt()
    {
        FakeFileManager manager = Build(selected: "folder");

        manager.Execute("mka_open");

        Assert.Equal("/home/audio/folder", manager.CurrentTab.Path);
    }

    [Fact]
    public void PausingWithNothingPlayingSaysSo()
    {
        FakeFileManager manager = Build();

        manager.Execute("mka_pause");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void StoppingWithNothingPlayingSaysSo()
    {
        FakeFileManager manager = Build();

        manager.Execute("mka_stop");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void ItPublishesItselfAsTheThingTheStatusBarShouldShow()
    {
        FakeFileManager manager = Build();

        Assert.Null(manager.BackgroundActivity);

        manager.Execute("mka_open");

        Assert.NotNull(manager.BackgroundActivity);
    }

    [Fact]
    public void ItReadsTheLastReadingMpvWroteAndTheProgressWithIt()
    {
        // mpv rewrites the line about eight times a second, separated by carriage returns, and
        // the newest has no terminator until the one after it arrives. The newest is the one
        // that must be shown.
        FakeFileManager manager = Build();
        FakeFileManager.RecordingProcessRunner runner =
            (FakeFileManager.RecordingProcessRunner)manager.Runner;

        runner.BackgroundResults["mpv"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 10_000,
            StandardOutput =
                "canger-mka:1.5|00:00:09 / 00:10:00 (2%) 1.5x\r" +
                "canger-mka:33.3333|00:03:20 / 00:10:00 (33%) 1.5x",
        };

        manager.Execute("mka_open");

        IBackgroundActivity activity = Assert.IsAssignableFrom<IBackgroundActivity>(
            manager.BackgroundActivity);

        Assert.Equal("00:03:20 / 00:10:00 (33%) 1.5x", activity.Describe());
        Assert.Equal(0.333333, activity.Progress!.Value, 5);
    }

    [Fact]
    public void ItSaysNothingUntilMpvHasWrittenAWholeReading()
    {
        // A line torn by a read boundary is passed over rather than shown half-formed.
        FakeFileManager manager = Build();
        FakeFileManager.RecordingProcessRunner runner =
            (FakeFileManager.RecordingProcessRunner)manager.Runner;

        runner.BackgroundResults["mpv"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 10_000,
            StandardOutput = "canger-mka:33.3|00:03:20 / 00:1",
        };

        manager.Execute("mka_open");

        Assert.Null(manager.BackgroundActivity!.Describe());
    }

    [Fact]
    public void ItTakesItselfOffTheBarWhenMpvHasFinished()
    {
        FakeFileManager manager = Build();
        FakeFileManager.RecordingProcessRunner runner =
            (FakeFileManager.RecordingProcessRunner)manager.Runner;

        FakeFileManager.FakeBackgroundProcess mpv = new()
        {
            StepsBeforeExit = 0,
            StandardOutput = "canger-mka:100|00:10:00 / 00:10:00 (100%) 1.5x\r",
        };

        runner.BackgroundResults["mpv"] = mpv;
        manager.Execute("mka_open");

        IBackgroundActivity activity = manager.BackgroundActivity!;
        Assert.NotNull(activity.Describe());

        // The fake reports it has ended only once something has waited on it, which is how it
        // stands in for a file that has played to its end.
        mpv.WaitForExit(TimeSpan.Zero);
        Assert.Null(activity.Describe());

        Assert.Null(manager.BackgroundActivity);
    }
}
