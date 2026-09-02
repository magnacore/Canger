// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Input;
using Canger.Core.Model;
using Canger.Core.Previews;
using Canger.Core.Processes;
using Canger.Core.State;
using Canger.Core.Settings;
using Canger.Core.Tasks;

namespace Canger.Core.Commands;

/// <summary>
/// Everything a command can do to the running file manager.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole surface a command sees. Keeping it an interface rather than the concrete
/// browser means commands can be tested by driving them against a stand-in, with no terminal and
/// no screen — which matters because commands are the largest and most user-visible part of the
/// port.
/// </para>
/// <para>
/// It is also the API that user-written commands and plugins are compiled against, so anything
/// added here becomes part of the extension contract.
/// </para>
/// </remarks>
public interface IFileManager
{
    /// <summary>The tab the user is working in.</summary>
    Tab CurrentTab { get; }

    /// <summary>Every open tab, keyed by the number shown in the tab bar.</summary>
    IReadOnlyDictionary<int, Tab> Tabs { get; }

    /// <summary>Which tab is active.</summary>
    int CurrentTabNumber { get; }

    /// <summary>The configuration.</summary>
    CangerSettings Settings { get; }

    /// <summary>Where directories come from.</summary>
    DirectoryCache Directories { get; }

    /// <summary>The underlying filesystem.</summary>
    IFileSystem FileSystem { get; }

    /// <summary>Background work.</summary>
    TaskQueue Tasks { get; }

    /// <summary>The key bindings, so the map commands can change them at run time.</summary>
    KeyMaps KeyMaps { get; }

    /// <summary>The command registry, so commands can look up and alias one another.</summary>
    CommandRegistry Commands { get; }

    /// <summary>
    /// The paths waiting to be pasted, and whether they are being moved rather than copied.
    /// </summary>
    /// <remarks>
    /// Paths rather than nodes, because a buffer filled by another running Canger has no nodes to
    /// offer — only names it read from a file. Every consumer reduced to the path anyway.
    /// <see cref="SetCopyBuffer"/> still takes nodes, which is what the browser and any plugin
    /// have to hand.
    /// </remarks>
    IReadOnlyList<string> CopyBuffer { get; }

    /// <summary>Whether the copy buffer will be moved rather than copied.</summary>
    bool IsCutPending { get; }

    /// <summary>The file under the cursor, or <see langword="null"/> when the listing is empty.</summary>
    FsNode? CurrentFile => CurrentTab.Selected;

    /// <summary>The directory being shown.</summary>
    DirectoryNode CurrentDirectory => CurrentTab.Current;

    /// <summary>What a command should act on.</summary>
    IReadOnlyList<FsNode> Selection => CurrentTab.Selection;

    /// <summary>Shows a message in the status bar.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="isError">Whether it reports a problem.</param>
    void Notify(string message, bool isError = false);

    /// <summary>
    /// What <c>search_next</c> steps through when not told: one of <c>search</c>, <c>tag</c>,
    /// <c>size</c>, <c>mimetype</c>, <c>ctime</c>, <c>mtime</c>, <c>atime</c>.
    /// </summary>
    /// <remarks>
    /// Sticky, as ranger's is (<c>core/fm.py:81</c>, defaulting to <c>ctime</c>): pressing
    /// <c>cm</c> once makes every later <c>n</c> step through modification times until something
    /// else changes it.
    /// </remarks>
    string SearchMethod { get; set; }

    /// <summary>
    /// Every message shown this session, oldest first.
    /// </summary>
    /// <remarks>
    /// A notification lives on the status line until the next one replaces it, so anything that
    /// happens twice in quick succession is gone before it can be read. Keeping them is what
    /// makes <c>:display_log</c> — <c>W</c> — able to answer "what did that just say?".
    /// </remarks>
    IReadOnlyList<string> MessageLog { get; }

    /// <summary>
    /// Whether visual mode is on, so moving the cursor extends a selection.
    /// </summary>
    bool IsVisualMode { get; }

    /// <summary>
    /// Turns visual mode on or off.
    /// </summary>
    /// <param name="mode"><c>visual</c> or <c>normal</c>; anything else is ignored.</param>
    /// <remarks>
    /// Entering visual mode remembers what was already marked and marks the entry under the
    /// cursor; leaving it forgets both. Ranger keeps the previous selection so that abandoning a
    /// visual selection can restore it (<c>core/actions.py:79-102</c>).
    /// </remarks>
    void ChangeMode(string mode);

    /// <summary>
    /// Scrolls the preview column.
    /// </summary>
    /// <param name="lines">How far, negative for up.</param>
    /// <remarks>
    /// Only the preview: the cursor stays where it is. This is how a long file is read without
    /// opening it (<c>core/actions.py:1024-1033</c>).
    /// </remarks>
    void ScrollPreview(int lines);

    /// <summary>
    /// The command lines run at the prompt, oldest first.
    /// </summary>
    /// <remarks>
    /// Exposed so a binding can reach back into it — <c>&lt;C-p&gt;</c> opens the prompt showing
    /// the last command — without every such binding needing its own verb on this interface.
    /// </remarks>
    IReadOnlyList<string> CommandHistory { get; }

    /// <summary>
    /// Asks the user a question, answered with a single key.
    /// </summary>
    /// <param name="question">What to ask.</param>
    /// <param name="callback">
    /// What to do with the answer. Called only once an accepted key is pressed; abandoning the
    /// question never calls it, so a command that only acts on "yes" needs no cancel path.
    /// </param>
    /// <param name="choices">The accepted answers, the first being what Escape means.</param>
    void Ask(string question, Action<char> callback, IReadOnlyList<char>? choices = null);

    /// <summary>Asks Canger to exit.</summary>
    void Quit();

    /// <summary>Opens the command line, optionally pre-filled.</summary>
    /// <param name="text">What to put on the line.</param>
    /// <param name="cursorPosition">Where to put the cursor, or -1 for the end.</param>
    void OpenConsole(string text = "", int cursorPosition = -1);

    /// <summary>Records what should be pasted, and how.</summary>
    /// <param name="files">The files.</param>
    /// <param name="cut">Whether they should be moved rather than copied.</param>
    /// <remarks>
    /// Kept taking nodes rather than paths: it is what the browser and a plugin have to hand, and
    /// changing it would break configurations for nothing.
    /// </remarks>
    void SetCopyBuffer(IEnumerable<FsNode> files, bool cut);

    /// <summary>Records what should be pasted, by path.</summary>
    /// <param name="paths">Absolute paths.</param>
    /// <param name="cut">Whether they should be moved rather than copied.</param>
    /// <remarks>
    /// The form the shared buffer speaks, since a buffer read from another running Canger is only
    /// ever a list of names.
    /// </remarks>
    void SetCopyBufferPaths(IEnumerable<string> paths, bool cut);

    /// <summary>Runs a command line, as if the user had typed it.</summary>
    /// <param name="line">The command and its arguments.</param>
    /// <param name="quantifier">A numeric prefix typed before the key, when there was one.</param>
    /// <param name="wildcards">Keys captured by <c>&lt;any&gt;</c> in the binding that fired.</param>
    /// <returns><see langword="true"/> when a command ran.</returns>
    bool Execute(string line, int? quantifier = null, IReadOnlyList<int>? wildcards = null);

    /// <summary>
    /// How many rows the browser occupies, which is what a page of movement means.
    /// </summary>
    /// <remarks>
    /// Ranger passes its browser's height as the page size (<c>core/actions.py:522</c>). Canger
    /// used <c>scroll_offset * 2</c> — a constant sixteen whatever the terminal was — so a page
    /// down on a tall window moved a third of the way and on a short one overshot.
    /// </remarks>
    int BrowserHeight { get; }

    /// <summary>Re-reads the directory being shown.</summary>
    void ReloadCurrentDirectory();

    /// <summary>Re-reads one particular directory, wherever the user has since moved to.</summary>
    /// <param name="path">The absolute path of the directory to re-read.</param>
    /// <remarks>
    /// The form background work needs. A job that started in one directory may well finish after
    /// the user has walked somewhere else, and it is the directory the files landed in that has
    /// changed — not whichever one happens to be on screen. Reloading the current one instead
    /// would refresh the wrong listing and leave the right one stale.
    ///
    /// Ranger's archive plugin captures its <c>cwd</c> for exactly this reason and reloads it by
    /// path (<c>plugins/ranger-archives/extract.py</c>, the <c>refresh</c> closure).
    ///
    /// Does nothing when that directory is not loaded: there is no listing to bring up to date,
    /// and it will be read fresh whenever it is next visited.
    /// </remarks>
    void ReloadDirectory(string path);

    /// <summary>
    /// Puts the cursor on a path, entering its directory first if that is not where we are.
    /// </summary>
    /// <param name="path">The absolute path to land on.</param>
    /// <returns><see langword="true"/> when it was found and selected.</returns>
    /// <remarks>
    /// What every chooser needs: fzf, <c>fd</c> and <c>--selectfile</c> all end by naming a path
    /// somewhere in the tree and expecting the user to be looking straight at it.
    /// </remarks>
    bool SelectPath(string path);

    /// <summary>
    /// Raised when the directory being browsed changes, with its path.
    /// </summary>
    /// <remarks>
    /// Ranger's <c>cd</c> signal, which is one of the two things real plugins actually bind to —
    /// zoxide keeps its frecency database up to date this way. Raised for any change of place: a
    /// <c>:cd</c>, moving up or in, switching tabs, or following a bookmark.
    /// </remarks>
    event EventHandler<string>? DirectoryEntered;

    /// <summary>Repaints everything, after something outside Canger disturbed the screen.</summary>
    void Redraw();

    /// <summary>Shows text full screen, with somewhere to scroll.</summary>
    /// <param name="text">What to show.</param>
    void ShowInPager(string text);

    /// <summary>Shows a list of lines over the bottom of the listing until the console closes.</summary>
    /// <param name="lines">What to show, one per row.</param>
    /// <remarks>
    /// For an answer the user needs <em>while</em> they type — the programs that could open a
    /// file, with the numbers to pick them by. The status bar cannot do this: it holds one line,
    /// and the console that follows sits in the same place and hides it immediately. Ranger draws
    /// it into the browser instead (<c>ui.browser.draw_info</c>,
    /// <c>gui/widgets/view_base.py:97-107</c>) and clears it when the console closes.
    /// </remarks>
    void ShowInfo(IReadOnlyList<string> lines);

    /// <summary>Shows text in the user's own pager, rather than in Canger's.</summary>
    /// <param name="text">What to show.</param>
    /// <remarks>
    /// For the help dumps, which are long and are read by searching them. Canger's pager scrolls
    /// and nothing more, so <c>/</c> does nothing in it; <c>less</c> brings its search, its
    /// <c>n</c>/<c>N</c>, its line addressing and everything else the reader already knows.
    /// Ranger draws the same line — <c>_run_pager</c> (<c>core/actions.py:1483-1484</c>) hands
    /// the dumps to <c>$PAGER</c> while previews stay in the built-in one.
    /// </remarks>
    void ShowInExternalPager(string text);

    /// <summary>Closes the pager.</summary>
    void ClosePager();

    /// <summary>Discards cached previews, so they are generated afresh.</summary>
    void InvalidatePreviews();

    /// <summary>
    /// Shows the bookmark list until the next keystroke.
    /// </summary>
    /// <remarks>
    /// What <c>map '&lt;bg&gt; draw_bookmarks</c> calls. Every keystroke hides it again, so it
    /// lives exactly as long as the pending key sequence that asked for it.
    /// </remarks>
    void ShowBookmarks();

    /// <summary>Shows the queued background work.</summary>
    void OpenTaskView();

    /// <summary>Closes the task view.</summary>
    void CloseTaskView();

    /// <summary>
    /// Asks for a line of text, and hands it back when it has been typed.
    /// </summary>
    /// <param name="question">What to show before the input.</param>
    /// <param name="callback">
    /// Given what was typed, or <see langword="null"/> if the user gave up. The two are different
    /// answers: an empty passphrase is a passphrase, and a cancelled prompt is not.
    /// </param>
    /// <param name="hidden">
    /// Whether to draw the line as bullets. A hidden line is also kept out of the command history
    /// — which is written to disc — and neither completes nor recalls anything.
    /// </param>
    void Prompt(string question, Action<string?> callback, bool hidden = false);

    /// <summary>The removable drives, their cursor, and the checks that guard them.</summary>
    Devices.DeviceSession Devices { get; }

    /// <summary>Shows the removable drives.</summary>
    void OpenDevices();

    /// <summary>Closes the device list.</summary>
    void CloseDevices();

    /// <summary>
    /// Switches to a tab, creating it if there is none with that number.
    /// </summary>
    /// <param name="number">Which tab.</param>
    /// <param name="path">Where the tab should go, or <see langword="null"/> to leave it be.</param>
    void OpenTab(int number, string? path = null);

    /// <summary>
    /// Closes a tab.
    /// </summary>
    /// <param name="number">
    /// Which tab, or <see langword="null"/> for the current one.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when this was the last tab, which is never closed: there would be
    /// nothing left to show.
    /// </returns>
    bool CloseTab(int? number = null);

    /// <summary>
    /// Moves the current tab to a different number, shifting whatever is in the way.
    /// </summary>
    /// <param name="number">The number to move it to.</param>
    void ShiftTab(int number);

    /// <summary>
    /// Reopens the most recently closed tab.
    /// </summary>
    /// <returns><see langword="true"/> when there was one to reopen.</returns>
    bool RestoreTab();

    /// <summary>Directories the user can jump to by pressing a single key.</summary>
    Bookmarks Bookmarks { get; }

    /// <summary>Marks on files that survive between sessions.</summary>
    Tags Tags { get; }

    /// <summary>Decides how each row of a listing is rendered.</summary>
    LinemodeSelector Linemodes { get; }

    /// <summary>Annotations recorded about entries, beyond what the filesystem knows.</summary>
    MetadataManager Metadata { get; }

    /// <summary>
    /// Version-control status, or <see langword="null"/> when <c>vcs_aware</c> is off.
    /// </summary>
    Vcs.VcsService? Vcs { get; }

    /// <summary>Runs external programs.</summary>
    IProcessRunner Runner { get; }

    /// <summary>
    /// The system clipboard, which <c>:yank</c> writes to.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Runner"/> because a clipboard helper must not be given the
    /// terminal, and because the text has to go in on its standard input rather than through a
    /// shell — a filename may contain anything, and none of it should need escaping.
    /// </remarks>
    IClipboard Clipboard { get; }

    /// <summary>Decides which program opens a file, and opens it.</summary>
    IFileOpener Opener { get; }

    /// <summary>Runs a program, suspending the interface while it has the terminal.</summary>
    /// <param name="command">The command line to run through the shell.</param>
    /// <param name="flags">Runner flags, as rifle and <c>:shell</c> use them.</param>
    void RunProgram(string command, string flags = "");

    /// <summary>
    /// Queues a program to run in the background, reported in the task view.
    /// </summary>
    /// <param name="description">What the task view calls it, such as "Extracting: notes.zip".</param>
    /// <param name="command">The command line to run through the shell.</param>
    /// <param name="workingDirectory">Where to run it, or <see langword="null"/> for here.</param>
    /// <param name="finished">
    /// Run once the program has ended, whatever its outcome. Usually
    /// <see cref="ReloadCurrentDirectory"/>, since a program that made files is not otherwise
    /// noticed.
    /// </param>
    /// <returns>The queued task, so a caller can track or cancel it.</returns>
    /// <remarks>
    /// <para>
    /// This is the counterpart of <see cref="RunProgram"/> for work that takes a while and has
    /// nothing to say: the browser stays usable, the title bar spins, and the job can be paused
    /// or cancelled from the task view like any other. Anything the program writes to standard
    /// error is reported when it ends.
    /// </para>
    /// <para>
    /// It is the equivalent of ranger's <c>fm.loader.add(CommandLoader(args, descr))</c>, which
    /// is what its archive plugin uses so that unpacking does not take over the screen
    /// (<c>plugins/ranger-archives/extract.py</c>).
    /// </para>
    /// </remarks>
    Tasks.QueuedTask RunInBackground(string description, string command,
                                     string? workingDirectory = null,
                                     Action<Tasks.CommandTask>? finished = null);
}
