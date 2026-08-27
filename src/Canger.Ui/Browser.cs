// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using Canger.Core.Commands;
using Canger.Core.FileSystem;
using Canger.Core.Input;
using Canger.Core.Model;
using Canger.Core.Previews;
using Canger.Preview.Images;
using Canger.Core.Processes;
using Canger.Core.State;
using Canger.Core.Settings;
using Canger.Core.Tasks;
using Canger.Tui;
using Canger.Tui.Input;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;
using Canger.Vcs;
using Canger.Ui.Views;
using Canger.Ui.Widgets;

namespace Canger.Ui;

/// <summary>
/// The running file manager: the screen, the input loop, and the state commands act on.
/// </summary>
/// <remarks>
/// <para>
/// One loop drives everything. It waits for input, decodes it, routes it to the console or the
/// browser depending on which has focus, runs whatever command that produced, gives background
/// work a slice of time, and redraws. The wait has a timeout so work continues while nobody is
/// typing, and a shorter one while an escape sequence might still be arriving.
/// </para>
/// <para>
/// This is also the <see cref="IFileManager"/> that commands are written against, which is why
/// it exposes the tabs, the settings and the copy buffer rather than keeping them private.
/// </para>
/// </remarks>
public sealed class Browser : IFileManager, IDisposable
{
    private const int EscapeDelayMilliseconds = 25;

    private readonly Terminal _terminal;
    private readonly ScreenBuffer _screen;
    private readonly InputDecoder _decoder = new();
    private readonly KeyBuffer _keys;
    private readonly KeyBuffer _consoleKeys;
    private readonly KeyBuffer _pagerKeys;
    private readonly MillerView _view;
    private readonly TitleBar _titleBar;
    private readonly StatusBar _statusBar;
    private readonly ConsoleWidget _console;
    private readonly Pager _pager;
    private readonly TaskView _taskView;
    private readonly KeyBuffer _taskViewKeys;
    private readonly CommandDispatcher _dispatcher;
    private readonly Dictionary<int, Tab> _tabs;

    private CangerCommand? _pendingCommand;
    private Action<char>? _questionCallback;
    private string? _message;
    private bool _messageIsError;

    /// <summary>When the message stops being shown, as a tick count.</summary>
    private long _messageExpiresAt;

    /// <summary>
    /// How long a message stays on the status bar.
    /// </summary>
    /// <remarks>
    /// Ranger's default duration for <c>fm.notify</c> (<c>core/actions.py:165</c>). Canger had no
    /// expiry at all, so a message sat there until the next keystroke — which, if the keystroke
    /// that produced it was the last one for a while, meant forever, with the file under the
    /// cursor hidden behind it the whole time.
    /// </remarks>
    private const int MessageMilliseconds = 4000;
    private bool _running = true;
    private bool _hadWork;

    private readonly HintWindow _hints;
    private readonly BookmarkWindow _bookmarkWindow;

    private readonly MultipaneView _multipaneView;

    private readonly ColorSchemeRegistry _colorSchemes;

    private readonly SwitchableColorScheme _colorScheme;

    /// <summary>
    /// Set from a background thread when something it computed is ready to be shown. Read and
    /// written through <see cref="Volatile"/> because only the main loop clears it.
    /// </summary>
    private bool _needsRedraw;

    /// <summary>
    /// Set when another program has had the terminal, so the next frame is written in full.
    /// </summary>
    /// <remarks>
    /// The screen buffer diffs against what it last wrote. After another program has drawn over
    /// the terminal that record is fiction, so an ordinary diffed frame writes almost nothing and
    /// leaves the display blank. A resize is the same situation arrived at differently: the
    /// terminal reflows what it is holding, and nothing the buffer believes about it is true.
    ///
    /// Written from the signal thread as well as the drawing thread, hence <see cref="Volatile"/>.
    /// </remarks>
    private bool _needsFullRepaint;

    /// <summary>Whether the hint window is showing what could follow the keys typed so far.</summary>
    private bool _showHints;

    /// <summary>Whether the bookmark list is on screen.</summary>
    /// <remarks>
    /// Turned off by every keystroke and back on by <c>:draw_bookmarks</c>, which is what a
    /// <c>&lt;bg&gt;</c> binding runs while a sequence is still pending.
    /// </remarks>
    private bool _showBookmarks;

    /// <inheritdoc />
    public void ShowBookmarks() => _showBookmarks = true;

    /// <summary>Lines drawn over the bottom of the listing, or nothing.</summary>
    /// <remarks>
    /// Ranger's <c>ui.browser.draw_info</c>. It outlives the keystroke that set it, because the
    /// thing it answers — "which number opens this in what?" — is needed while the console that
    /// follows is being typed into.
    /// </remarks>
    private IReadOnlyList<string>? _info;

    /// <inheritdoc />
    public void ShowInfo(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _info = lines.Count > 0 ? lines : null;
        RequestRedraw();
    }


    private readonly FileDescriber _describer;
    private readonly IImageDisplay? _images;
    private (string Path, Rect Bounds)? _shownImage;

    /// <summary>Prepares the browser.</summary>
    /// <param name="terminal">The terminal to draw to.</param>
    /// <param name="settings">The loaded configuration.</param>
    /// <param name="keyMaps">The loaded key bindings.</param>
    /// <param name="commands">The command registry.</param>
    /// <param name="cache">Where directories come from.</param>
    /// <param name="startPath">Where to open.</param>
    /// <param name="fileSystem">The filesystem to work against.</param>
    /// <param name="runner">Runs external programs.</param>
    /// <param name="opener">Decides which program opens a file.</param>
    /// <param name="previews">Produces file previews, or <see langword="null"/> for none.</param>
    /// <param name="images">Draws image previews, or <see langword="null"/> to show none.</param>
    /// <param name="bookmarks">Where bookmarks are kept.</param>
    /// <param name="tags">Where tags are kept.</param>
    /// <param name="linemodes">Row-rendering modes, including any a plugin has added.</param>
    /// <param name="clipboard">
    /// The system clipboard, or <see langword="null"/> to find a helper program the usual way.
    /// </param>
    public Browser(Terminal terminal, CangerSettings settings, KeyMaps keyMaps,
                   CommandRegistry commands, DirectoryCache cache, string startPath,
                   IFileSystem fileSystem, IProcessRunner runner, IFileOpener opener,
                   IPreviewProvider? previews = null, IImageDisplay? images = null,
                   Bookmarks? bookmarks = null, Tags? tags = null,
                   LinemodeRegistry? linemodes = null, IClipboard? clipboard = null)
    {
        Clipboard = clipboard ?? new SystemClipboard();
        Previews = previews;
        _images = images;
        Bookmarks = bookmarks ?? new Bookmarks(fileSystem, "/dev/null");
        Tags = tags ?? new Tags("/dev/null");

        // The fileinfo linemode needs a describer, and the describer needs to be able to ask for
        // a redraw once an answer arrives; registering a configured instance over the built-in is
        // how the mode is switched on.
        Metadata = new MetadataManager(fileSystem);
        _describer = new FileDescriber(RequestRedraw);

        // The caller may pass a registry plugins have already added to; registering the
        // describer-backed mode over the built-in is what switches `fileinfo` on either way.
        LinemodeRegistry registry = linemodes ?? new LinemodeRegistry();
        registry.Register(new FileInfoLinemode(_describer));
        Linemodes = new LinemodeSelector(registry, Tags, Metadata);

        // Only built when asked for: the worker thread and the repository walk are pure cost in
        // a session that never looks at a repository.
        if (settings.VcsAware)
        {
            Vcs = new VcsService(RequestRedraw);
            ApplyVcsBackendSettings(settings);
        }
        Runner = runner ?? throw new ArgumentNullException(nameof(runner));

        // Every file opened through rifle runs a program without coming back through this class,
        // so these are the only notifications that the screen has been taken and returned.
        Runner.Suspending += (_, _) =>
        {
            // The image is drawn by a helper outside the screen buffer, so clearing the buffer
            // does not remove it — it would sit on top of whatever gets the terminal next.
            _images?.Clear();
            _shownImage = null;
        };

        Runner.Resumed += (_, _) =>
        {
            Volatile.Write(ref _needsFullRepaint, true);

            // The image was cleared on the way out and the helper has been told nothing since,
            // so it has to be sent again rather than assumed to still be there.
            _shownImage = null;
        };
        Opener = opener ?? throw new ArgumentNullException(nameof(opener));
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        KeyMaps = keyMaps ?? throw new ArgumentNullException(nameof(keyMaps));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Directories = cache ?? throw new ArgumentNullException(nameof(cache));
        FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

        _screen = new ScreenBuffer(terminal.Width, terminal.Height);
        _keys = new KeyBuffer(keyMaps.Browser);
        _consoleKeys = new KeyBuffer(keyMaps.Console);
        _pagerKeys = new KeyBuffer(keyMaps.Pager);
        _dispatcher = new CommandDispatcher(this, commands);

        // Wrapped so that :set colorscheme takes effect at once: the widgets keep this one
        // reference and the wrapper swaps what is behind it.
        _colorSchemes = new ColorSchemeRegistry();
        SwitchableColorScheme colorScheme =
            new(_colorSchemes.CreateOrDefault(settings.Colorscheme));
        _colorScheme = colorScheme;
        _view = new MillerView(colorScheme);
        _multipaneView = new MultipaneView(colorScheme);
        _titleBar = new TitleBar(colorScheme);
        _statusBar = new StatusBar(colorScheme);
        _console = new ConsoleWidget(colorScheme) { IsVisible = false };
        _pager = new Pager(colorScheme) { IsVisible = false };
        _taskView = new TaskView(colorScheme, Tasks) { IsVisible = false };
        _hints = new HintWindow(colorScheme) { IsVisible = false };
        _bookmarkWindow = new BookmarkWindow(colorScheme) { IsVisible = false };
        _taskViewKeys = new KeyBuffer(keyMaps.TaskView);

        _tabs = new Dictionary<int, Tab>
        {
            [1] = new Tab(cache, startPath, settings.MaxHistorySize ?? 20),
        };

        // What makes `setinregex`, `setinpath` and `setintag` mean anything: without a path to
        // resolve against, every read is the global value and a rule scoped to a directory is
        // stored and never consulted. Read through a function so the answer is current at the
        // moment of the read, since settings are read between frames as well as during them.
        Settings.CurrentPath = () =>
            _tabs.TryGetValue(CurrentTabNumber, out Tab? tab) ? tab.Path : null;

        ApplySettingsToDirectory();

        foreach (Tab tab in _tabs.Values)
        {
            tab.Left += (_, from) => Bookmarks.RememberPrevious(from);
        }

        _terminal.Resized += (_, size) =>
        {
            _screen.Resize(size.Width, size.Height);

            // Resizing the buffer is not enough on its own, and both halves of this were
            // missing. The signal arrives on the runtime's signal thread while the main loop is
            // blocked in `WaitForInput` for up to the whole idle delay, so the flag alone would
            // change nothing until the next keystroke — which is what the user saw: a wrapped,
            // garbled screen that came right only when they pressed something. And a diffed
            // flush would repaint only the cells the new layout happens to differ in, over a
            // terminal that has already reflowed everything, so the frame has to be forced.
            Volatile.Write(ref _needsFullRepaint, true);
            RequestRedraw();
        };
    }

    // ---- IFileManager ----------------------------------------------------------------

    /// <inheritdoc />
    public Tab CurrentTab => _tabs[CurrentTabNumber];

    /// <inheritdoc />
    public IReadOnlyDictionary<int, Tab> Tabs => _tabs;

    /// <inheritdoc />
    public int CurrentTabNumber { get; private set; } = 1;

    /// <inheritdoc />
    public CangerSettings Settings { get; }

    /// <inheritdoc />
    public DirectoryCache Directories { get; }

    /// <inheritdoc />
    public IFileSystem FileSystem { get; }

    /// <inheritdoc />
    public TaskQueue Tasks { get; } = new();

    /// <inheritdoc />
    public KeyMaps KeyMaps { get; }

    /// <inheritdoc />
    public CommandRegistry Commands { get; }

    /// <inheritdoc />
    public IProcessRunner Runner { get; }

    /// <inheritdoc />
    public IClipboard Clipboard { get; }

    /// <inheritdoc />
    public IFileOpener Opener { get; }

    /// <summary>Produces file previews, or <see langword="null"/> when previews are off.</summary>
    public IPreviewProvider? Previews { get; }

    /// <summary>
    /// Tabs that have been closed, most recent last.
    /// </summary>
    /// <remarks>
    /// Closing a tab throws away a cursor position and a history that may have taken a while to
    /// build, so it is kept rather than dropped: <c>uq</c> puts it back.
    /// </remarks>
    private readonly List<Tab> _closedTabs = [];

    /// <inheritdoc />
    public void OpenTab(int number, string? path = null)
    {
        int previous = CurrentTabNumber;

        if (!_tabs.TryGetValue(number, out Tab? tab))
        {
            // A new tab starts where the current one is, so opening one keeps a place rather
            // than losing it, and inherits the history that led there.
            tab = new Tab(Directories, CurrentTab.Path, Settings.MaxHistorySize ?? 20);
            tab.History.InheritFrom(CurrentTab.History);
            _tabs[number] = tab;
        }

        CurrentTabNumber = number;

        if (path is not null)
        {
            tab.Enter(path);
        }

        if (previous != number)
        {
            tab.Activate();
        }
    }

    /// <inheritdoc />
    public bool CloseTab(int? number = null)
    {
        int target = number ?? CurrentTabNumber;

        // The last tab is never closed: there would be nothing left to show.
        if (_tabs.Count <= 1 || !_tabs.TryGetValue(target, out Tab? tab))
        {
            return false;
        }

        _tabs.Remove(target);
        _closedTabs.Add(tab);

        if (target == CurrentTabNumber)
        {
            CurrentTabNumber = NeighbourOf(target);
            CurrentTab.Activate();
        }

        if (Settings.RenumberTabsOnTabClose)
        {
            RenumberTabs();
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

        // Whatever is already there moves aside rather than being overwritten, cascading until
        // an empty number is found — so no tab is lost to a shift.
        Tab moving = CurrentTab;
        int from = CurrentTabNumber;
        _tabs.Remove(from);
        MakeRoom(number, number > from ? -1 : 1);
        _tabs[number] = moving;
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

        // The lowest free number, so a restored tab lands where a new one would.
        int number = 1;

        while (_tabs.ContainsKey(number))
        {
            number++;
        }

        _tabs[number] = tab;
        CurrentTabNumber = number;
        tab.Activate();
        return true;
    }

    /// <summary>Empties a tab number by pushing whatever holds it along.</summary>
    /// <param name="number">The number to free.</param>
    /// <param name="direction">Which way the displaced tabs move.</param>
    private void MakeRoom(int number, int direction)
    {
        if (!_tabs.TryGetValue(number, out Tab? occupant))
        {
            return;
        }

        MakeRoom(number + direction, direction);
        _tabs.Remove(number);
        _tabs[number + direction] = occupant;
    }

    /// <summary>Renumbers the tabs so they run consecutively from one.</summary>
    /// <summary>
    /// The tab to land on after closing one.
    /// </summary>
    /// <param name="closed">The number of the tab that has just been removed.</param>
    /// <returns>The number of the tab to make current.</returns>
    /// <remarks>
    /// The one after it, or the one before when the closed tab was the last. Ranger says the same
    /// thing the other way round — it moves off the tab *before* deleting it, backwards if the tab
    /// is last in the list and forwards otherwise (<c>core/actions.py:1283-1288</c>).
    ///
    /// Canger jumped to the lowest-numbered tab instead, so closing tab 5 of five landed on tab 1.
    /// Closing a tab should leave you next to where you were, not at the beginning.
    /// </remarks>
    private int NeighbourOf(int closed) => NeighbourOf([.. _tabs.Keys.Order()], closed);

    /// <inheritdoc cref="NeighbourOf(int)"/>
    /// <param name="remaining">The numbers of the surviving tabs, in ascending order.</param>
    /// <param name="closed">The number of the tab that has just been removed.</param>
    internal static int NeighbourOf(IReadOnlyList<int> remaining, int closed)
    {
        foreach (int number in remaining)
        {
            if (number > closed)
            {
                return number;
            }
        }

        // Nothing after it, so the closed tab was the last one: step back instead.
        return remaining[^1];
    }

    private void RenumberTabs()
    {
        int[] ordered = [.. _tabs.Keys.Order()];
        List<Tab> tabs = [.. ordered.Select(n => _tabs[n])];
        int current = Math.Max(Array.IndexOf(ordered, CurrentTabNumber), 0) + 1;

        _tabs.Clear();

        for (int i = 0; i < tabs.Count; i++)
        {
            _tabs[i + 1] = tabs[i];
        }

        CurrentTabNumber = current;
    }

    /// <inheritdoc />
    public Bookmarks Bookmarks { get; }

    /// <inheritdoc />
    public Tags Tags { get; }

    /// <inheritdoc />
    public LinemodeSelector Linemodes { get; }

    /// <inheritdoc />
    public MetadataManager Metadata { get; }

    /// <inheritdoc />
    public VcsService? Vcs { get; }

    /// <inheritdoc />
    public IReadOnlyList<FsNode> CopyBuffer { get; private set; } = [];

    /// <summary>The same buffer as a set of paths, for the listing to dim as it draws.</summary>
    /// <remarks>
    /// Kept in step with <see cref="CopyBuffer"/> rather than rebuilt per frame. Ranger rebuilds
    /// its list on every draw (<c>gui/widgets/browsercolumn.py:294</c>), which it can afford at
    /// its refresh rate; this is the same answer without paying for it sixty times a second.
    /// </remarks>
    private readonly HashSet<string> _copyBufferPaths = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsCutPending { get; private set; }

    /// <inheritdoc />
    public void Notify(string message, bool isError = false)
    {
        _message = message;
        _messageIsError = isError;
        _messageExpiresAt = Environment.TickCount64 + MessageMilliseconds;

        // Kept so `:display_log` can show what scrolled past. Bounded, because a session left
        // open all week should not accumulate messages without limit; the oldest are the least
        // interesting.
        _messageLog.Add(isError ? "error: " + message : message);

        if (_messageLog.Count > MessageLogLimit)
        {
            _messageLog.RemoveRange(0, _messageLog.Count - MessageLogLimit);
        }
    }

    /// <summary>How many messages are remembered for <c>:display_log</c>.</summary>
    private const int MessageLogLimit = 1000;

    private readonly List<string> _messageLog = [];

    /// <inheritdoc />
    public IReadOnlyList<string> MessageLog => _messageLog;

    /// <inheritdoc />
    /// <remarks>Ranger's default, from <c>core/fm.py:81</c>.</remarks>
    public string SearchMethod { get; set; } = "ctime";

    /// <inheritdoc />
    public bool IsVisualMode => _visualStart is not null;

    /// <summary>Where the cursor was when visual mode began.</summary>
    private int? _visualStart;

    /// <summary>The file the cursor was on when visual mode began.</summary>
    /// <remarks>
    /// The anchor's real identity. <see cref="_visualStart"/> is only where it was standing, and
    /// rows move when a listing changes underneath a selection — see
    /// <see cref="VisualRange.AnchorIndex"/>.
    /// </remarks>
    private string? _visualStartPath;

    /// <summary>The directory visual mode began in, and the tab it was shown in.</summary>
    /// <remarks>
    /// A visual selection is a range of rows in one listing, so it means nothing anywhere else.
    /// Leaving either ends the mode — see <see cref="LeaveVisualModeIfMoved"/>.
    /// </remarks>
    private DirectoryNode? _visualDirectory;

    /// <inheritdoc cref="_visualDirectory"/>
    private int _visualTab;

    /// <summary>What was already marked when visual mode began.</summary>
    private HashSet<string>? _selectionBeforeVisual;

    /// <summary>Whether visual mode is unmarking rather than marking.</summary>
    private bool _visualReverse;

    /// <inheritdoc />
    /// <remarks>
    /// <c>visual-reverse</c> is Canger's spelling of ranger's <c>toggle_visual_mode
    /// reverse=True</c>: the same visual mode, but moving unmarks rather than marks.
    /// </remarks>
    public void ChangeMode(string mode) =>
        ChangeMode(mode == "visual-reverse" ? "visual" : mode,
                   reverse: mode == "visual-reverse");

    /// <summary>Turns visual mode on or off, optionally to unmark rather than mark.</summary>
    /// <param name="mode"><c>visual</c> or <c>normal</c>.</param>
    /// <param name="reverse">Whether moving unmarks instead of marking.</param>
    internal void ChangeMode(string mode, bool reverse)
    {
        switch (mode)
        {
            case "visual" when _visualStart is null:
                _visualStart = CurrentTab.Current.Cursor.Index;
                _visualStartPath = CurrentTab.Selected?.Path;
                _visualDirectory = CurrentTab.Current;
                _visualTab = CurrentTabNumber;
                _visualReverse = reverse;
                _selectionBeforeVisual =
                    [.. CurrentTab.Current.MarkedEntries.Select(e => e.Path)];

                // The entry the selection starts from is acted on at once, so a visual selection
                // of one file is possible without moving.
                if (CurrentTab.Selected is { } start)
                {
                    start.IsMarked = !reverse;
                }

                break;

            case "normal" when _visualStart is not null:
                _visualStart = null;
                _visualStartPath = null;
                _visualDirectory = null;
                _selectionBeforeVisual = null;
                break;

            default:
                // Either an unknown mode, or the mode asked for is the one already in effect.
                return;
        }

        RequestRedraw();
    }

    /// <summary>
    /// Extends a visual selection to wherever the cursor has just moved.
    /// </summary>
    /// <param name="cursorPathBefore">The file the cursor was on before the command ran.</param>
    /// <remarks>
    /// <para>
    /// Marks everything between the anchor and the cursor and unmarks everything outside it,
    /// except what was already marked before the mode began — so moving back over your own path
    /// deselects, but an unrelated earlier mark survives.
    /// </para>
    /// <para>
    /// Only when the cursor actually moved, which is the whole reason for
    /// <paramref name="cursorPathBefore"/>. Ranger sweeps from inside <c>move</c> itself
    /// (<c>core/actions.py:522-559</c>), so a listing that gains a file while the cursor sits
    /// still is never re-swept and the new file stays unmarked. Sweeping after every command
    /// instead meant re-deriving the range from whatever now lay between the two ends: a file
    /// written into the middle of a selection — the usual way being a command that generates one —
    /// joined it by itself.
    /// </para>
    /// <para>
    /// Compared by path rather than by reference: a reload rebuilds the listing and carries marks
    /// across by path (<c>DirectoryNode.RestoreMarks</c>), so the same file is not the same
    /// object afterwards and reference equality would read every reload as a movement.
    /// </para>
    /// </remarks>
    private void UpdateVisualSelection(string? cursorPathBefore)
    {
        LeaveVisualModeIfMoved();

        if (_visualStart is not { } fallback)
        {
            return;
        }

        if (string.Equals(CurrentTab.Selected?.Path, cursorPathBefore, StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyList<FsNode> entries = CurrentTab.Current.Entries;

        VisualRange.Apply(entries, VisualRange.AnchorIndex(entries, _visualStartPath, fallback),
                          CurrentTab.Current.Cursor.Index, _visualReverse, _selectionBeforeVisual);
    }

    /// <summary>Ends visual mode once the cursor has left the listing it started in.</summary>
    /// <remarks>
    /// <para>
    /// A visual selection is a range of rows in one directory. The anchor is a row number, so in
    /// any other listing it points at an unrelated file — which is exactly what happened: pressing
    /// <c>h</c> during a selection carried the mode into the parent, where the next movement
    /// marked a fresh range, and again in its parent, with no way back short of quitting. Marks
    /// left behind in directories the user never selected anything in are also what made a large
    /// tree crawl, since every frame re-reads the whole marked set.
    /// </para>
    /// <para>
    /// Ranger reaches the same end by calling <c>change_mode('normal')</c> from nine different
    /// places — moving left, <c>move_parent</c>, <c>enter_dir</c>, <c>traverse</c> both ways,
    /// two tab paths, <c>reset</c> and <c>open_console</c> (<c>core/actions.py</c>). Asking
    /// whether we are still where we started covers all of those at once, including any route
    /// nobody has thought of yet.
    /// </para>
    /// </remarks>
    private void LeaveVisualModeIfMoved()
    {
        if (_visualStart is null)
        {
            return;
        }

        if (!ReferenceEquals(_visualDirectory, CurrentTab.Current)
            || _visualTab != CurrentTabNumber)
        {
            ChangeMode("normal", reverse: false);
        }
    }

    /// <inheritdoc />
    public void ScrollPreview(int lines) => _view.ScrollPreview(lines);

    /// <inheritdoc />
    public event EventHandler<string>? DirectoryEntered;

    /// <summary>The last path the change event was raised for.</summary>
    private string? _announcedPath;

    /// <summary>
    /// Raises <see cref="DirectoryEntered"/> if the current directory has changed.
    /// </summary>
    /// <remarks>
    /// Checked from the loop rather than raised from each of the many places that can change
    /// directory — <c>:cd</c>, moving up or in, a bookmark, a tab switch, a plugin. One place that
    /// notices is more reliable than a dozen that must all remember to announce.
    /// </remarks>
    private void AnnounceDirectory()
    {
        string path = CurrentTab.Path;

        if (string.Equals(path, _announcedPath, StringComparison.Ordinal))
        {
            return;
        }

        _announcedPath = path;
        DirectoryEntered?.Invoke(this, path);
    }

    /// <inheritdoc />
    public void Quit() => _running = false;

    /// <summary>
    /// Puts the cursor on a named file, entering its directory if that is not where we are.
    /// </summary>
    /// <param name="path">The file to select.</param>
    /// <returns><see langword="true"/> when the file was found and selected.</returns>
    /// <remarks>
    /// This is what <c>--selectfile</c> needs: another program hands Canger a path and expects
    /// the user to be looking straight at it.
    /// </remarks>
    public bool SelectPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? directory = Path.GetDirectoryName(path);

        if (directory is not null &&
            !string.Equals(directory, CurrentTab.Path, StringComparison.Ordinal))
        {
            CurrentTab.Enter(directory);
        }

        FsNode? target = CurrentTab.Current.Entries.FirstOrDefault(
            e => string.Equals(e.Path, path, StringComparison.Ordinal));

        if (target is null)
        {
            return false;
        }

        CurrentTab.MoveCursorTo(target);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CommandHistory => _console.CommandHistory.Entries;

    /// <summary>
    /// Where the console history is kept, or <see langword="null"/> to keep none.
    /// </summary>
    /// <remarks>
    /// Set by the composition root, which is the only thing that knows the data directory and
    /// whether <c>--clean</c> was given.
    /// </remarks>
    public string? ConsoleHistoryPath { get; set; }

    /// <summary>
    /// Reads the saved console history, if there is a file to read.
    /// </summary>
    /// <remarks>
    /// Not gated on <c>save_console_history</c>: ranger loads whatever is there and lets the
    /// setting decide only whether anything is written back.
    /// </remarks>
    public void LoadConsoleHistory()
    {
        _console.SetHistoryCapacity(Settings.MaxConsoleHistorySize);

        if (ConsoleHistoryPath is not { Length: > 0 } path)
        {
            return;
        }

        if (Core.State.ConsoleHistoryFile.Load(_console.CommandHistory, path) is { } error)
        {
            Notify($"could not read the console history: {error}", isError: true);
        }
    }

    /// <summary>Writes the console history out, when the settings ask for it.</summary>
    private void SaveConsoleHistory()
    {
        if (ConsoleHistoryPath is not { Length: > 0 } path || !Settings.SaveConsoleHistory)
        {
            return;
        }

        // Nothing to report a failure to at this point — the interface is going away — so a
        // problem here is swallowed rather than written over a screen that is being torn down.
        Core.State.ConsoleHistoryFile.Save(_console.CommandHistory, path);
    }

    /// <inheritdoc />
    public void OpenConsole(string text = "", int cursorPosition = -1)
    {
        // Typing a command is not extending a selection, and whatever is typed will most likely
        // act on what is already marked (ranger, `core/actions.py:228`).
        ChangeMode("normal", reverse: false);

        _console.Open(text, cursorPosition);
        _consoleKeys.Clear();
    }

    /// <inheritdoc />
    public void SetCopyBuffer(IEnumerable<FsNode> files, bool cut)
    {
        CopyBuffer = [.. files];
        IsCutPending = cut;

        _copyBufferPaths.Clear();
        foreach (FsNode file in CopyBuffer)
        {
            _copyBufferPaths.Add(file.Path);
        }
    }

    /// <inheritdoc />
    public bool Execute(string line, int? quantifier = null, IReadOnlyList<int>? wildcards = null) =>
        _dispatcher.Execute(line, quantifier, wildcards);

    /// <summary>Records where the user is leaving, for the previous-directory bookmark.</summary>
    /// <param name="from">The directory being left.</param>
    public void RememberPreviousDirectory(string from) => Bookmarks.RememberPrevious(from);

    /// <inheritdoc />
    /// <remarks>
    /// The screen less the title bar and the status bar, which is ranger's <c>browser.hei</c>
    /// — the container's height rather than a column's, so borders do not change it.
    /// </remarks>
    public int BrowserHeight => Math.Max(_screen.Height - 2, 1);

    /// <inheritdoc />
    public void ReloadCurrentDirectory()
    {
        CurrentTab.Current.Load();
        ApplySettingsToDirectory();
    }

    /// <summary>Re-reads every directory the user can currently see.</summary>
    /// <remarks>
    /// <para>
    /// A command that changes files is not confined to the directory the cursor happens to be in.
    /// A tool that walks a tree, or one given a path in another column, leaves the rest of the
    /// screen showing what was true before it ran — and because staleness is judged by the
    /// directory's own modification time, nothing later notices: <c>chmod</c> changes a file's
    /// ctime and leaves the directory's mtime alone, so the listing never looks out of date.
    /// Ranger has the same blind spot for the same reason
    /// (<c>container/directory.py:700</c>).
    /// </para>
    /// <para>
    /// Deliberately what is <em>visible</em> rather than what is loaded. The cache never evicts,
    /// so after a long session "everything loaded" is hundreds of directories — a synchronous
    /// re-scan of all of them would stall the interface, and it would reach paths on media that
    /// has since been unplugged or on a mount that has stopped answering, with nothing the user
    /// could press to get out of it. The columns on screen are few, and they are by definition
    /// the ones being looked at.
    /// </para>
    /// </remarks>
    public void ReloadVisibleDirectories()
    {
        foreach (DirectoryNode directory in
                 VisibleDirectories(CurrentTab, Tabs, Settings.Viewmode))
        {
            directory.Load();
        }

        ApplySettingsToDirectory();
    }

    /// <summary>The directories any tab is standing on, drawn or not.</summary>
    /// <param name="tabs">Every open tab.</param>
    /// <returns>Each directory once.</returns>
    /// <remarks>
    /// A superset of <see cref="VisibleDirectories"/>, and the right set to protect from the idle
    /// sweep. Unloading is cheap to undo, so the background tabs could be swept too — but the
    /// saving is in the hundreds of directories nobody is sitting on, not in the handful a tab is,
    /// and sweeping those would put a scan in front of every tab switch for nothing. Ranger draws
    /// the line in the same place: <c>any(value in tab.pathway for tab in self.tabs.values())</c>
    /// (<c>core/fm.py:480-481</c>).
    /// </remarks>
    internal static IEnumerable<DirectoryNode> RetainedDirectories(
        IReadOnlyDictionary<int, Tab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        HashSet<DirectoryNode> seen = [];

        foreach (Tab tab in tabs.Values)
        {
            foreach (DirectoryNode directory in tab.Pathway)
            {
                if (seen.Add(directory))
                {
                    yield return directory;
                }
            }

            if (tab.SelectedDirectory is { } selected && seen.Add(selected))
            {
                yield return selected;
            }
        }
    }

    /// <summary>How long a listing goes untouched before the sweep may drop it.</summary>
    /// <remarks>Ranger's <c>TIME_BEFORE_FILE_BECOMES_GARBAGE</c>, rather than a fresh guess.</remarks>
    private static readonly TimeSpan IdleBeforeUnload = TimeSpan.FromSeconds(1200);

    /// <summary>How often the sweep is worth running.</summary>
    /// <remarks>
    /// It walks every cached directory, so it is not something to do per frame. A minute is far
    /// finer than the twenty it is looking for, and the walk itself is a comparison per entry.
    /// </remarks>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>When the sweep last ran.</summary>
    private DateTimeOffset _lastSweep = DateTimeOffset.UtcNow;

    /// <summary>Lets go of listings for directories left alone long enough.</summary>
    /// <remarks>
    /// The cache holds every directory of the session, so without this a long session keeps
    /// growing — roughly a kilobyte per entry ever scanned, forty megabytes for forty thousand.
    /// It does not hand memory back to the system, which .NET will not do; it stops the heap
    /// having to grow to hold listings nobody is going to look at again.
    /// </remarks>
    private void UnloadIdleDirectories()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (now - _lastSweep < SweepInterval)
        {
            return;
        }

        _lastSweep = now;
        Directories.UnloadIdle(new HashSet<DirectoryNode>(RetainedDirectories(Tabs)),
                               now - IdleBeforeUnload);
    }

    /// <summary>The directories the current view is drawing.</summary>
    /// <param name="current">The tab whose columns are on screen.</param>
    /// <param name="tabs">Every open tab, for the view mode that shows them all at once.</param>
    /// <param name="viewmode">The <c>viewmode</c> setting.</param>
    /// <returns>Each directory once, however many columns happen to show it.</returns>
    /// <remarks>
    /// Static, and given everything it needs, so the rule can be checked without standing up a
    /// terminal. It is the same set <see cref="ApplySettingsToDirectory"/> walks, which is not a
    /// coincidence: both answer "what is the user looking at".
    /// </remarks>
    internal static IEnumerable<DirectoryNode> VisibleDirectories(
        Tab current, IReadOnlyDictionary<int, Tab> tabs, string? viewmode)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(tabs);

        HashSet<DirectoryNode> seen = [];

        // The ancestry columns and the current directory; `Pathway` ends with the one the cursor
        // is in. Then the preview column, which is a directory only when the cursor is on one.
        foreach (DirectoryNode directory in current.Pathway)
        {
            if (seen.Add(directory))
            {
                yield return directory;
            }
        }

        if (current.SelectedDirectory is { } selected && seen.Add(selected))
        {
            yield return selected;
        }

        // In multipane every tab is a column, so every tab's directory is on screen.
        if (viewmode is "multipane")
        {
            foreach (Tab tab in tabs.Values)
            {
                if (seen.Add(tab.Current))
                {
                    yield return tab.Current;
                }
            }
        }
    }

    /// <inheritdoc />
    public void ReloadDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Through the cache, so this is the same node every tab and column is holding rather
        // than a fresh one nobody is looking at.
        if (string.Equals(path, CurrentTab.Path, StringComparison.Ordinal))
        {
            ReloadCurrentDirectory();
        }
        else
        {
            Directories.Get(path).Load();
        }

        // Called from a finished task, which is not a keystroke: without this the new files sit
        // in the model until the user happens to press something.
        RequestRedraw();
    }

    /// <summary>
    /// Asks for the next frame to be drawn now rather than after the idle wait.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread: it only sets a flag the main loop reads. This is how work
    /// that finishes in the background — a preview, a version-control status, a file description
    /// — gets on screen without the user pressing a key first.
    /// </remarks>
    public void RequestRedraw()
    {
        Volatile.Write(ref _needsRedraw, true);

        // The flag alone is not enough: the loop may already be waiting, and a wait that has
        // begun can only be ended by something the poll is watching.
        Terminal.Wake();
    }

    /// <inheritdoc />
    public void Redraw()
    {
        StringBuilder output = new();
        _screen.Flush(output, force: true);
        _terminal.Write(output.ToString());
    }

    /// <inheritdoc />
    public QueuedTask RunInBackground(string description, string command,
                                      string? workingDirectory = null,
                                      Action<CommandTask>? finished = null)
    {
        CommandTask task = new(
            Runner,
            new ProcessRequest(command, default, workingDirectory ?? CurrentTab.Path),
            description,
            (message, isError) => Notify(message, isError),
            finished);

        return Tasks.Add(task);
    }

    /// <inheritdoc />
    public void ShowInExternalPager(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // `$PAGER` may carry arguments — `less -R` is common — so only its first word names the
        // program to look for. Ranger's default is the same (`ranger/__init__.py:53`).
        string configured = Environment.GetEnvironmentVariable("PAGER") is { Length: > 0 } set
            ? set
            : DefaultPager;
        string program = configured.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                   .FirstOrDefault() ?? DefaultPager;

        // Ranger runs `$PAGER` unconditionally and shows nothing when it is missing. Falling back
        // means help still opens on a system without one — worse than `less`, better than silence.
        if (!Executables.Exists(program))
        {
            ShowInPager(text);
            return;
        }

        string path = Path.Join(Path.GetTempPath(), "canger-" + Path.GetRandomFileName());

        try
        {
            // CreateNew rather than Create: it fails rather than following a symbolic link
            // somebody left at the name we picked. The mode is set before anything is written,
            // so the contents are never briefly world-readable.
            using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write,
                                         FileShare.None))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

                using StreamWriter writer = new(file);
                writer.Write(text);
            }

            RunProgram($"{configured} {MacroExpander.ShellQuote(path)}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write means no external pager, not no help.
            ShowInPager(text);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A file left in the temporary directory is not worth reporting.
            }
        }
    }

    /// <summary>The pager used when <c>$PAGER</c> says nothing, as in ranger.</summary>
    private const string DefaultPager = "less";

    /// <inheritdoc />
    public void RunProgram(string command, string flags = "")
    {
        ProcessResult result = Runner.Run(new ProcessRequest(
            command, new ProcessFlags(flags), CurrentTab.Path));

        if (result.Error is { } error)
        {
            Notify(error, isError: true);
        }
        else if (result.Output.Length > 0)
        {
            // Output that would not fit on the status line goes to the pager instead of being
            // truncated to its first line.
            ShowInPager(result.Output);
        }
        else if (result.ExitCode is { } code and not 0)
        {
            // A program that had the terminal wrote its own complaint there, and the repaint
            // that follows wipes it out before it can be read. Saying what happened on the
            // status line is the difference between a mystery and a diagnosis.
            Notify(Describe(command, code), isError: true);
        }

        // The program may have written over the screen, so nothing less than a full repaint is
        // safe. The directories may also have changed under it — every one on show, not just the
        // one the cursor is in, since a program is free to touch anything it was pointed at.
        ReloadVisibleDirectories();
        Redraw();
    }

    /// <summary>How long a free-space reading is reused before it is taken again.</summary>
    /// <remarks>
    /// Free space is a number that drifts slowly and is shown in the corner of one line, so a
    /// reading a couple of seconds old is indistinguishable from a fresh one. It was being taken
    /// with a real <c>statvfs</c> on every frame, and not even gated on whether the status bar
    /// was going to show it.
    /// </remarks>
    private static readonly TimeSpan FreeSpaceLifetime = TimeSpan.FromSeconds(2);

    private long? _freeBytes;
    private string? _freeBytesPath;
    private long _freeBytesTaken;

    /// <summary>The free space on the current tab's filesystem, or null when it is not shown.</summary>
    private long? FreeSpace()
    {
        if (!Settings.DisplayFreeSpaceInStatusBar)
        {
            return null;
        }

        string path = CurrentTab.Path;
        long now = Environment.TickCount64;

        if (!string.Equals(path, _freeBytesPath, StringComparison.Ordinal)
            || now - _freeBytesTaken > FreeSpaceLifetime.TotalMilliseconds)
        {
            _freeBytesPath = path;
            _freeBytesTaken = now;
            _freeBytes = FileSystem.GetDiskUsage(path)?.FreeBytes;
        }

        return _freeBytes;
    }

    /// <summary>
    /// Says what a non-zero exit meant, in the terms the shell uses.
    /// </summary>
    /// <remarks>
    /// 127 and 126 are worth naming because they are almost always a configuration problem rather
    /// than the program's own failure, and because the shell's own message has just been painted
    /// over. This once printed the whole PATH as well, which was what proved a window-manager
    /// launch inherited a bare one; the launcher script sets the PATH now, and a hundred
    /// characters of it only crowded the real message off the status line.
    /// </remarks>
    private static string Describe(string command, int exitCode)
    {
        string program = command.TrimStart().Split(' ', 2)[0];

        return exitCode switch
        {
            127 => $"{program}: not found on PATH",
            126 => $"{program}: found but not executable",
            _ => $"{program}: exited {exitCode.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>Shows text full screen, with somewhere to scroll.</summary>
    /// <param name="text">What to show.</param>
    public void ShowInPager(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Trim().Length == 0)
        {
            return;
        }

        _pager.SetText(text);
        _pager.IsVisible = true;
        _pagerKeys.Clear();
    }

    /// <summary>Closes the pager.</summary>
    public void ClosePager()
    {
        _pager.IsVisible = false;
        _pager.Clear();
    }

    /// <summary>Whether the pager is showing.</summary>
    public bool IsPagerOpen => _pager.IsVisible;

    /// <inheritdoc />
    public void InvalidatePreviews()
    {
        if (Previews is Canger.Preview.ScriptPreviewProvider provider)
        {
            provider.InvalidateAll();
        }
    }

    /// <inheritdoc />
    public void Ask(string question, Action<char> callback, IReadOnlyList<char>? choices = null)
    {
        _questionCallback = callback;
        _console.Ask(question, choices);
    }

    // ---- The loop --------------------------------------------------------------------

    /// <summary>
    /// Releases the background workers the browser owns.
    /// </summary>
    /// <remarks>
    /// The terminal is deliberately not restored here: whoever put it into raw mode restores it,
    /// so that ownership stays in one place.
    /// </remarks>
    public void Dispose()
    {
        SaveConsoleHistory();
        _describer.Dispose();
        Vcs?.Dispose();
    }

    /// <summary>
    /// Raised once the interface is up and the first directory is drawn.
    /// </summary>
    /// <remarks>
    /// This is what a plugin's <c>OnReady</c> hangs off. It fires after the first paint rather
    /// than before, so a plugin that draws or asks something has a screen to do it on.
    /// </remarks>
    public event EventHandler? Ready;

    /// <summary>Turns on whichever backends the settings enable.</summary>
    /// <remarks>
    /// <c>local</c> and <c>enabled</c> both mean the backend is used; ranger distinguishes them
    /// only for whether remote status is fetched, which Canger never does synchronously anyway.
    /// </remarks>
    private void ApplyVcsBackendSettings(CangerSettings settings)
    {
        if (Vcs is not { } service)
        {
            return;
        }

        service.EnabledBackends.Clear();

        foreach ((string name, string value) in ((string, string)[])
                 [
                     ("git", settings.VcsBackendGit),
                     ("hg", settings.VcsBackendMercurial),
                     ("svn", settings.VcsBackendSubversion),
                     ("bzr", settings.VcsBackendBazaar),
                 ])
        {
            if (value is "enabled" or "local")
            {
                service.EnabledBackends.Add(name);
            }
        }
    }

    /// <summary>Runs until the user quits.</summary>
    /// <returns>A process exit code.</returns>
    public int Run()
    {
        Draw();
        Ready?.Invoke(this, EventArgs.Empty);

        // Where the session started counts as a place visited, so a plugin keeping a history of
        // directories records it rather than missing the first one.
        AnnounceDirectory();

        // Whatever the handlers just did — a deferred `default_linemode`, a plugin's OnReady —
        // has to be shown now. The loop below blocks for the whole idle delay before drawing
        // again, so without this the first frame the user sees is stale for two seconds.
        Draw();

        while (_running)
        {
            // A short wait while an escape sequence might still be in flight, none at all while
            // there is work to advance, and otherwise however long the idle setting allows.
            // While a job is only waiting on an outside program there is nothing to come back
            // for, so the wait happens here — where a keystroke ends it immediately — instead of
            // inside the job, where it would hold up everything else.
            int timeout = _decoder.HasPendingInput
                ? EscapeDelayMilliseconds
                : Volatile.Read(ref _needsRedraw) ? 0
                : Tasks.HasWork ? (int)Tasks.IdleDelay.TotalMilliseconds
                : Settings.IdleDelay;

            // A message showing has to be taken down on time, and the loop is otherwise asleep
            // for the whole idle delay. Without this it would linger for up to `idle_delay`
            // longer than it should — two seconds by default, which is half again as long as it
            // was meant to be there.
            if (_message is not null)
            {
                long remaining = _messageExpiresAt - Environment.TickCount64;
                timeout = (int)Math.Clamp(remaining, 0, timeout);
            }

            // Something answered from a background thread, so this pass draws rather than
            // waiting out the idle delay with a stale row on screen.
            Volatile.Write(ref _needsRedraw, false);

            if (Terminal.WaitForInput(timeout))
            {
                ReadOnlyMemory<byte> input = _terminal.ReadAsync().AsTask().GetAwaiter().GetResult();
                if (input.Length == 0)
                {
                    break;
                }

                Handle(_decoder.Feed(input.Span));

                // Whatever was typed while that key was being handled is thrown away, so a slow
                // command does not end with a burst of keystrokes running somewhere unintended.
                // Not while the console is open: there the keys are text the user meant to type.
                if (Settings.Flushinput && !_console.IsOpen && !_decoder.HasPendingInput)
                {
                    Terminal.DiscardPendingInput();
                }
            }
            else if (_decoder.HasPendingInput)
            {
                Handle(_decoder.Flush());
            }

            if (Tasks.HasWork)
            {
                _hadWork = true;
                Tasks.Work();
            }
            else if (_hadWork)
            {
                // Work has just finished. Whatever it did — a copy, a move — most likely changed
                // a directory being shown, so the listings are re-read once rather than polled.
                // Every visible one, because a paste lands in a directory the cursor is not in
                // nearly as often as in one it is, and the destination is frequently the column
                // to the right.
                _hadWork = false;
                ReportFinishedWork();
                ReloadVisibleDirectories();
            }
            else
            {
                // Nothing running and nothing just finished, so this is the moment to spend on
                // housekeeping rather than in front of the user.
                UnloadIdleDirectories();
            }

            Draw();
        }

        return 0;
    }

    /// <summary>Says how finished work turned out, then clears it from the queue.</summary>
    private void ReportFinishedWork()
    {
        // One message for the whole run. Reporting each job in turn meant each report replacing
        // the last, so a transfer that had lost a file was reported and then unreported in the
        // same frame by a transfer that finished cleanly beside it.
        if (Core.FileOperations.FinishedWork.Describe(
                [.. Tasks.Tasks.Where(t => t.IsComplete)]) is var (message, isError))
        {
            Notify(message, isError);
        }

        Tasks.RemoveCompleted();
    }

    /// <summary>
    /// Puts pasted text where it was meant to go.
    /// </summary>
    /// <param name="text">What the terminal delivered.</param>
    /// <remarks>
    /// <para>
    /// Canger turns bracketed paste on, so a terminal wraps a paste in markers and it arrives as
    /// one event instead of a burst of keystrokes. Nothing consumed that event, so pasting a path
    /// into <c>:cd</c> did nothing at all — the payload was decoded and then dropped on the floor.
    /// </para>
    /// <para>
    /// Ranger appears to handle this only because it never enables bracketed paste: the terminal
    /// sends the clipboard as ordinary key presses and the console consumes them one by one. That
    /// also means a paste into ranger's *browser* runs whatever bindings those characters happen
    /// to name — a pasted <c>q</c> quits. Keeping bracketed paste and ignoring a paste outside the
    /// prompt is the better behaviour, and is what the event type was introduced for.
    /// </para>
    /// <para>
    /// Newlines become spaces rather than submitting the line. A prompt holds one line, and a
    /// clipboard entry ending in a newline is extremely common — running a half-read command
    /// because of a trailing newline is exactly what bracketed paste exists to prevent.
    /// </para>
    /// </remarks>
    private void HandlePaste(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (!_console.IsOpen)
        {
            // Deliberately not fed to the key handler: see the remarks above.
            Notify("pasted text ignored; open a prompt first");
            return;
        }

        string flattened = text.ReplaceLineEndings(" ").Replace('\t', ' ');

        _console.Insert(flattened);
        RequestRedraw();
    }

    private void Handle(IReadOnlyList<InputEvent> events)
    {
        foreach (InputEvent evt in events)
        {
            if (evt is PasteEvent paste)
            {
                HandlePaste(paste.Text);
                continue;
            }

            if (evt is not KeyEvent key)
            {
                continue;
            }

            _message = null;

            if (_console.IsOpen)
            {
                HandleConsoleKey(key.Key);
                continue;
            }

            // The pager and the task view take the whole screen, so they take the keys too.
            if (_pager.IsVisible)
            {
                HandlePagerKey(key.Key);
                continue;
            }

            if (_taskView.IsVisible)
            {
                HandleTaskViewKey(key.Key);
                continue;
            }

            string? command = _keys.Add(key.Key);
            int? quantifier = _keys.Quantifier;
            IReadOnlyList<int> wildcards = [.. _keys.Wildcards];

            // Hidden before the command runs, never after. A `<bg>` binding's whole job is to
            // turn this back on — `map '<bg> draw_bookmarks` — so clearing it afterwards would
            // undo the one thing the keystroke was for (gui/ui.py:212).
            _showBookmarks = false;

            if (command is not null)
            {
                // Noted before the command runs, so afterwards the selection can tell a movement
                // from a listing that changed on its own. Ranger gets that distinction for free by
                // sweeping inside `move`; this is the price of doing it in one place instead.
                string? cursorBefore = CurrentTab.Selected?.Path;

                _dispatcher.Execute(command, quantifier, wildcards);

                // Ranger extends the selection inside `move` itself; doing it after whatever the
                // key did covers the same ground without every movement command having to know
                // about the mode. Cheap: it is a no-op unless visual mode is on.
                UpdateVisualSelection(cursorBefore);
                AnnounceDirectory();
            }

            if (_keys.IsFinished || _keys.HasFailed)
            {
                _keys.Clear();
            }

            // A sequence that is genuinely part-way through is a question — "what can follow?" —
            // and the hint window answers it. A quantifier still being typed is not: the digits
            // of `12j` mean nothing to list.
            _showHints = !_keys.IsFinished && !_keys.HasFailed
                         && _keys.IsQuantifierFinished && _keys.Keys.Count > 0;
        }
    }

    /// <summary>Opens the task view.</summary>
    public void OpenTaskView()
    {
        _taskView.IsVisible = true;
        _taskViewKeys.Clear();
    }

    /// <summary>Closes the task view.</summary>
    public void CloseTaskView() => _taskView.IsVisible = false;

    /// <summary>Whether the task view is showing.</summary>
    public bool IsTaskViewOpen => _taskView.IsVisible;

    /// <summary>Routes a key while the task view has focus.</summary>
    private void HandleTaskViewKey(int key)
    {
        string? action = _taskViewKeys.Add(key);

        if (action is not null)
        {
            RunTaskViewAction(action);
        }

        if (_taskViewKeys.IsFinished || _taskViewKeys.HasFailed)
        {
            _taskViewKeys.Clear();
        }
    }

    /// <summary>Carries out a task view binding.</summary>
    private void RunTaskViewAction(string action)
    {
        string[] parts = action.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        switch (parts.Length > 0 ? parts[0] : action)
        {
            case "taskview_close":
                CloseTaskView();
                break;

            case "taskview_move":
                MoveTaskViewCursor(parts.Length > 1 ? parts[1] : string.Empty);
                break;

            case "task_move_up":
                _taskView.MoveTask(-1);
                break;

            case "task_move_down":
                _taskView.MoveTask(1);
                break;

            case "task_remove":
                _taskView.CancelSelected();
                break;

            case "task_pause":
                _taskView.TogglePauseSelected();
                break;

            case "redraw_window":
                Redraw();
                break;

            default:
                _dispatcher.Execute(action);
                break;
        }
    }

    /// <summary>Moves the task view cursor according to a movement command's arguments.</summary>
    private void MoveTaskViewCursor(string arguments)
    {
        Dictionary<string, string> named = new(StringComparer.Ordinal);

        foreach (string part in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                named[part[..equals]] = part[(equals + 1)..];
            }
        }

        Direction direction = Direction.FromArguments(named);

        if (direction.IsAbsolute)
        {
            _taskView.MoveCursorToEdge(toEnd: direction.Down < 0);
            return;
        }

        double amount = direction.Down;
        _taskView.MoveCursor((int)(direction.Pages ? amount * Math.Max(_screen.Height - 3, 1) : amount));
    }

    /// <summary>Routes a key while the pager has focus.</summary>
    private void HandlePagerKey(int key)
    {
        string? action = _pagerKeys.Add(key);

        if (action is not null)
        {
            RunPagerAction(action);
        }

        if (_pagerKeys.IsFinished || _pagerKeys.HasFailed)
        {
            _pagerKeys.Clear();
        }
    }

    /// <summary>Carries out a pager binding.</summary>
    private void RunPagerAction(string action)
    {
        string[] parts = action.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        switch (parts.Length > 0 ? parts[0] : action)
        {
            case "pager_close":
                ClosePager();
                break;

            case "pager_move":
                MovePager(parts.Length > 1 ? parts[1] : string.Empty);
                break;

            case "redraw_window":
                Redraw();
                break;

            default:
                // Anything else is an ordinary command, so bindings shared with the browser
                // still work while the pager is up.
                _dispatcher.Execute(action);
                break;
        }
    }

    /// <summary>Scrolls the pager according to a movement command's arguments.</summary>
    private void MovePager(string arguments)
    {
        Dictionary<string, string> named = new(StringComparer.Ordinal);

        foreach (string part in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                named[part[..equals]] = part[(equals + 1)..];
            }
        }

        Direction direction = Direction.FromArguments(named);
        int page = Math.Max(_screen.Height - 2, 1);

        if (direction.IsHorizontal)
        {
            _pager.ScrollHorizontally((int)direction.Right);
            return;
        }

        if (direction.IsAbsolute)
        {
            _pager.ScrollToEdge(toEnd: direction.Down < 0);
            return;
        }

        double amount = direction.Down;
        _pager.ScrollVertically((int)(direction.Pages ? amount * page : amount));
    }

    /// <summary>
    /// Routes a key while the console has focus.
    /// </summary>
    /// <remarks>
    /// Console bindings are consulted first, and anything they do not claim is typed as text.
    /// That is what lets the console accept arbitrary input without a binding for every
    /// printable key.
    /// </remarks>
    private void HandleConsoleKey(int key)
    {
        RouteConsoleKey(key);

        // The overlay belongs to the console, so it goes when the console does — checked here
        // rather than at each place that closes it. Enumerating those is what put this comment
        // here: `console_accept` reaches `ConsoleWidget.Accept`, which closes the widget itself
        // and never touches the browser's own closing path, so running a command from the console
        // left the list of programs on screen with nothing to dismiss it. There are two `Accept`
        // calls and three `Close` calls, and asking about the state afterwards covers all five
        // and anything added later.
        if (!_console.IsOpen)
        {
            _info = null;
        }
    }

    /// <inheritdoc cref="HandleConsoleKey"/>
    private void RouteConsoleKey(int key)
    {
        if (_console.Question is not null)
        {
            if (_console.AnswerQuestion(key) is { } answer)
            {
                Action<char>? callback = _questionCallback;
                _questionCallback = null;
                _console.Close();
                callback?.Invoke(answer);
            }

            return;
        }

        string? action = _consoleKeys.Add(key);

        if (action is not null)
        {
            RunConsoleAction(action);
        }
        else if (_consoleKeys.HasFailed)
        {
            _console.TypeKey(key);
            OnConsoleLineChanged();
        }

        if (_consoleKeys.IsFinished || _consoleKeys.HasFailed)
        {
            _consoleKeys.Clear();
        }
    }

    /// <summary>Carries out a console binding.</summary>
    private void RunConsoleAction(string action)
    {
        switch (action)
        {
            case "console_accept":
                {
                    string line = _console.Accept();
                    _pendingCommand = null;
                    _consoleKeys.Clear();

                    if (line.Trim().Length > 0)
                    {
                        _dispatcher.Execute(line);
                    }

                    break;
                }

            case "console_close":
                _pendingCommand?.Cancel();
                _pendingCommand = null;
                _console.Close();
                break;

            case "console_complete":
            case "console_complete_back":
                {
                    int direction = action.EndsWith("back", StringComparison.Ordinal) ? -1 : 1;
                    _console.CycleCompletions(CompletionsForCurrentLine(direction), direction);
                    break;
                }

            case "console_delete_back":
                if (_console.Delete(-1))
                {
                    OnConsoleLineChanged();
                }
                else
                {
                    _console.Close();
                }

                break;

            case "console_delete":
                _console.Delete(0);
                OnConsoleLineChanged();
                break;

            case "console_delete_word":
                _console.DeleteWord();
                OnConsoleLineChanged();
                break;

            case "console_delete_to_end":
                _console.DeleteRest(1);
                OnConsoleLineChanged();
                break;

            case "console_delete_to_start":
                _console.DeleteRest(-1);
                OnConsoleLineChanged();
                break;

            case "console_paste":
                _console.Paste();
                OnConsoleLineChanged();
                break;

            case "console_left":
                _console.MoveCursor(-1);
                break;

            case "console_right":
                _console.MoveCursor(1);
                break;

            case "console_word_left":
                _console.MoveCursorByWord(-1);
                break;

            case "console_word_right":
                _console.MoveCursorByWord(1);
                break;

            case "console_home":
                _console.MoveCursorToEdge(toEnd: false);
                break;

            case "console_end":
                _console.MoveCursorToEdge(toEnd: true);
                break;

            case "console_history_back":
                _console.HistoryMove(-1);
                break;

            case "console_history_forward":
                _console.HistoryMove(1);
                break;

            case "redraw_window":
                Redraw();
                break;

            default:
                Notify($"console: unknown action: {action}", isError: true);
                break;
        }
    }

    /// <summary>
    /// Lets the command act on each keystroke, so a filter or a search shows its effect as it is
    /// typed rather than only once it is finished.
    /// </summary>
    private void OnConsoleLineChanged()
    {
        _pendingCommand?.Cancel();
        _pendingCommand = null;

        if (_console.Text.Trim().Length == 0)
        {
            return;
        }

        try
        {
            _pendingCommand = _dispatcher.Build(_console.Text);

            if (_pendingCommand?.Quick() == true)
            {
                string line = _console.Accept();
                _pendingCommand = null;
                _dispatcher.Execute(line);
            }
        }
        catch (Exception e) when (e is CommandException or MacroException)
        {
            // A half-typed line is routinely invalid, so this is not worth reporting.
            _pendingCommand = null;
        }
    }

    /// <summary>Asks the command on the line what it would complete to.</summary>
    private IReadOnlyList<string> CompletionsForCurrentLine(int direction)
    {
        try
        {
            // With no space yet, the user is still naming the command itself.
            if (!_console.Text.Contains(' ', StringComparison.Ordinal))
            {
                return [.. Commands.Matching(_console.Text).Select(name => name + " ")];
            }

            return _dispatcher.Build(_console.Text)?.Complete(direction) ?? [];
        }
        catch (Exception e) when (e is CommandException or MacroException)
        {
            return [];
        }
    }

    /// <summary>Pushes the current settings onto the directories being shown.</summary>
    private void ApplySettingsToDirectory()
    {
        SortOrder order = new(
            SortOrder.ParseKey(Settings.Sort),
            Settings.SortReverse,
            Settings.SortDirectoriesFirst,
            Settings.SortCaseInsensitive,
            Settings.SortUnicode);

        bool autoupdate = Settings.AutoupdateCumulativeSize;

        foreach (DirectoryNode directory in CurrentTab.Pathway)
        {
            directory.ShowHidden = Settings.ShowHidden;
            directory.HiddenPattern = Settings.HiddenFilter;
            directory.SortOrder = order;
            directory.AutoupdateCumulativeSize = autoupdate;
        }

        // The directory shown to the right is measured and re-read like any other, so it needs the
        // setting too — and it is the one a `dc` is most often aimed at, since the cursor is on it.
        if (CurrentTab.SelectedDirectory is { } selected)
        {
            selected.AutoupdateCumulativeSize = autoupdate;
        }
    }

    /// <summary>Draws the info lines over the bottom of the listing.</summary>
    /// <param name="lines">What to show, one per row.</param>
    /// <remarks>
    /// Bottom-anchored and no taller than it needs, so as much of the directory as possible stays
    /// readable behind it — the same shape as the bookmark and hint windows, and as ranger's own
    /// (<c>gui/widgets/view_base.py:97-107</c>). Each row is blanked before it is written, because
    /// the listing underneath is wider than the line replacing it.
    /// </remarks>
    private void DrawInfo(IReadOnlyList<string> lines)
    {
        Rect bounds = BrowserBounds();
        int rows = Math.Min(lines.Count, bounds.Height);

        if (rows <= 0)
        {
            return;
        }

        CellStyle style = _colorScheme.Resolve(StyleContext.Of(ContextKey.InBrowser));
        int top = bounds.Bottom - rows;

        // The last `rows` lines, so a list too long for the screen shows its end rather than its
        // beginning — the higher numbers are the ones that would otherwise be unreachable.
        for (int i = 0; i < rows; i++)
        {
            _screen.Fill(bounds.X, top + i, bounds.Width, 1, style);
            _screen.Write(bounds.X, top + i,
                          new WideString(lines[lines.Count - rows + i]).Truncate(bounds.Width),
                          style);
        }
    }

    /// <summary>
    /// Draws or removes the image preview, after the buffer has been flushed.
    /// </summary>
    /// <remarks>
    /// Deliberately after the flush: the terminal draws the image itself, over cells the buffer
    /// left blank, so drawing it first would let the flush paint over it. It is also only
    /// redrawn when it changes, because re-sending an image on every keystroke makes the whole
    /// display flicker.
    /// </remarks>
    private void DrawImagePreview()
    {
        if (_images is null)
        {
            return;
        }

        (string Path, Rect Bounds)? wanted = _view.PendingImage;

        if (wanted == _shownImage)
        {
            return;
        }

        if (wanted is { } image)
        {
            _images.Draw(image.Path, image.Bounds.X, image.Bounds.Y,
                         image.Bounds.Width, image.Bounds.Height);
        }
        else
        {
            _images.Clear();
        }

        _shownImage = wanted;
    }

    /// <summary>Asks for the status of any repository shown as a row in the current listing.</summary>
    /// <remarks>
    /// <para>
    /// Only the current directory's repository was ever refreshed, and a repository is only drawn
    /// once it has been. So a listing of project directories — the one place a per-row remote
    /// mark is worth anything — showed nothing at all: each repository was found, none was loaded,
    /// and every marker came back empty.
    /// </para>
    /// <para>
    /// Cheap after the first pass: <c>RepositoryFor</c> caches by directory, and <c>Request</c>
    /// only queues a repository that has gone stale, on a worker. The first pass costs one walk
    /// up the tree per subdirectory, which is what <c>vcs_aware</c> is asking for.
    /// </para>
    /// </remarks>
    private void RequestVcsForListedRepositories()
    {
        if (Vcs is not { } vcs)
        {
            return;
        }

        // Once per listing, not once per frame. Asking is a dictionary lookup per subdirectory,
        // which is nothing on its own and is a few thousand of them a second in a directory of
        // repositories. The revision changes whenever the listing is rebuilt, which is exactly
        // when the answer could differ.
        DirectoryNode directory = CurrentTab.Current;

        if (_vcsRequestedFor == (directory.Path, directory.Revision))
        {
            return;
        }

        _vcsRequestedFor = (directory.Path, directory.Revision);

        foreach (FsNode entry in directory.Entries)
        {
            if (entry.IsDirectory)
            {
                vcs.Request(entry.Path);
            }
        }
    }

    /// <summary>The listing whose subdirectories have already been asked about.</summary>
    private (string Path, int Revision)? _vcsRequestedFor;

    /// <summary>The region the columns occupy, between the two bars.</summary>
    /// <remarks>
    /// The screen less the title bar and whichever row the status bar has taken. With
    /// <c>status_bar_on_top</c> the browser starts a row lower rather than losing a row from the
    /// bottom, so the console still has the last row to itself — which is how ranger arranges it
    /// (<c>gui/ui.py:360-362</c>): the status bar moves, the console does not.
    /// </remarks>
    private Rect BrowserBounds() =>
        new(0, Settings.StatusBarOnTop ? 2 : 1,
            _screen.Width, Math.Max(_screen.Height - (Settings.StatusBarOnTop ? 3 : 2), 0));

    /// <summary>Passes the settings the two views share on to the multipane one.</summary>
    /// <remarks>
    /// <c>draw_borders_multipane</c> is deliberately nullable rather than defaulting to
    /// <c>none</c>: unset means "whatever draw_borders says", which is different from asking
    /// for no borders and is what lets one setting govern both views.
    /// </remarks>
    private void ConfigureMultipane()
    {
        _multipaneView.ScrollOffset = Settings.ScrollOffset;
        _multipaneView.ShowSize = Settings.DisplaySizeInMainColumn;
        _multipaneView.Linemodes = Linemodes;
        _multipaneView.LineNumbers = Settings.LineNumbers;
        _multipaneView.OneIndexed = Settings.OneIndexed;
        _multipaneView.RelativeCurrentZero = Settings.RelativeCurrentZero;
        _multipaneView.Vcs = Vcs;
        _multipaneView.Tags = Tags;
        _multipaneView.CopyBuffer = _copyBufferPaths;
        _multipaneView.CopyBufferIsCut = IsCutPending;
        _multipaneView.DisplayTagsInAllColumns = Settings.DisplayTagsInAllColumns;
        _multipaneView.DrawBorders = Settings.DrawBordersMultipane ?? Settings.DrawBorders;
    }

    private void Draw()
    {
        // Before anything is configured, and not inside the status bar's own branch: the loop
        // shortens its wait while a message is showing, so a message that never expired — with
        // the console open, say — would leave it spinning on a zero timeout.
        if (_message is not null && Environment.TickCount64 >= _messageExpiresAt)
        {
            _message = null;
        }

        // Settings can change under a running session, so the view is configured each frame
        // rather than once. It is a handful of property reads.
        ApplySettingsToDirectory();
        _screen.Clear();

        _titleBar.Layout(new Rect(0, 0, _screen.Width, 1));
        _titleBar.Tab = CurrentTab;
        _titleBar.TildeInTitlebar = Settings.TildeInTitlebar;
        _titleBar.ShowSelection = Settings.ShowSelectionInTitlebar;
        _titleBar.KeyBuffer = _keys.Keys.Count > 0 ? _keys.ToString() : string.Empty;

        // Shown only while there is something to show it for, as ranger's main loop does
        // (`core/fm.py:512-515`).
        _titleBar.Throbber = Tasks.HasWork ? Tasks.Throbber.ToString() : string.Empty;
        _titleBar.UserName = Settings.HostnameInTitlebar ? Environment.UserName : null;
        _titleBar.HostName = Settings.HostnameInTitlebar ? Environment.MachineName : null;
        _titleBar.IsRoot = UserDatabase.IsRoot;
        _titleBar.Tabs = [.. _tabs.OrderBy(t => t.Key)
                                .Select(t => new TabHeading(t.Key, t.Value.Path, t.Value.Label))];
        _titleBar.ActiveTabNumber = CurrentTabNumber;
        _titleBar.DirnameInTabs = Settings.DirnameInTabs;

        // Pushed onto every tab, not just the current one: a background tab's filter should go
        // the same way when it is next left.
        foreach (Tab tab in _tabs.Values)
        {
            tab.ClearFilterOnLeave = Settings.ClearFiltersOnDirChange;
        }

        _titleBar.Ellipsis = Settings.UnicodeEllipsis ? "…" : "~";

        // Read from whatever the last refresh produced; nothing here waits on the repository.
        VcsRepository? repository = Vcs?.RepositoryFor(CurrentTab.Path);
        _titleBar.Branch = repository is { IsLoaded: true } ? repository.Branch : null;
        _titleBar.RemoteMarker = repository?.RemoteStatus switch
        {
            VcsRemoteStatus.Ahead => " ↑",
            VcsRemoteStatus.Behind => " ↓",
            VcsRemoteStatus.Diverged => " ↕",
            _ => string.Empty,
        };
        _titleBar.Render(_screen);

        _view.ColumnRatios = Settings.ColumnRatios;
        _view.PaddingRight = Settings.PaddingRight;
        _view.ScrollOffset = Settings.ScrollOffset;
        _view.ShowSize = Settings.DisplaySizeInMainColumn;
        _view.DrawBorders = Settings.DrawBorders;
        _view.LineNumbers = Settings.LineNumbers;
        _view.OneIndexed = Settings.OneIndexed;
        _view.RelativeCurrentZero = Settings.RelativeCurrentZero;

        // Asking now means the answer is ready for the next frame rather than this one, which is
        // exactly the trade that keeps a slow repository from stalling the listing.
        _view.Vcs = Vcs;
        _view.Tags = Tags;
        _view.CopyBuffer = _copyBufferPaths;
        _view.CopyBufferIsCut = IsCutPending;
        _view.DisplayTagsInAllColumns = Settings.DisplayTagsInAllColumns;
        _view.CollapsePreview = Settings.CollapsePreview;
        Directories.Frozen = Settings.FreezeFiles;
        Vcs?.Request(CurrentTab.Path);
        RequestVcsForListedRepositories();
        Linemodes.BinaryPrefix = Settings.BinarySizePrefix;
        Linemodes.CountFiles = Settings.AutomaticallyCountFiles;
        Linemodes.ExactBytes = Settings.SizeInBytes;

        // A changed colourscheme repaints everything, since every cached style is now wrong.
        //
        // Compared by name first. `SwitchTo` compares by name too, so constructing a scheme to
        // hand it was building a whole object and an empty style-memo dictionary on every frame
        // only to throw both away.
        if (!string.Equals(_colorScheme.Name, Settings.Colorscheme, StringComparison.Ordinal)
            && _colorScheme.SwitchTo(_colorSchemes.CreateOrDefault(Settings.Colorscheme)))
        {
            _screen.Clear();
        }

        Metadata.DeepSearch = Settings.MetadataDeepSearch;
        _view.Linemodes = Linemodes;
        _view.PreviewProvider = Settings.PreviewFiles || Settings.PreviewDirectories
            ? Previews
            : null;
        _view.WrapPreviews = Settings.WrapPlaintextPreviews;
        _view.ClearPendingImage();

        if (!_pager.IsVisible && !_taskView.IsVisible)
        {
            if (Settings.Viewmode is "multipane")
            {
                ConfigureMultipane();
                _multipaneView.Render(_screen, BrowserBounds(), Tabs, CurrentTabNumber);
            }
            else
            {
                _view.Render(_screen, BrowserBounds(), CurrentTab);
            }
        }

        // The task view and the pager each take the whole area between the bars.
        if (_taskView.IsVisible)
        {
            _taskView.Layout(BrowserBounds());
            _taskView.Render(_screen);
        }
        else if (_pager.IsVisible)
        {
            _pager.Layout(BrowserBounds());
            _pager.WrapLines = Settings.WrapPlaintextPreviews;
            _pager.Render(_screen);
        }
        else if (_showBookmarks)
        {
            // Before the hints, not after: ranger's draw() is `if draw_bookmarks ... elif
            // draw_hints`, so a pending `'` shows where the bookmarks lead rather than a list of
            // the keys that could follow. Showing both, or the hints instead, is the difference
            // between answering the question and restating it.
            _bookmarkWindow.Bookmarks = Bookmarks;
            _bookmarkWindow.ShowHidden = Settings.ShowHiddenBookmarks;

            int wanted = Math.Min(_bookmarkWindow.Entries.Count + 1, BrowserBounds().Height);

            if (wanted > 1)
            {
                _bookmarkWindow.IsVisible = true;
                _bookmarkWindow.Layout(new Rect(0, BrowserBounds().Bottom - wanted,
                                                _screen.Width, wanted));
                _bookmarkWindow.Render(_screen);
            }
        }
        else if (_info is { Count: > 0 } info)
        {
            // Last, matching ranger's `if draw_bookmarks ... elif draw_hints ... elif draw_info`
            // (gui/widgets/view_base.py:44-49): a pending key sequence is a more urgent question
            // than a list the user has already asked for.
            DrawInfo(info);
        }
        else if (_showHints)
        {
            // Over the listing rather than beside it, and only as tall as the list needs, so as
            // much of the directory as possible stays visible behind the answer.
            _hints.Position = _keys.Position;
            _hints.CollapseThreshold = Math.Max(Settings.HintCollapseThreshold, 1);

            int wanted = Math.Min(_hints.Hints.Count + 1, BrowserBounds().Height);

            if (wanted > 1)
            {
                _hints.IsVisible = true;
                _hints.Layout(new Rect(0, BrowserBounds().Bottom - wanted,
                                       _screen.Width, wanted));
                _hints.Render(_screen);
            }
        }

        // The console owns the bottom row. The status bar shares it, unless it has been asked
        // to sit under the title bar instead — in which case both are on screen at once.
        Rect bottom = new(0, _screen.Height - 1, _screen.Width, 1);
        bool statusOnTop = Settings.StatusBarOnTop;

        if (_console.IsOpen)
        {
            _console.Layout(bottom);
            _console.Render(_screen);
        }

        if (!_console.IsOpen || statusOnTop)
        {
            _statusBar.Layout(statusOnTop ? new Rect(0, 1, _screen.Width, 1) : bottom);
            _statusBar.Tab = CurrentTab;
            _statusBar.Message = _message;
            _statusBar.MessageIsError = _messageIsError;
            _statusBar.ShowFreeSpace = Settings.DisplayFreeSpaceInStatusBar;
            _statusBar.ShowSize = Settings.DisplaySizeInStatusBar;
            _statusBar.BinaryPrefix = Settings.BinarySizePrefix;
            _statusBar.Frozen = Settings.FreezeFiles;
            _statusBar.IsVisualMode = IsVisualMode;
            _statusBar.IsVisualReverse = _visualReverse;
            _statusBar.ExactBytes = Settings.SizeInBytes;
            _statusBar.Head = repository is { IsLoaded: true } ? repository.Head : null;
            _statusBar.VcsMessageLength = Math.Max(Settings.VcsMessageLength, 1);
            _statusBar.ShowProgressBar = Settings.DrawProgressBarInStatusBar;
            _statusBar.Progress = Tasks.OverallProgress();
            // The queue's line, not the running job's: with more than one thing queued the bar
            // speaks for all of it, and with one it is the job's own line unchanged.
            _statusBar.TaskDescription = Tasks.Summary()?.Describe();
            _statusBar.FreeBytes = FreeSpace();
            _statusBar.Render(_screen);
        }

        StringBuilder output = new();

        // A frame after another program had the terminal — or after a resize — is written in
        // full: the buffer's record of what is on screen is fiction until it has been rewritten
        // once.
        _screen.Flush(output, force: Volatile.Read(ref _needsFullRepaint));
        Volatile.Write(ref _needsFullRepaint, false);

        _terminal.Write(output.ToString());

        DrawImagePreview();

        // The hardware cursor is only shown while typing, where it marks the insertion point.
        if (_console.IsOpen && _console.Question is null)
        {
            _terminal.SetCursorPosition(_console.ScreenCursorX, _screen.Height - 1);
            _terminal.SetCursorVisible(true);
        }
        else
        {
            _terminal.SetCursorVisible(false);
        }
    }
}
