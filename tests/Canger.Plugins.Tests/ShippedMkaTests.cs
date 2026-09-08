// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using Canger.Core.Commands;
using Canger.Core.Processes;
using Canger.Core.Input;
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

        // What Canger does at start-up, and what registers the plugin's claim on audio files.
        host.NotifyInit(manager);

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

        Assert.True(manager.Execute("open"));

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
        manager.Execute("open");

        string command = CommandFor(manager);

        // Only the two fields Canger owns are asserted. What follows the bar is whatever format
        // mpv's own configuration names, so asserting a particular field here would make the test
        // depend on the mpv.conf of whoever runs it.
        Assert.Contains("--term-status-msg=", command, StringComparison.Ordinal);
        Assert.Contains("canger-mka:${=percent-pos}|", command, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadableHalfComesFromMpvsOwnConfiguration()
    {
        // The parser being right is not the same as the command line using it. MPV_HOME is how
        // mpv itself is pointed at a configuration directory, so it is how this points the plugin
        // at a known one — otherwise the test would assert whatever format the machine running it
        // happens to have. Restored afterwards; no other test reads it.
        const string Distinctive = "canger-test ${time-pos} of ${duration}";

        string home = Path.Join(_root, "mpv");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Join(home, "mpv.conf"),
                          $"speed=2.0\nterm-status-msg=\"{Distinctive}\"\n");

        string? previous = Environment.GetEnvironmentVariable("MPV_HOME");
        Environment.SetEnvironmentVariable("MPV_HOME", home);

        try
        {
            FakeFileManager manager = Build();
            manager.Execute("open");

            Assert.Contains(Distinctive, CommandFor(manager), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MPV_HOME", previous);
        }
    }

    [Fact]
    public void ItFallsBackToAPlainFormatWhereMpvNamesNone()
    {
        string home = Path.Join(_root, "empty-mpv");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Join(home, "mpv.conf"), "speed=2.0\n");

        string? previous = Environment.GetEnvironmentVariable("MPV_HOME");
        Environment.SetEnvironmentVariable("MPV_HOME", home);

        try
        {
            FakeFileManager manager = Build();
            manager.Execute("open");

            Assert.Contains("${time-pos}", CommandFor(manager), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MPV_HOME", previous);
        }
    }

    [Fact]
    public void ItDoesNotSayPausedTwiceWhenTheFormatAlreadySaysIt()
    {
        // A format carrying its own ${?pause==yes:(Paused)} — as the reporter's does — says it on
        // the one line mpv manages to write after pausing. Canger's own prefix is there because
        // that line usually never arrives; on the occasions it does, one mention is enough.
        FakeFileManager manager = Build();
        FakeFileManager.RecordingProcessRunner runner =
            (FakeFileManager.RecordingProcessRunner)manager.Runner;

        runner.BackgroundResults["mpv"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 10_000,
            StandardOutput = "canger-mka:2|00:04:56 / 00:07:36 (2%) 1.5x (Paused)\r",
        };

        manager.Execute("open");

        IBackgroundActivity activity = manager.BackgroundActivity!;

        // Held, without going through the socket a fake mpv does not have.
        activity.GetType()
                .GetField("_paused", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(activity, true);

        string line = activity.Describe()!;

        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(
                            line, "paused",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    /// <summary>Starts playback against a fake mpv that never ends.</summary>
    private static FakeFileManager Playing(FakeFileManager manager)
    {
        ((FakeFileManager.RecordingProcessRunner)manager.Runner).BackgroundResults["mpv"] =
            new FakeFileManager.FakeBackgroundProcess { StepsBeforeExit = 10_000 };

        manager.Execute("open");

        return manager;
    }

    /// <summary>What the plugin sent to mpv's control socket, if anything reached it.</summary>
    private static IKeyGrab Grab(FakeFileManager manager) =>
        Assert.IsAssignableFrom<IKeyGrab>(manager.KeyGrab);

    [Fact]
    public void TheModeHandsTheKeyboardToMpvAndSaysSo()
    {
        FakeFileManager manager = Playing(Build());

        Assert.Null(manager.KeyGrab);

        manager.Execute("mka_mode");

        Assert.NotNull(manager.KeyGrab);
        Assert.Equal("MPV", manager.BackgroundActivity!.Badge);
    }

    [Fact]
    public void EscapeTakesTheKeyboardBack()
    {
        // The way out, and it is never forwarded: whatever mpv would do with Escape matters less
        // than always being able to take the keys back.
        FakeFileManager manager = Playing(Build());
        manager.Execute("mka_mode");

        Assert.True(Grab(manager).Handle(KeyCodes.Escape));

        Assert.Null(manager.KeyGrab);
        Assert.Null(manager.BackgroundActivity!.Badge);
    }

    [Fact]
    public void EveryOtherKeyIsSwallowedSoTheBrowserDoesNotAlsoAct()
    {
        // The whole point of the mode: `j` must not move the cursor while mpv has the keys.
        FakeFileManager manager = Playing(Build());
        manager.Execute("mka_mode");

        IKeyGrab grab = Grab(manager);

        Assert.True(grab.Handle('j'));
        Assert.True(grab.Handle(']'));
        Assert.True(grab.Handle(KeyCodes.Left));
    }

    [Fact]
    public void TheModeIsOffAgainWhenNothingHoldsTheKeyboard()
    {
        FakeFileManager manager = Playing(Build());
        manager.Execute("mka_mode");
        manager.Execute("mka_mode");

        Assert.Null(manager.KeyGrab);
    }

    [Fact]
    public void TheModeCannotBeEnteredWithNothingPlaying()
    {
        FakeFileManager manager = Build();

        manager.Execute("mka_mode");

        Assert.Null(manager.KeyGrab);
        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void StoppingGivesTheKeyboardBack()
    {
        // Otherwise a file reaching its end would leave the keys pointed at a program that is no
        // longer there, and no key but Escape would do anything at all.
        FakeFileManager manager = Playing(Build());
        manager.Execute("mka_mode");
        Assert.NotNull(manager.KeyGrab);

        manager.Execute("mka_stop");

        Assert.Null(manager.KeyGrab);
    }

    /// <summary>Reads a term-status-msg line the way the plugin does.</summary>
    private static string? ParseOption(string line)
    {
        string plugins = PluginDirectory();
        Assert.SkipWhen(plugins.Length == 0, "the repository layout was not found");

        CompilationResult compiled =
            new ScriptCompiler().Compile("mka", [Path.Join(plugins, "mka.cs")]);

        Assert.True(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));

        Type configuration = compiled.Assembly!.GetType("MpvConfiguration")!;
        MethodInfo option = configuration.GetMethod(
            "Option", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        return (string?)option.Invoke(null, [line]);
    }

    [Theory]
    // The reporter's own line, quoted because it contains spaces.
    [InlineData(
        """term-status-msg="${playtime-remaining} / ${duration} (${percent-pos}%) ${speed}x" """,
        "${playtime-remaining} / ${duration} (${percent-pos}%) ${speed}x")]
    [InlineData("term-status-msg='${time-pos}'", "${time-pos}")]
    [InlineData("term-status-msg=${time-pos}", "${time-pos}")]
    [InlineData("  term-status-msg = ${time-pos}  ", "${time-pos}")]
    public void ItReadsTheFormatMpvsOwnConfigurationNames(string line, string expected) =>
        Assert.Equal(expected, ParseOption(line));

    [Theory]
    [InlineData("# term-status-msg=${time-pos}")]              // commented out
    [InlineData("term-status-msg-foo=${time-pos}")]            // a different option
    [InlineData("speed=1.5")]                                  // something else entirely
    [InlineData("term-status-msg")]                            // no value at all
    public void ItPassesOverALineThatIsNotThatOption(string line) =>
        Assert.Null(ParseOption(line));

    [Fact]
    public void ItStripsATrailingCommentOnlyFromAnUnquotedValue()
    {
        // mpv requires quoting when a value contains a #, so a # inside quotes is part of the
        // format and a # outside them starts a comment.
        Assert.Equal("${time-pos}", ParseOption("term-status-msg=${time-pos}  # the clock"));
        Assert.Equal("a # b", ParseOption("""term-status-msg="a # b" """));
    }

    [Fact]
    public void ItDoesNotUseTheFlagsThatSilenceThatLine()
    {
        // Measured: `--no-terminal` gave no status readings at all where `--input-terminal=no`
        // gave sixteen in two seconds, and every `--msg-level=all=…` value tried gave none,
        // because the status message rides that log level. Either would leave playback working
        // and the bar empty.
        FakeFileManager manager = Build();
        manager.Execute("open");

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
        manager.Execute("open");

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

        manager.Execute("open");

        Assert.DoesNotContain(((FakeFileManager.RecordingProcessRunner)manager.Runner).Requests,
                              r => r.Command.Contains("mpv", StringComparison.Ordinal));
    }

    [Fact]
    public void EnterOnADirectoryOpensIt()
    {
        FakeFileManager manager = Build(selected: "folder");

        // What the key actually runs, rather than a stand-in for it: a directory is entered
        // before anything is opened at all, and must stay that way with the plugin loaded.
        manager.Execute("move right=1");

        Assert.Equal("/home/audio/folder", manager.CurrentTab.Path);
    }

    [Fact]
    public void StartingSaysNothing()
    {
        // A message outranks the activity line it would be announcing, so "playing OSHO.mka" sat
        // over the very clock it was telling the user about. Starting successfully is silent; the
        // clock appearing is the announcement.
        FakeFileManager manager = Build();

        manager.Execute("open");

        Assert.Empty(manager.Messages);
    }

    [Fact]
    public void StoppingSomethingThatWasPlayingSaysNothingEither()
    {
        FakeFileManager manager = Build();
        FakeFileManager.RecordingProcessRunner runner =
            (FakeFileManager.RecordingProcessRunner)manager.Runner;

        runner.BackgroundResults["mpv"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 10_000,
        };

        manager.Execute("open");
        manager.Execute("mka_stop");

        Assert.Empty(manager.Messages);
    }

    [Fact]
    public void FailingToStartStillSaysSo()
    {
        // The half that must keep talking: silence is only right when something visible happens.
        FakeFileManager manager = Build();
        ((FakeFileManager.RecordingProcessRunner)manager.Runner).BackgroundStartFails = true;

        manager.Execute("open");

        Assert.Contains(manager.Messages, m => m.IsError);
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

        manager.Execute("open");

        Assert.NotNull(manager.BackgroundActivity);
    }

    [Fact]
    public void ItReadsTheLastReadingMpvWroteAndTheProgressWithIt()
    {
        // mpv rewrites the line about eight times a second, separated by carriage returns. The
        // newest *terminated* one is what is shown; a trailing part-line is left for the next
        // frame, which is at most an eighth of a second behind and never half a clock.
        FakeFileManager manager = Build();
        FakeFileManager.RecordingProcessRunner runner =
            (FakeFileManager.RecordingProcessRunner)manager.Runner;

        runner.BackgroundResults["mpv"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 10_000,
            StandardOutput =
                "canger-mka:1.5|00:00:09 / 00:10:00 (2%) 1.5x\r" +
                "canger-mka:33.3333|00:03:20 / 00:10:00 (33%) 1.5x\r" +
                "canger-mka:33.4|00:03:21 / 00:10",
        };

        manager.Execute("open");

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

        manager.Execute("open");

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
        manager.Execute("open");

        IBackgroundActivity activity = manager.BackgroundActivity!;
        Assert.NotNull(activity.Describe());

        // The fake reports it has ended only once something has waited on it, which is how it
        // stands in for a file that has played to its end.
        mpv.WaitForExit(TimeSpan.Zero);
        Assert.Null(activity.Describe());

        Assert.Null(manager.BackgroundActivity);
    }
}
