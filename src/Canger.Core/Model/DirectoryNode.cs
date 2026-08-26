// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;
using Canger.Core.Model.Filters;

namespace Canger.Core.Model;

/// <summary>
/// A directory as the browser sees it: its contents, how they are ordered and filtered, which are
/// marked, and where the cursor sits.
/// </summary>
/// <remarks>
/// <para>
/// Two lists are kept. <see cref="AllEntries"/> is everything the scan found, ordered.
/// <see cref="Entries"/> is what survives the filters and is what the user sees and navigates.
/// Sorting works on the full list and the filtered view is derived from it, so toggling a filter
/// never has to re-read the directory.
/// </para>
/// <para>
/// Directory objects are shared: the same instance backs the main column, the parent column of
/// the directory below it, and every tab that has it open. That sharing is what makes breadcrumb
/// columns and tab switching cheap, and it is why marks and the cursor live here rather than in
/// the view.
/// </para>
/// </remarks>
public sealed class DirectoryNode : FsNode
{
    private readonly IFileSystem _fileSystem;
    private readonly List<FsNode> _allEntries = [];
    private readonly HashSet<string> _markedPaths = new(StringComparer.Ordinal);

    private List<FsNode> _entries = [];
    private SortOrder _sortOrder = SortOrder.Default;
    private bool _showHidden;
    private string _hiddenPattern = DefaultHiddenPattern;
    private System.Text.RegularExpressions.Regex? _hiddenRegex =
        HiddenRegexFor(DefaultHiddenPattern);
    private int _shuffleSeed;

    /// <summary>Creates a directory node.</summary>
    /// <param name="fileSystem">Used to scan the directory.</param>
    /// <param name="path">Absolute path.</param>
    /// <param name="status">Metadata following symbolic links.</param>
    /// <param name="linkStatus">Metadata of the link itself, when reached through one.</param>
    /// <param name="relativeToPath">The directory the display path is measured from.</param>
    /// <param name="cache">
    /// Where child directories are interned, so a directory is one object however it is reached.
    /// Null builds fresh child nodes, which is what a directory constructed on its own does.
    /// </param>
    public DirectoryNode(IFileSystem fileSystem, string path, FileStatus? status,
                         FileStatus? linkStatus = null, string? relativeToPath = null,
                         DirectoryCache? cache = null)
        : base(path, status, linkStatus, relativeToPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _cache = cache;
    }

    /// <summary>
    /// Where child directories are interned, when this node came from a cache.
    /// </summary>
    /// <remarks>
    /// Optional so a directory can still be built on its own — every test does that — in which
    /// case its children are fresh nodes, which is what this class did for everything before.
    /// </remarks>
    private readonly DirectoryCache? _cache;

    /// <summary>Everything the scan found, ordered but unfiltered.</summary>
    public IReadOnlyList<FsNode> AllEntries => _allEntries;

    /// <summary>What the user sees: ordered and filtered.</summary>
    public IReadOnlyList<FsNode> Entries => _entries;

    /// <summary>Where the cursor sits within <see cref="Entries"/>.</summary>
    public Cursor Cursor { get; } = new();

    /// <summary>The entry under the cursor, or <see langword="null"/> when the listing is empty.</summary>
    public FsNode? Selected => Cursor.Current;

    /// <summary>The filters the user has stacked up on this directory.</summary>
    public FilterStack FilterStack { get; } = new();

    /// <summary>
    /// A filter applied while the user is still typing, shown live and discarded if they cancel.
    /// </summary>
    public IFileFilter? PreviewFilter { get; set; }

    /// <summary>
    /// Whether entries the hidden filter matches are shown anyway.
    /// </summary>
    /// <remarks>
    /// Changing this re-derives the listing immediately, as changing the sort order does.
    /// Requiring a separate call would make it possible to change the setting and see nothing
    /// happen, which is a bug waiting to be written.
    /// </remarks>
    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (_showHidden == value)
            {
                return;
            }

            _showHidden = value;
            Refilter();
        }
    }

    /// <summary>The pattern deciding which entries count as hidden.</summary>
    public string HiddenPattern
    {
        get => _hiddenPattern;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (string.Equals(_hiddenPattern, value, StringComparison.Ordinal))
            {
                return;
            }

            _hiddenPattern = value;

            // Built once here, not looked up per entry per pass. The static Regex.IsMatch pays a
            // (pattern, options, culture) cache probe on every call, and this is called for every
            // path component of every entry each time the listing is filtered.
            //
            // Deliberately not RegexOptions.Compiled: that costs about a millisecond of codegen
            // per pattern, which a listing filtered once or twice never earns back.
            _hiddenRegex = HiddenRegexFor(value);

            Refilter();
        }
    }

    /// <summary>
    /// How many levels below this directory are folded into the listing. Zero lists only direct
    /// children; a negative value means the whole tree.
    /// </summary>
    public int FlatLevel { get; private set; }

    /// <summary>Whether the listing is flattened.</summary>
    public bool IsFlat => FlatLevel != 0;

    /// <summary>Whether a scan has happened.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Why the last scan failed, or <see langword="null"/> when it succeeded.</summary>
    public string? LoadError { get; private set; }

    /// <summary>When the directory was last scanned.</summary>
    public DateTimeOffset LastLoaded { get; private set; }

    /// <summary>How many entries the listing shows.</summary>
    public int Count => _entries.Count;

    /// <summary>The entries the user has marked, in listing order.</summary>
    public IReadOnlyList<FsNode> MarkedEntries => [.. _entries.Where(e => e.IsMarked)];

    /// <summary>
    /// What a command should act on: the marked entries when there are any, otherwise the entry
    /// under the cursor.
    /// </summary>
    /// <remarks>
    /// This is the rule the whole interface depends on. It is what lets <c>dd</c> mean "cut this
    /// one" normally and "cut all of these" once something has been marked, with no separate
    /// command for each case.
    /// </remarks>
    public IReadOnlyList<FsNode> Selection
    {
        get
        {
            IReadOnlyList<FsNode> marked = MarkedEntries;
            if (marked.Count > 0)
            {
                return marked;
            }

            return Selected is { } selected ? [selected] : [];
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A directory has no meaningful byte size of its own, so the entry count is reported
    /// instead: the loaded listing's own count where there is one, otherwise whatever
    /// <see cref="EnsureCounted"/> found.
    /// </remarks>
    public override long? Size => IsLoaded ? _entries.Count : _shallowCount;

    private int? _shallowCount;
    private bool _counted;

    /// <summary>
    /// Counts the entries without loading the directory, so a listing can show the count for
    /// folders the user has not opened.
    /// </summary>
    /// <returns>The count, or <see langword="null"/> when the directory could not be read.</returns>
    /// <remarks>
    /// One shallow directory read, cached, and only for what is actually on screen — which is
    /// what <c>automatically_count_files</c> asks for. It does not descend, so the cost does not
    /// grow with the size of the tree beneath.
    /// </remarks>
    public int? EnsureCounted()
    {
        if (_counted || IsLoaded)
        {
            return IsLoaded ? _entries.Count : _shallowCount;
        }

        _counted = true;

        try
        {
            _shallowCount = _fileSystem.CountEntries(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable, which the listing shows as a placeholder rather than as a count.
            _shallowCount = null;
        }

        return _shallowCount;
    }

    /// <summary>
    /// Whether a measured size is re-measured on every reload rather than marked doubtful.
    /// </summary>
    /// <remarks>
    /// <c>autoupdate_cumulative_size</c>, pushed in with the other settings. Off by default in
    /// ranger and here, because re-measuring means walking the whole tree again every time the
    /// listing is re-read.
    /// </remarks>
    public bool AutoupdateCumulativeSize { get; set; }

    /// <summary>
    /// Keeps a measured size honest across a reload.
    /// </summary>
    /// <remarks>
    /// Ranger's rule, from <c>container/directory.py:378-389</c>: a directory whose cumulative
    /// size was measured and is now being re-read either has that size taken again — when
    /// <c>autoupdate_cumulative_size</c> is on — or keeps the old figure with a <c>?</c> against
    /// it. It does not try to work out whether the size really changed, because knowing that costs
    /// exactly as much as re-measuring.
    /// </remarks>
    private void UpdateCumulativeSizeAfterReload(bool reloading, CancellationToken cancellationToken)
    {
        if (!reloading || CumulativeSize is null)
        {
            return;
        }

        if (!AutoupdateCumulativeSize)
        {
            CumulativeSizeStale = true;
            return;
        }

        try
        {
            CumulativeSize = DirectorySize.Measure(_fileSystem, Path, cancellationToken);
            CumulativeSizeStale = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The listing is still worth showing; the figure just cannot be refreshed.
            CumulativeSizeStale = true;
        }
    }

    /// <summary>The directory's modification time when the current listing was taken.</summary>
    private DateTimeOffset? _loadedMtime;

    /// <summary>
    /// Re-reads the listing if the directory has changed since it was taken.
    /// </summary>
    /// <returns><see langword="true"/> when the listing was re-read.</returns>
    /// <remarks>
    /// <para>
    /// One <c>statx</c> of the directory itself, compared against the time recorded by the last
    /// <see cref="Load"/>. Ranger does exactly this — <c>load_content_if_outdated</c>
    /// (<c>container/directory.py:686-711</c>) — and calls it from the draw path for every visible
    /// column, which is why a file created by a forked <c>cp</c>, or moved in from another pane,
    /// appears there almost at once.
    /// </para>
    /// <para>
    /// Canger had no equivalent: a listing was only re-read when something Canger itself did
    /// finished. So <c>shell -f cp …</c> created a file that stayed invisible, and a paste from
    /// another directory took an unbounded time to show up.
    /// </para>
    /// <para>
    /// Flat mode is deliberately excluded. Ranger compares the maximum mtime across every folded
    /// level, which means walking the tree on every draw; on a flattened directory of twenty
    /// thousand entries that would cost far more than the staleness it prevents. A flattened
    /// listing is refreshed by <c>:reload_cwd</c> instead.
    /// </para>
    /// </remarks>
    public bool LoadIfOutdated(CancellationToken cancellationToken = default)
    {
        if (!IsLoaded)
        {
            Load(cancellationToken);
            return true;
        }

        if (IsFlat)
        {
            return false;
        }

        DateTimeOffset? now = _fileSystem.GetStatus(Path, followSymbolicLinks: true)?.ModifyTime;

        // An unreadable directory is left alone: the listing already on screen is more use than
        // an empty one, and the error is reported by whatever tries to enter it.
        if (now is null || now == _loadedMtime)
        {
            return false;
        }

        Load(cancellationToken);
        return true;
    }

    /// <summary>Forgets a shallow count, so the next request reads the directory again.</summary>
    public void InvalidateCount()
    {
        _counted = false;
        _shallowCount = null;
    }

    /// <summary>The ordering applied to the listing.</summary>
    public SortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value)
            {
                return;
            }

            _sortOrder = value;
            ApplySort();
        }
    }

    /// <summary>
    /// Scans the directory and rebuilds the listing.
    /// </summary>
    /// <remarks>
    /// Marks are recorded by path before the scan and restored afterwards, so marking a set of
    /// files and then refreshing does not silently lose the selection.
    /// </remarks>
    /// <param name="cancellationToken">Abandons a scan of a large or slow directory.</param>
    public void Load(CancellationToken cancellationToken = default)
    {
        // Frozen means the listing is held as it is, so a directory being written to can be read
        // without it moving underfoot. A directory never loaded still loads once: freezing an
        // empty screen would show nothing at all rather than holding what is there.
        if (_cache is { Frozen: true } && IsLoaded)
        {
            return;
        }

        // Taken before the scan, because the scan is what makes this a *re*load.
        bool reloading = IsLoaded;

        // Only when there is a listing to read them from. After `Unload` there is not, and the
        // marks it saved on the way out are the ones to restore — asking again here would find an
        // empty listing, conclude nothing was marked, and quietly throw a selection away.
        if (reloading)
        {
            RememberMarks();
        }

        _allEntries.Clear();
        LoadError = null;

        try
        {
            if (IsFlat)
            {
                ScanFlattened(Path, FlatLevel, cancellationToken);
            }
            else
            {
                ScanInto(Path, relativeToPath: null, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LoadError = e.Message;
        }

        IsLoaded = true;
        LastLoaded = DateTimeOffset.UtcNow;

        // Summed here rather than on demand because the entries are already in hand, and because
        // it is what the status line asks for on every frame.
        DiskUsage = _allEntries.Where(e => !e.IsDirectory).Sum(e => e.Size ?? 0);

        // The directory's own modification time as it was when this listing was taken. Anything
        // added or removed since bumps it, which is how the listing notices a change it did not
        // make itself.
        _loadedMtime = _fileSystem.GetStatus(Path, followSymbolicLinks: true)?.ModifyTime;

        UpdateCumulativeSizeAfterReload(reloading, cancellationToken);

        RestoreMarks();
        ApplySort();
    }

    /// <summary>
    /// Folds entries from below this directory into the listing.
    /// </summary>
    /// <param name="level">
    /// How many levels to fold in. Zero restores the plain listing; a negative value folds in the
    /// whole tree.
    /// </param>
    /// <param name="cancellationToken">Abandons the rescan.</param>
    public void SetFlatLevel(int level, CancellationToken cancellationToken = default)
    {
        if (FlatLevel == level)
        {
            return;
        }

        FlatLevel = level;
        Load(cancellationToken);
    }

    /// <summary>
    /// How many times the visible listing has been rebuilt.
    /// </summary>
    /// <remarks>
    /// A tab keeps its own cursor, so it needs to know when the listing beneath it changed —
    /// after a reload the nodes it remembers may not be in the listing any more. Comparing a
    /// number is cheaper than searching the listing on every read.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>
    /// The total size of the files directly in this directory.
    /// </summary>
    /// <remarks>
    /// Files only. Subdirectories count for nothing, because knowing what is under them means
    /// walking the tree — which is what <c>:get_cumulative_size</c> is for, and is not something
    /// a listing should do unasked. Ranger sums the same thing during its own scan
    /// (<c>container/directory.py:443</c>), where it is likewise files only.
    /// </remarks>
    public long DiskUsage { get; private set; }

    /// <summary>Drops the listing, keeping everything the directory knows about itself.</summary>
    /// <returns>Whether there was a listing to drop.</returns>
    /// <remarks>
    /// <para>
    /// The entries are what a cached directory costs — around a kilobyte each — while the node
    /// itself is a path, a cursor row, a sort order and a set of marked paths. Dropping the
    /// entries and keeping the node frees effectively all of it and keeps the guarantee
    /// <see cref="DirectoryCache.Intern"/> depends on: one object per path, however it is reached.
    /// Discarding the node instead would let the next lookup mint a second one, and the cursor,
    /// the marks and any measured size would quietly be someone else's.
    /// </para>
    /// <para>
    /// Nothing has to be told this happened. <see cref="LoadIfOutdated"/> and
    /// <see cref="DirectoryCache.GetLoaded"/> both scan when <see cref="IsLoaded"/> is false, so
    /// the listing comes back the moment it is drawn or entered.
    /// </para>
    /// </remarks>
    public bool Unload()
    {
        if (!IsLoaded)
        {
            return false;
        }

        // Before the entries go: this reads them, and it is what puts the marks back on reload.
        RememberMarks();

        _allEntries.Clear();
        _allEntries.TrimExcess();
        _entries = [];
        Cursor.Forget();
        IsLoaded = false;

        return true;
    }

    /// <summary>Re-derives the filtered listing, after a filter or the hidden setting changed.</summary>
    public void Refilter()
    {
        FsNode? previous = Cursor.Current;

        _entries = [.. _allEntries.Where(Accepts)];
        Revision++;

        // Keep the cursor on the same entry when it survived the filter, rather than jumping.
        if (!Cursor.MoveToNode(previous, _entries))
        {
            Cursor.Reconcile(_entries);
        }
    }

    /// <summary>Marks or unmarks every visible entry.</summary>
    /// <param name="marked">Whether they should be marked.</param>
    public void SetAllMarked(bool marked)
    {
        foreach (FsNode entry in _entries)
        {
            entry.IsMarked = marked;
        }
    }

    /// <summary>Inverts which visible entries are marked.</summary>
    public void ToggleAllMarked()
    {
        foreach (FsNode entry in _entries)
        {
            entry.IsMarked = !entry.IsMarked;
        }
    }

    /// <summary>Unmarks everything.</summary>
    public void ClearMarks()
    {
        foreach (FsNode entry in _allEntries)
        {
            entry.IsMarked = false;
        }
    }

    /// <summary>Reapplies the current ordering, then the filters.</summary>
    private void ApplySort()
    {
        _shuffleSeed++;
        List<FsNode> sorted = NodeSorter.Sort(_allEntries, _sortOrder, _shuffleSeed);
        _allEntries.Clear();
        _allEntries.AddRange(sorted);
        Refilter();
    }

    /// <summary>Whether an entry survives the hidden setting, the live preview and the stack.</summary>
    private bool Accepts(FsNode node)
    {
        if (!ShowHidden && IsHidden(node))
        {
            return false;
        }

        if (PreviewFilter is { } preview && !preview.Accepts(node))
        {
            return false;
        }

        return FilterStack.Accepts(node);
    }

    /// <summary>
    /// Whether the hidden pattern matches the entry.
    /// </summary>
    /// <remarks>
    /// The pattern is tested against every path component rather than just the displayed name.
    /// That only matters in flat mode, where it is what stops a flattened listing filling up with
    /// the contents of dot-directories.
    /// </remarks>
    /// <summary>What <c>hidden_filter</c> starts out as: names beginning with a dot.</summary>
    private const string DefaultHiddenPattern = @"^\.";

    /// <summary>
    /// Compiles a hidden-file pattern, or returns null when there is nothing to hide.
    /// </summary>
    /// <remarks>
    /// Shared by the field initialiser and the setter, because keeping the pattern and its
    /// compiled form in step by hand is exactly the sort of thing that quietly stops hiding
    /// dotfiles.
    /// </remarks>
    private static System.Text.RegularExpressions.Regex? HiddenRegexFor(string pattern) =>
        pattern.Length == 0
            ? null
            : new System.Text.RegularExpressions.Regex(
                pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private bool IsHidden(FsNode node)
    {
        if (_hiddenRegex is not { } pattern)
        {
            return false;
        }

        // Walked as slices rather than `Split('/')`, which allocated a string array per entry per
        // filtering pass. Outside flat mode there is only ever one component, so the common case
        // is a single match against the whole name.
        ReadOnlySpan<char> remaining = node.RelativePath;

        while (!remaining.IsEmpty)
        {
            int slash = remaining.IndexOf('/');
            ReadOnlySpan<char> component = slash < 0 ? remaining : remaining[..slash];

            if (!component.IsEmpty && pattern.IsMatch(component))
            {
                return true;
            }

            remaining = slash < 0 ? default : remaining[(slash + 1)..];
        }

        return false;
    }

    /// <summary>Scans one directory level, adding its entries to the listing.</summary>
    private void ScanInto(string path, string? relativeToPath, CancellationToken cancellationToken)
    {
        foreach (Canger.Core.FileSystem.DirectoryEntry entry in
                 _fileSystem.ListDirectory(path, cancellationToken))
        {
            _allEntries.Add(Create(entry, path, relativeToPath));
        }
    }

    /// <summary>Scans a directory and, up to the given depth, everything beneath it.</summary>
    private void ScanFlattened(string path, int remainingLevels, CancellationToken cancellationToken)
    {
        Queue<(string Path, int Depth)> pending = new();
        pending.Enqueue((path, 0));

        while (pending.Count > 0)
        {
            (string current, int depth) = pending.Dequeue();

            IReadOnlyList<Canger.Core.FileSystem.DirectoryEntry> entries;
            try
            {
                entries = _fileSystem.ListDirectory(current, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable subdirectory should not abandon the whole flattened listing.
                continue;
            }

            foreach (Canger.Core.FileSystem.DirectoryEntry entry in entries)
            {
                FsNode node = Create(entry, current, relativeToPath: Path);
                _allEntries.Add(node);

                // A level of 1 means "fold in one level below this directory", so the scan
                // descends while the depth reached is still under the requested level.
                bool canDescend = remainingLevels < 0 || depth < remainingLevels;
                if (node.IsDirectory && canDescend)
                {
                    pending.Enqueue((node.Path, depth + 1));
                }
            }
        }
    }

    /// <summary>Builds the right kind of node for a scanned entry.</summary>
    private FsNode Create(Canger.Core.FileSystem.DirectoryEntry entry, string parentPath,
                          string? relativeToPath)
    {
        string path = parentPath == "/"
            ? "/" + entry.Name
            : parentPath + "/" + entry.Name;

        FileStatus? status = entry.TargetStatus ?? (entry.IsSymbolicLink ? null : entry.LinkStatus);

        if (!entry.IsDirectory)
        {
            return new FileNode(_fileSystem, path, status, entry.LinkStatus, relativeToPath);
        }

        // Interned, so this listing's row and the directory reached by entering it are the same
        // object. Without that a cumulative size measured here would be invisible to the reload
        // that is meant to mark it doubtful, and a cursor left inside would not show on the way
        // back out. Ranger interns from its scan for the same reason
        // (`container/directory.py:428`).
        return _cache is { } cache
            ? cache.Intern(path, status, entry.LinkStatus, relativeToPath)
            : new DirectoryNode(_fileSystem, path, status, entry.LinkStatus, relativeToPath);
    }

    private void RememberMarks()
    {
        _markedPaths.Clear();
        foreach (FsNode entry in _allEntries.Where(e => e.IsMarked))
        {
            _markedPaths.Add(entry.Path);
        }
    }

    private void RestoreMarks()
    {
        if (_markedPaths.Count == 0)
        {
            return;
        }

        foreach (FsNode entry in _allEntries)
        {
            entry.IsMarked = _markedPaths.Contains(entry.Path);
        }
    }
}
