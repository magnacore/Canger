// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model;

/// <summary>
/// One independent view of the filesystem: where it is, how it got there, and where its cursor
/// sits.
/// </summary>
/// <remarks>
/// <para>
/// A tab keeps its own copy of the cursor position rather than relying on the directory's.
/// Directories are shared between tabs, so two tabs showing the same directory would otherwise
/// fight over one cursor. On arrival the tab writes its remembered position into the directory,
/// and while it is active it keeps the two in step.
/// </para>
/// <para>
/// The pathway is the chain of directories from the root down to the current one, which the
/// browser draws as the columns to the left.
/// </para>
/// </remarks>
public sealed class Tab
{
    private readonly DirectoryCache _cache;

    /// <summary>The shared cache this tab reads its directories from.</summary>
    /// <remarks>
    /// Exposed so that whatever configures the directories on screen can configure the ones not
    /// made yet, in the same breath and from the same place.
    /// </remarks>
    public DirectoryCache Directories => _cache;
    private readonly Cursor _cursor = new();

    /// <summary>Creates a tab positioned at a directory.</summary>
    /// <param name="cache">Where directories are obtained from.</param>
    /// <param name="path">Where the tab starts.</param>
    /// <param name="historyCapacity">How many directories to remember.</param>
    public Tab(DirectoryCache cache, string path, int historyCapacity = 20)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        History = new History<string>(historyCapacity);
        Enter(path);
    }

    /// <summary>The directory the tab is showing.</summary>
    public DirectoryNode Current { get; private set; } = null!;

    /// <summary>Where the tab is.</summary>
    public string Path => Current.Path;

    /// <summary>The chain of directories from the root down to the current one.</summary>
    public IReadOnlyList<DirectoryNode> Pathway { get; private set; } = [];

    /// <summary>
    /// The directory containing this one, or <see langword="null"/> at the filesystem root.
    /// </summary>
    /// <remarks>
    /// Taken from the pathway rather than by reading the path, so it is the same interned object
    /// the ancestry column is drawing — which is what lets moving its cursor be visible.
    /// </remarks>
    public DirectoryNode? Parent =>
        Pathway.Count >= 2 ? Pathway[^2] : null;

    /// <summary>Where the tab has been.</summary>
    public History<string> History { get; }

    /// <summary>The entry under this tab's cursor.</summary>
    public FsNode? Selected => LiveCursor.Current;

    /// <summary>
    /// The tab's cursor, brought back into agreement with the listing first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tab keeps its own cursor rather than sharing the directory's, because the same directory
    /// can be open in several tabs. The cost is that anything which reloads the directory —
    /// finishing a move, a paste, <c>:reload_cwd</c> — leaves the tab holding a node that is no
    /// longer in the listing.
    /// </para>
    /// <para>
    /// That is exactly what went wrong after <c>mkdirmv</c>: the tab still pointed at the file it
    /// had just moved away, so pressing <c>l</c> asked rifle to open a file that was not there
    /// instead of entering the directory that had taken its place — and rifle's <c>xdg-open --</c>
    /// rule then failed, which is all the user saw.
    /// </para>
    /// <para>
    /// Ranger has the same split and solves it the same way: <c>Tab.pointer</c> is a property
    /// whose getter checks the cached pointer against the live listing and re-binds it
    /// (<c>core/tab.py:59-68</c>). Here the check is a revision comparison, so the listing is only
    /// searched when it has actually changed.
    /// </para>
    /// </remarks>
    private Cursor LiveCursor
    {
        get
        {
            if (Current is { } directory && _cursorRevision != directory.Revision)
            {
                _cursorRevision = directory.Revision;
                _cursor.Reconcile(directory.Entries);
            }

            return _cursor;
        }
    }

    private int _cursorRevision = -1;

    /// <summary>Takes the directory's cursor as this tab's own, and notes the listing it matches.</summary>
    /// <remarks>
    /// Recording the revision here is what keeps <see cref="LiveCursor"/> cheap: the two are known
    /// to agree at this moment, so nothing is searched until the listing is rebuilt.
    /// </remarks>
    private void AdoptCursor()
    {
        _cursor.CopyFrom(Current.Cursor);
        _cursorRevision = Current.Revision;
    }

    /// <summary>What a command run in this tab should act on.</summary>
    public IReadOnlyList<FsNode> Selection => Current.Selection;

    /// <summary>
    /// The selected entry as the shared directory it stands for, when it is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A directory's entries are rebuilt by each scan, so the <see cref="DirectoryNode"/> sitting
    /// in a listing is a *different object* from the interned one a tab uses when it enters that
    /// directory. It therefore has its own cursor, its own marks and its own counted state.
    /// </para>
    /// <para>
    /// That is why the preview column used to highlight the first row of the directory you had
    /// just left rather than the row you left the cursor on: it was drawing a fresh node whose
    /// cursor had never moved. Going through the cache gives it the same node entering would,
    /// which is what ranger does — its scan interns through <c>fm.get_directory</c>
    /// (<c>container/directory.py:428</c>).
    /// </para>
    /// </remarks>
    public DirectoryNode? SelectedDirectory =>
        Selected is { IsDirectory: true } selected ? _cache.Get(selected.Path) : null;

    /// <summary>Whether leaving a directory clears the filter set on it.</summary>
    /// <remarks>
    /// The <c>clear_filters_on_dir_change</c> setting, kept here rather than read from the
    /// settings because <see cref="Enter"/> is reached from a dozen places and none of them
    /// should have to remember to ask.
    ///
    /// Only the scout filter, as in ranger: the filter *stack* is built deliberately, one
    /// clause at a time, and is not something to throw away because someone pressed <c>l</c>.
    /// </remarks>
    public bool ClearFilterOnLeave { get; set; }

    /// <summary>An optional label shown in the tab bar.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// What was last searched for here, so <c>n</c> can repeat it.
    /// </summary>
    /// <remarks>
    /// Per tab, as in ranger (<c>core/tab.py:21</c>): each tab is a separate place to be looking
    /// at something, and repeating a search should repeat the one made here.
    /// </remarks>
    public string? LastSearch { get; set; }

    /// <summary>Raised with the directory being left, whenever the tab moves.</summary>
    public event EventHandler<string>? Left;

    /// <summary>
    /// Moves the tab to a directory.
    /// </summary>
    /// <param name="path">Where to go. A file's path moves to its directory and selects it.</param>
    /// <param name="recordHistory">Whether to remember the move, so it can be stepped back.</param>
    /// <param name="cancellationToken">Abandons the scan.</param>
    /// <returns><see langword="true"/> when the tab moved.</returns>
    public bool Enter(string path, bool recordHistory = true,
                      CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Expanded here rather than in each command that supplies a path, which is where ranger
        // does it (core/tab.py:25). A path that is already absolute passes through unchanged.
        string target = FileSystem.UserPath.Expand(path);
        string? selectAfterwards = null;
        string? previous = Current?.Path;

        // Being given a file rather than a directory means "go there and put the cursor on it",
        // which is what makes --selectfile and jumping to a search result work.
        // The filter belongs to the listing you set it on, not to you, and ranger clears it as
        // you leave rather than as you arrive (`core/tab.py:140-142`) — so coming back to a
        // directory shows it whole. Without this a `zf` set in one place quietly followed the
        // user everywhere, which reads as the listing being wrong rather than filtered.
        if (ClearFilterOnLeave && Current is { } leaving)
        {
            leaving.PreviewFilter = null;
            leaving.FilterStack.PopNameFilter();
            leaving.Refilter();
        }

        // Ranger's rule, and it is one test rather than two: anything that is not a directory is
        // entered as its parent, with the cursor put on the name (`core/tab.py:151-153`). That
        // covers a file — go there and select it — and equally a path that has stopped being
        // anything, which is what another instance deleting the directory you are standing in
        // leaves behind. Canger asked whether the path was a file, so a path that had vanished
        // failed the test and was entered anyway; `reset` then re-entered a directory that was
        // not there and stayed in it.
        DirectoryNode candidate = _cache.Get(target);
        if (candidate.Status is not { IsDirectory: true })
        {
            selectAfterwards = target;
            target = NearestDirectory(System.IO.Path.GetDirectoryName(target) ?? "/");
        }

        DirectoryNode directory = _cache.GetLoaded(target, cancellationToken);

        Current = directory;
        Pathway = _cache.PathwayTo(target);

        // The directory may be shared with another tab, so its cursor is set from this tab's
        // remembered position rather than the other way round.
        if (selectAfterwards is not null)
        {
            FsNode? node = directory.Entries.FirstOrDefault(
                e => string.Equals(e.Path, selectAfterwards, StringComparison.Ordinal));
            directory.Cursor.MoveToNode(node, directory.Entries);
        }

        directory.Cursor.Reconcile(directory.Entries);
        _cursor.CopyFrom(directory.Cursor);
        _cursorRevision = directory.Revision;

        // Each column highlights the child the tab came through, which is what makes the
        // breadcrumb columns line up with the current path.
        AlignPathwayCursors();

        if (recordHistory)
        {
            // The previous location is announced so the file manager can hold it under the
            // previous-directory bookmark, which is how pressing that key twice returns you to
            // where you started.
            if (previous is { Length: > 0 } &&
                !string.Equals(previous, directory.Path, StringComparison.Ordinal))
            {
                Left?.Invoke(this, previous);
            }

            History.Add(directory.Path);
        }

        return true;
    }

    /// <summary>Moves to the parent directory, selecting the directory just left.</summary>
    /// <param name="cancellationToken">Abandons the scan.</param>
    /// <returns><see langword="true"/> when there was a parent to move to.</returns>
    public bool GoUp(CancellationToken cancellationToken = default)
    {
        if (Path == "/")
        {
            return false;
        }

        string child = Path;
        string parent = System.IO.Path.GetDirectoryName(child) ?? "/";

        Enter(parent, recordHistory: true, cancellationToken);

        FsNode? node = Current.Entries.FirstOrDefault(
            e => string.Equals(e.Path, child, StringComparison.Ordinal));
        MoveCursorTo(node);

        return true;
    }

    /// <summary>
    /// Walks up from a path until it finds something that really is a directory.
    /// </summary>
    /// <param name="from">Where to start looking.</param>
    /// <returns>The nearest ancestor that exists, or the root.</returns>
    /// <remarks>
    /// Ranger tries the immediate parent and no further: if that is gone too its <c>chdir</c>
    /// fails and it stays where it was (`core/tab.py:156-159`), which for a deleted tree means it
    /// does not recover at all. Walking up gives the same answer as ranger whenever ranger has
    /// one, and an answer where ranger has none — which is the common case here, since deleting
    /// a directory usually means deleting the thing that contained what you were looking at.
    /// </remarks>
    private string NearestDirectory(string from)
    {
        string path = from;

        while (_cache.Get(path).Status is not { IsDirectory: true })
        {
            string? parent = System.IO.Path.GetDirectoryName(path);

            // At the root, or somewhere with no parent to go to. The root is the one directory
            // that can be relied on to be there.
            if (parent is null || string.Equals(parent, path, StringComparison.Ordinal))
            {
                return "/";
            }

            path = parent;
        }

        return path;
    }

    /// <summary>
    /// Enters the selected entry when it is a directory.
    /// </summary>
    /// <param name="cancellationToken">Abandons the scan.</param>
    /// <returns><see langword="true"/> when the tab moved.</returns>
    public bool EnterSelected(CancellationToken cancellationToken = default)
    {
        if (Selected is not { IsDirectory: true } directory)
        {
            return false;
        }

        Enter(directory.Path, recordHistory: true, cancellationToken);
        return true;
    }

    /// <summary>Steps through the history and moves there.</summary>
    /// <param name="offset">How far to step. Negative goes back.</param>
    /// <param name="cancellationToken">Abandons the scan.</param>
    /// <returns><see langword="true"/> when the tab moved.</returns>
    public bool GoInHistory(int offset, CancellationToken cancellationToken = default)
    {
        if (History.Move(offset) is not { } path)
        {
            return false;
        }

        // Not recorded, or stepping back would immediately overwrite the forward path.
        return Enter(path, recordHistory: false, cancellationToken);
    }

    /// <summary>Moves this tab's cursor, keeping the shared directory in step.</summary>
    /// <param name="index">The index to move to.</param>
    public void MoveCursor(int index)
    {
        Current.Cursor.MoveTo(index, Current.Entries);
        AdoptCursor();
    }

    /// <summary>Moves this tab's cursor to a particular entry.</summary>
    /// <param name="node">The entry to move to.</param>
    public void MoveCursorTo(FsNode? node)
    {
        Current.Cursor.MoveToNode(node, Current.Entries);
        AdoptCursor();
    }

    /// <summary>
    /// Re-establishes the cursor after this tab becomes active again, since the shared directory
    /// may have been moved around by another tab in the meantime.
    /// </summary>
    public void Activate()
    {
        Current.Cursor.CopyFrom(_cursor);
        Current.Cursor.Reconcile(Current.Entries);
        AdoptCursor();
        AlignPathwayCursors();
    }

    /// <summary>Points each breadcrumb column at the child the tab descended through.</summary>
    private void AlignPathwayCursors()
    {
        for (int i = 0; i < Pathway.Count - 1; i++)
        {
            DirectoryNode parent = Pathway[i];
            DirectoryNode child = Pathway[i + 1];

            if (!parent.IsLoaded)
            {
                parent.Load();
            }

            FsNode? node = parent.Entries.FirstOrDefault(
                e => string.Equals(e.Path, child.Path, StringComparison.Ordinal));
            parent.Cursor.MoveToNode(node, parent.Entries);
        }
    }
}
