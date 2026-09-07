// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.FileSystem;
using Canger.Core.Input;
using Canger.Core.Model;
using Canger.Core.Processes;
using Canger.Core.Settings;
using Canger.Core.State;
using Canger.Core.Tasks;

namespace Canger.TestSupport;

/// <summary>
/// A file manager that records what commands did to it, instead of doing it.
/// </summary>
/// <remarks>
/// Commands are the largest and most user-visible part of the port, so they need to be testable
/// without a terminal, a screen or a real filesystem. This supplies the surface they act on and
/// keeps a log of the effects that have no observable result of their own — messages, quitting,
/// launching programs.
/// </remarks>
public sealed class FakeFileManager : IFileManager
{
    /// <summary>Creates a file manager over an in-memory tree.</summary>
    /// <param name="fileSystem">The tree.</param>
    /// <param name="startPath">Where the first tab opens.</param>
    public FakeFileManager(InMemoryFileSystem fileSystem, string startPath = "/home")
    {
        FileSystem = fileSystem;
        Directories = new DirectoryCache(fileSystem);
        SettingsStore = new SettingsStore();
        Settings = new CangerSettings(SettingsStore);
        Commands = new CommandRegistry();
        CommandDispatcher.RegisterBuiltins(Commands);

        // Written into a scratch directory so the tests exercise the real file formats without
        // touching anything the user owns.
        string state = Path.Join(Path.GetTempPath(), "canger-fake-" + Path.GetRandomFileName());
        Directory.CreateDirectory(state);
        Bookmarks = new Bookmarks(fileSystem, Path.Join(state, "bookmarks"));
        Tags = new Tags(Path.Join(state, "tagged"));
        Metadata = new MetadataManager(FileSystem);
        Linemodes = new LinemodeSelector(new LinemodeRegistry(), Tags, Metadata);

        Tab tab = new(Directories, startPath);
        MutableTabs = new Dictionary<int, Tab> { [1] = tab };
        Dispatcher = new CommandDispatcher(this, Commands);
    }

    /// <summary>The settings store behind <see cref="Settings"/>.</summary>
    public SettingsStore SettingsStore { get; }

    /// <summary>
    /// The buffer shared with other running Cangers, when a test wants two managers over one file.
    /// </summary>
    public SharedCopyBuffer? SharedCopyBuffer { get; set; }

    /// <summary>Picks up a buffer another manager wrote, as the browser does on every draw.</summary>
    /// <returns>Whether anything was taken.</returns>
    public bool RefreshSharedCopyBuffer()
    {
        if (SharedCopyBuffer is not { } shared ||
            !Settings.SharedCopyBuffer ||
            shared.ReadIfChanged() is not { } taken)
        {
            return false;
        }

        CopyBuffer = taken.Paths;
        IsCutPending = taken.Cut;
        return true;
    }

    /// <summary>The tabs, mutable so tests can add more.</summary>
    public Dictionary<int, Tab> MutableTabs { get; }

    /// <summary>The dispatcher, for running command lines.</summary>
    public CommandDispatcher Dispatcher { get; }

    /// <summary>Messages shown, oldest first.</summary>
    public List<(string Message, bool IsError)> Messages { get; } = [];

    /// <summary>Whether a command asked Canger to exit.</summary>
    public bool HasQuit { get; private set; }

    /// <summary>Programs a command asked to run.</summary>
    public List<(string Command, string Flags)> LaunchedPrograms { get; } = [];

    /// <summary>Console openings a command asked for.</summary>
    public List<(string Text, int CursorPosition)> ConsoleOpenings { get; } = [];

    /// <summary>How many times a command asked for a repaint.</summary>
    public int RedrawCount { get; private set; }

    /// <summary>How many times a command asked for a reload.</summary>
    public int ReloadCount { get; private set; }

    /// <inheritdoc />
    public Tab CurrentTab => MutableTabs[CurrentTabNumber];

    /// <inheritdoc />
    public IReadOnlyDictionary<int, Tab> Tabs => MutableTabs;

    /// <inheritdoc />
    public int CurrentTabNumber { get; set; } = 1;

    /// <inheritdoc />
    public CangerSettings Settings { get; }

    /// <inheritdoc />
    public Canger.Core.Tasks.IBackgroundActivity? BackgroundActivity { get; set; }

    /// <inheritdoc />
    public IList<Func<IReadOnlyList<string>, bool>> FileOpeners { get; } = [];

    /// <inheritdoc />
    public DirectoryCache Directories { get; }

    /// <inheritdoc />
    public IFileSystem FileSystem { get; }

    /// <inheritdoc />
    public TaskQueue Tasks { get; } = new();

    /// <inheritdoc />
    public KeyMaps KeyMaps { get; } = new();

    /// <inheritdoc />
    public CommandRegistry Commands { get; }

    /// <inheritdoc />
    public IProcessRunner Runner { get; } = new RecordingProcessRunner();

    /// <inheritdoc />
    public IClipboard Clipboard { get; } = new RecordingClipboard();

    /// <inheritdoc />
    public IReadOnlyList<string> MessageLog =>
        [.. Messages.Select(m => m.IsError ? "error: " + m.Message : m.Message)];

    /// <inheritdoc />
    public bool IsVisualMode { get; private set; }

    /// <inheritdoc />
    public string SearchMethod { get; set; } = "ctime";

    /// <inheritdoc />
    public event EventHandler<string>? DirectoryEntered;

    /// <summary>Raises <see cref="DirectoryEntered"/>, so a plugin's handler can be exercised.</summary>
    /// <param name="path">The directory to announce.</param>
    public void AnnounceDirectory(string path) => DirectoryEntered?.Invoke(this, path);

    /// <summary>Paths that were asked for but are not in the listing.</summary>
    public List<string> UnselectablePaths { get; } = [];

    /// <inheritdoc />
    public bool SelectPath(string path)
    {
        FsNode? target = CurrentTab.Current.Entries.FirstOrDefault(
            e => string.Equals(e.Path, path, StringComparison.Ordinal));

        if (target is null)
        {
            UnselectablePaths.Add(path);
            return false;
        }

        CurrentTab.MoveCursorTo(target);
        return true;
    }

    /// <summary>The modes asked for, oldest first.</summary>
    public List<string> ModeChanges { get; } = [];

    /// <inheritdoc />
    public void ChangeMode(string mode)
    {
        ModeChanges.Add(mode);
        IsVisualMode = mode is "visual" or "visual-reverse";
    }

    /// <summary>Whether the bookmark list has been asked for.</summary>
    public bool BookmarksShown { get; private set; }

    /// <inheritdoc />
    public void ShowBookmarks() => BookmarksShown = true;

    /// <summary>How far the preview has been scrolled, in total.</summary>
    public int PreviewScroll { get; private set; }

    /// <inheritdoc />
    public void ScrollPreview(int lines) => PreviewScroll += lines;

    /// <inheritdoc />
    public IFileOpener Opener { get; } = new RecordingFileOpener();

    /// <summary>The runner, typed so tests can inspect what it was asked to run.</summary>
    public RecordingProcessRunner RecordedRuns => (RecordingProcessRunner)Runner;

    /// <summary>The opener, typed so tests can inspect what it was asked to open.</summary>
    public RecordingFileOpener RecordedOpens => (RecordingFileOpener)Opener;

    /// <inheritdoc />
    public IReadOnlyList<string> CopyBuffer { get; private set; } = [];

    /// <inheritdoc />
    public bool IsCutPending { get; private set; }

    /// <inheritdoc />
    public void Notify(string message, bool isError = false) => Messages.Add((message, isError));

    /// <summary>
    /// The question waiting to be answered, if any, with what to do about it.
    /// </summary>
    /// <remarks>
    /// Nothing answers it on its own: a test drives the outcome by calling
    /// <see cref="Answer"/>, so both branches of a confirmation can be exercised.
    /// </remarks>
    public (string Question, Action<char> Callback, IReadOnlyList<char> Choices)? PendingQuestion
    { get; private set; }

    /// <inheritdoc />
    public void Ask(string question, Action<char> callback, IReadOnlyList<char>? choices = null) =>
        PendingQuestion = (question, callback, choices ?? ['y', 'n']);

    /// <summary>The line being asked for, when one is.</summary>
    public (string Question, Action<string?> Callback, bool Hidden)? PendingPrompt
    { get; private set; }

    /// <inheritdoc />
    public void Prompt(string question, Action<string?> callback, bool hidden = false) =>
        PendingPrompt = (question, callback, hidden);

    /// <summary>Types a line into the prompt that is waiting.</summary>
    /// <param name="line">What the user is taken to have typed, or null for giving up.</param>
    public void Type(string? line)
    {
        if (PendingPrompt is not { } pending)
        {
            throw new InvalidOperationException("Nothing has been asked for.");
        }

        PendingPrompt = null;
        pending.Callback(line);
    }

    /// <summary>Answers the question that is waiting.</summary>
    /// <param name="answer">The key the user is taken to have pressed.</param>
    public void Answer(char answer)
    {
        if (PendingQuestion is not { } pending)
        {
            throw new InvalidOperationException("Nothing has been asked.");
        }

        PendingQuestion = null;
        pending.Callback(answer);
    }

    /// <inheritdoc />
    public void Quit() => HasQuit = true;

    /// <inheritdoc />
    public void OpenConsole(string text = "", int cursorPosition = -1) =>
        ConsoleOpenings.Add((text, cursorPosition));

    /// <summary>Past command lines, which a test can seed to drive history bindings.</summary>
    public List<string> PastCommands { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> CommandHistory => PastCommands;

    /// <inheritdoc />
    public void SetCopyBuffer(IEnumerable<FsNode> files, bool cut) =>
        SetCopyBufferPaths(files.Select(f => f.Path), cut);

    /// <inheritdoc />
    public void SetCopyBufferPaths(IEnumerable<string> paths, bool cut)
    {
        CopyBuffer = [.. paths];
        IsCutPending = cut;

        // The same writing the browser does, so a test can watch two managers share one file.
        if (SharedCopyBuffer is { } shared && Settings.SharedCopyBuffer)
        {
            shared.Write(CopyBuffer, cut);
        }
    }

    /// <inheritdoc />
    public bool Execute(string line, int? quantifier = null, IReadOnlyList<int>? wildcards = null) =>
        Dispatcher.Execute(line, quantifier, wildcards);

    /// <summary>How many rows the browser is pretending to occupy.</summary>
    /// <remarks>Twenty-four rows less the title and status bars, a plausible terminal.</remarks>
    public int BrowserHeight { get; set; } = 22;

    /// <inheritdoc />
    public void ReloadCurrentDirectory()
    {
        ReloadCount++;
        CurrentTab.Current.Load();
    }

    /// <inheritdoc />
    public void Redraw() => RedrawCount++;

    /// <summary>Text a command asked to show in the pager.</summary>
    public List<string> PagerText { get; } = [];

    /// <summary>How many times a command asked to discard cached previews.</summary>
    public int PreviewResetCount { get; private set; }

    /// <inheritdoc />
    public void ShowInPager(string text) => PagerText.Add(text);

    /// <summary>The lines last drawn over the listing.</summary>
    public List<string> InfoLines { get; } = [];

    /// <inheritdoc />
    public void ShowInfo(IReadOnlyList<string> lines)
    {
        InfoLines.Clear();
        InfoLines.AddRange(lines);
    }

    /// <summary>What was handed to the user's own pager.</summary>
    public List<string> ExternalPagerText { get; } = [];

    /// <inheritdoc />
    public void ShowInExternalPager(string text) => ExternalPagerText.Add(text);

    /// <inheritdoc />
    public void ClosePager() => PagerText.Clear();

    /// <summary>Whether the previews were discarded at least once.</summary>
    public bool PreviewsInvalidated => PreviewResetCount > 0;

    /// <inheritdoc />
    public void InvalidatePreviews() => PreviewResetCount++;

    /// <summary>Whether a command asked for the task view.</summary>
    public bool TaskViewOpen { get; private set; }

    /// <inheritdoc />
    public void OpenTaskView() => TaskViewOpen = true;

    /// <inheritdoc />
    public void CloseTaskView() => TaskViewOpen = false;

    /// <summary>Whether a command asked for the device list.</summary>
    public bool DevicesOpen { get; private set; }

    /// <inheritdoc />
    public Canger.Core.Devices.DeviceSession Devices =>
        _devices ??= new(Runner, Tasks, () => SecretToolAvailable);

    /// <summary>
    /// Whether tests should behave as though libsecret's command is installed.
    /// </summary>
    /// <remarks>
    /// Fixed rather than probed, so a test means the same thing on every machine. It was probed
    /// once, and the passphrase tests passed only on machines without libsecret — which was all
    /// of them until one had it.
    /// </remarks>
    public bool SecretToolAvailable { get; set; } = true;

    private Canger.Core.Devices.DeviceSession? _devices;

    /// <inheritdoc />
    public void OpenDevices() => DevicesOpen = true;

    /// <inheritdoc />
    public void CloseDevices() => DevicesOpen = false;

    /// <inheritdoc />
    public Bookmarks Bookmarks { get; }

    /// <inheritdoc />
    public Tags Tags { get; }

    /// <inheritdoc />
    public LinemodeSelector Linemodes { get; }

    /// <inheritdoc />
    public MetadataManager Metadata { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Settable so a test can attach a service over a real repository; null by default, since
    /// most tests have no repository and should not pay for a worker thread.
    /// </remarks>
    public Canger.Vcs.VcsService? Vcs { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Routed through <see cref="Runner"/>, as the real browser does, so a test can assert on
    /// either the convenience list or the request the runner actually received.
    /// </remarks>
    public void RunProgram(string command, string flags = "")
    {
        LaunchedPrograms.Add((command, flags));
        Runner.Run(new ProcessRequest(command, new ProcessFlags(flags), CurrentTab.Path));
    }

    /// <summary>Directories re-read by path, oldest first.</summary>
    public List<string> Reloaded { get; } = [];

    /// <inheritdoc />
    public void ReloadDirectory(string path)
    {
        Reloaded.Add(path);
        Directories.Get(path).Load();
    }

    /// <summary>Everything queued to run in the background, oldest first.</summary>
    public List<(string Description, string Command, string? WorkingDirectory)> BackgroundWork
    { get; } = [];

    /// <summary>What each queued job was given to watch its progress with, oldest first.</summary>
    /// <remarks>
    /// Recorded because the choice of source is exactly the kind of decision a caller gets wrong
    /// invisibly: an archive that reports nothing looks the same as one whose reporting was never
    /// wired up.
    /// </remarks>
    public List<ICommandProgress?> BackgroundProgress { get; } = [];

    /// <inheritdoc />
    /// <remarks>
    /// Really queued, on the real <see cref="Tasks"/>, so a test can drive it with
    /// <c>Tasks.Work()</c> and see the completion callback run. The recorded list is a
    /// convenience for the common assertion, which is only that the work was queued at all
    /// rather than run in front of the interface.
    /// </remarks>
    public QueuedTask RunInBackground(string description, string command,
                                      string? workingDirectory = null,
                                      Action<CommandTask>? finished = null,
                                      ICommandProgress? progress = null)
    {
        BackgroundWork.Add((description, command, workingDirectory));
        BackgroundProgress.Add(progress);

        CommandTask task = new(
            Runner,
            new ProcessRequest(command, default, workingDirectory ?? CurrentTab.Path),
            description,
            (message, isError) => Notify(message, isError),
            finished,
            progress);

        return Tasks.Add(task);
    }

    /// <summary>The last message shown, or <see langword="null"/> when there was none.</summary>
    public string? LastMessage => Messages.Count > 0 ? Messages[^1].Message : null;

    /// <summary>Records what was asked to run, without running it.</summary>
    /// <summary>
    /// A clipboard that keeps what it was given instead of reaching a display.
    /// </summary>
    public sealed class RecordingClipboard : IClipboard
    {
        /// <summary>Everything copied, oldest first.</summary>
        public List<string> Copied { get; } = [];

        /// <summary>The most recent thing copied, or null when nothing has been.</summary>
        public string? Last => Copied.Count > 0 ? Copied[^1] : null;

        /// <summary>
        /// What to report as the helper that took the text. Set to <see langword="null"/> to
        /// stand in for a machine with no clipboard program installed.
        /// </summary>
        public string? Helper { get; set; } = "xclip";

        /// <inheritdoc />
        public string? Copy(string text)
        {
            Copied.Add(text);
            return Helper;
        }
    }

    public sealed class RecordingProcessRunner : IProcessRunner
    {
        /// <summary>Everything that was asked to run, oldest first.</summary>
        public List<ProcessRequest> Requests { get; } = [];

        /// <summary>What to report back.</summary>
        public ProcessResult Result { get; set; } = new(0);

        /// <inheritdoc />
        /// <remarks>Never raised: nothing here takes the terminal.</remarks>
        public event EventHandler? Suspending
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        /// <remarks>Never raised: nothing here takes the terminal, so nothing gives it back.</remarks>
        public event EventHandler? Resumed
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public ProcessResult Run(ProcessRequest request)
        {
            Requests.Add(request);
            return Result;
        }

        /// <summary>
        /// What was written to a program's standard input, oldest first.
        /// </summary>
        /// <remarks>
        /// Recorded so a test can prove a passphrase reached the program by the pipe and not by
        /// the command line, which is the whole point of the method. Kept in plain sight here
        /// because this is a test double and there are no real secrets in it.
        /// </remarks>
        public List<(string Command, string Input)> Fed { get; } = [];

        /// <summary>What a run with input should report, keyed by a fragment of the command.</summary>
        public Dictionary<string, ProcessResult> InputResults { get; } =
            new(StringComparer.Ordinal);

        /// <inheritdoc />
        public ProcessResult RunWithInput(ProcessRequest request, string input)
        {
            Requests.Add(request);
            Fed.Add((request.Command, input));

            foreach ((string fragment, ProcessResult result) in InputResults)
            {
                if (request.Command.Contains(fragment, StringComparison.Ordinal))
                {
                    return result;
                }
            }

            return Result;
        }

        /// <summary>
        /// What a captured run should report, keyed by a fragment of the command.
        /// </summary>
        /// <remarks>
        /// Keyed rather than a single value because one command may run several choosers in turn,
        /// and a test needs to say what each of them answered.
        /// </remarks>
        public Dictionary<string, ProcessResult> CapturedResults { get; } =
            new(StringComparer.Ordinal);

        /// <summary>What a captured run reports when nothing more specific is arranged.</summary>
        public ProcessResult CapturedResult { get; set; } = new(0);

        /// <inheritdoc />
        public ProcessResult RunCapturingOutput(ProcessRequest request)
        {
            Requests.Add(request);

            foreach ((string fragment, ProcessResult result) in CapturedResults)
            {
                if (request.Command.Contains(fragment, StringComparison.Ordinal))
                {
                    return result;
                }
            }

            return CapturedResult;
        }

        /// <summary>
        /// What each background command should do, keyed by a fragment of its command line.
        /// </summary>
        public Dictionary<string, FakeBackgroundProcess> BackgroundResults { get; } =
            new(StringComparer.Ordinal);

        /// <summary>Every background handle handed out, oldest first.</summary>
        public List<FakeBackgroundProcess> Started { get; } = [];

        /// <summary>Whether starting a background command fails outright.</summary>
        public bool BackgroundStartFails { get; set; }

        /// <inheritdoc />
        public IBackgroundProcess? StartInBackground(ProcessRequest request)
        {
            Requests.Add(request);

            if (BackgroundStartFails)
            {
                return null;
            }

            foreach ((string fragment, FakeBackgroundProcess arranged) in BackgroundResults)
            {
                if (request.Command.Contains(fragment, StringComparison.Ordinal))
                {
                    Started.Add(arranged);
                    return arranged;
                }
            }

            FakeBackgroundProcess process = new();
            Started.Add(process);
            return process;
        }
    }

    /// <summary>
    /// A background command that finishes when the test says so.
    /// </summary>
    /// <remarks>
    /// Exits immediately by default, so a test that only cares that the work was queued need say
    /// nothing. Set <see cref="StepsBeforeExit"/> to check that the queue really does give the
    /// interface a turn between polls.
    /// </remarks>
    public sealed class FakeBackgroundProcess : IBackgroundProcess
    {
        /// <summary>How many waits to refuse before reporting the program as finished.</summary>
        public int StepsBeforeExit { get; set; }

        /// <summary>How many times the task waited on this program.</summary>
        public int Waits { get; private set; }

        /// <summary>Whether it was killed rather than left to finish.</summary>
        public bool Killed { get; private set; }

        /// <summary>Whether the handle was released.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc />
        public bool HasExited { get; private set; }

        /// <summary>What to report once it has finished.</summary>
        public int Code { get; set; }

        /// <inheritdoc />
        public int? ExitCode => HasExited ? Code : null;

        /// <inheritdoc />
        public string StandardOutput { get; set; } = string.Empty;

        /// <inheritdoc />
        public string StandardError { get; set; } = string.Empty;

        /// <inheritdoc />
        public bool WaitForExit(TimeSpan timeout)
        {
            Waits++;

            if (Waits > StepsBeforeExit)
            {
                HasExited = true;
            }

            return HasExited;
        }

        /// <inheritdoc />
        public void Kill()
        {
            Killed = true;
            HasExited = true;
        }

        /// <inheritdoc />
        public void Dispose() => Disposed = true;
    }

    /// <summary>Records what was asked to open, without opening it.</summary>
    public sealed class RecordingFileOpener : IFileOpener
    {
        /// <summary>Everything that was asked to open, oldest first.</summary>
        public List<(IReadOnlyList<string> Paths, int Number, string? Label)> Opened { get; } = [];

        /// <summary>What to report back.</summary>
        public OpenResult Result { get; set; } = new(true);

        /// <summary>What to offer as alternatives.</summary>
        public List<OpenAlternative> AvailableAlternatives { get; } = [];

        /// <inheritdoc />
        public OpenResult Open(IReadOnlyList<string> paths, int number = 0, string? label = null,
                               string flags = "")
        {
            Opened.Add((paths, number, label));
            return Result;
        }

        /// <inheritdoc />
        public IReadOnlyList<OpenAlternative> Alternatives(string path) => AvailableAlternatives;
    }

    /// <summary>Adds another tab, so multi-tab macros can be exercised.</summary>
    /// <param name="number">The tab number.</param>
    /// <param name="path">Where it opens.</param>
    /// <returns>The new tab.</returns>
    public Tab AddTab(int number, string path)
    {
        Tab tab = new(Directories, path);
        MutableTabs[number] = tab;
        return tab;
    }

    /// <summary>Tabs that have been closed, most recent last.</summary>
    private readonly List<Tab> _closedTabs = [];

    /// <inheritdoc />
    public void OpenTab(int number, string? path = null)
    {
        if (!MutableTabs.TryGetValue(number, out Tab? tab))
        {
            tab = new Tab(Directories, CurrentTab.Path);
            tab.History.InheritFrom(CurrentTab.History);
            MutableTabs[number] = tab;
        }

        CurrentTabNumber = number;

        if (path is not null)
        {
            tab.Enter(path);
        }
    }

    /// <inheritdoc />
    public bool CloseTab(int? number = null)
    {
        int target = number ?? CurrentTabNumber;

        if (MutableTabs.Count <= 1 || !MutableTabs.TryGetValue(target, out Tab? tab))
        {
            return false;
        }

        MutableTabs.Remove(target);
        _closedTabs.Add(tab);

        if (target == CurrentTabNumber)
        {
            CurrentTabNumber = MutableTabs.Keys.Order().First();
        }

        return true;
    }

    /// <inheritdoc />
    public void ShiftTab(int number)
    {
        if (number == CurrentTabNumber || number < 1)
        {
            return;
        }

        Tab moving = CurrentTab;
        int from = CurrentTabNumber;
        MutableTabs.Remove(from);
        MakeRoom(number, number > from ? -1 : 1);
        MutableTabs[number] = moving;
        CurrentTabNumber = number;
    }

    /// <inheritdoc />
    public bool RestoreTab()
    {
        if (_closedTabs.Count == 0)
        {
            return false;
        }

        Tab tab = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);

        int number = 1;

        while (MutableTabs.ContainsKey(number))
        {
            number++;
        }

        MutableTabs[number] = tab;
        CurrentTabNumber = number;
        return true;
    }

    /// <summary>Empties a tab number by pushing whatever holds it along.</summary>
    private void MakeRoom(int number, int direction)
    {
        if (!MutableTabs.TryGetValue(number, out Tab? occupant))
        {
            return;
        }

        MakeRoom(number + direction, direction);
        MutableTabs.Remove(number);
        MutableTabs[number + direction] = occupant;
    }
}
