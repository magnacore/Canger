// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Settings;

/// <summary>
/// A typed view over <see cref="ISettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// The store is keyed by string because cc.conf, the <c>:set</c> command and plugins all address
/// settings by name at run time. That is unavoidable at the edges, but it should not leak into
/// the rest of the codebase: a typo in <c>Get("sort_reverse")</c> would compile happily and fail
/// only when the code ran.
/// </para>
/// <para>
/// This wrapper gives every setting a compile-checked property of the right type, so widgets and
/// the model read <c>settings.SortReverse</c> instead. Both routes reach the same store, so
/// scoping and the change pipeline behave identically either way.
/// </para>
/// </remarks>
/// <param name="settings">The underlying store.</param>
public sealed class CangerSettings(ISettings settings)
{
    /// <summary>The underlying store, for access by name.</summary>
    public ISettings Raw => settings;

    /// <summary>
    /// Reads a setting that cannot be null, falling back to its declared default.
    /// </summary>
    /// <remarks>
    /// The fallback is defensive rather than expected: every non-nullable setting is seeded with
    /// its default when the store is constructed. Returning the declared default rather than an
    /// empty string or list keeps behaviour correct if a scope ever resolves to nothing.
    /// </remarks>
    /// <summary>
    /// Where the user is, so a setting scoped to a directory can be found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this every read is global, and <c>setinregex</c>, <c>setinpath</c> and
    /// <c>setintag</c> are parsed, stored, reported without error — and never consulted. A rule
    /// saying "sort this one directory by date" simply did nothing.
    /// </para>
    /// <para>
    /// A function rather than a value, because settings are read between frames as well as
    /// during them and the answer has to be current at the moment of the read. Ranger reaches
    /// for <c>fm.thisdir.path</c> inside the lookup for the same reason
    /// (<c>container/settings.py:222-235</c>).
    /// </para>
    /// </remarks>
    public Func<string?>? CurrentPath { get; set; }

    private T Required<T>(string name) where T : notnull =>
        settings.Get<T>(name, CurrentPath?.Invoke()) is T value
            ? value
            : (T)SettingsCatalog.Require(name).DefaultValue!;

    // ---- Appearance and layout ------------

    /// <summary>The <c>viewmode</c> setting.</summary>
    public string Viewmode => Required<string>("viewmode");

    /// <summary>
    /// Where an encrypted drive asks for its passphrase.
    /// </summary>
    /// <remarks>
    /// <c>terminal</c>, the default, hands the screen to <c>udisksctl</c> and lets it prompt, as
    /// it always has: nothing of the passphrase passes through Canger, and nothing is kept.
    /// <c>builtin</c> asks inside Canger, which is what makes it possible to offer to remember
    /// the answer — in the desktop's own keyring, under the same schema Thunar and GNOME Disks
    /// use, so a passphrase saved in either works in the other.
    /// </remarks>
    public string UnlockPrompt => Required<string>("unlock_prompt");

    /// <summary>The <c>column_ratios</c> setting.</summary>
    public IReadOnlyList<int> ColumnRatios => Required<IReadOnlyList<int>>("column_ratios");

    /// <summary>The <c>colorscheme</c> setting.</summary>
    public string Colorscheme => Required<string>("colorscheme");

    /// <summary>The <c>draw_borders</c> setting.</summary>
    public string DrawBorders => Required<string>("draw_borders");

    /// <summary>The <c>draw_borders_multipane</c> setting.</summary>
    public string? DrawBordersMultipane => settings.Get<string>("draw_borders_multipane", CurrentPath?.Invoke());

    /// <summary>The <c>padding_right</c> setting.</summary>
    public bool PaddingRight => settings.Get<bool>("padding_right", CurrentPath?.Invoke());

    /// <summary>The <c>collapse_preview</c> setting.</summary>
    public bool CollapsePreview => settings.Get<bool>("collapse_preview", CurrentPath?.Invoke());

    /// <summary>The <c>status_bar_on_top</c> setting.</summary>
    public bool StatusBarOnTop => settings.Get<bool>("status_bar_on_top", CurrentPath?.Invoke());

    /// <summary>The <c>draw_progress_bar_in_status_bar</c> setting.</summary>
    public bool DrawProgressBarInStatusBar => settings.Get<bool>("draw_progress_bar_in_status_bar", CurrentPath?.Invoke());

    /// <summary>The <c>dirname_in_tabs</c> setting.</summary>
    public bool DirnameInTabs => settings.Get<bool>("dirname_in_tabs", CurrentPath?.Invoke());

    /// <summary>The <c>hostname_in_titlebar</c> setting.</summary>
    public bool HostnameInTitlebar => settings.Get<bool>("hostname_in_titlebar", CurrentPath?.Invoke());

    /// <summary>The <c>tilde_in_titlebar</c> setting.</summary>
    public bool TildeInTitlebar => settings.Get<bool>("tilde_in_titlebar", CurrentPath?.Invoke());

    /// <summary>The <c>show_selection_in_titlebar</c> setting.</summary>
    public bool ShowSelectionInTitlebar => settings.Get<bool>("show_selection_in_titlebar", CurrentPath?.Invoke());

    /// <summary>The <c>shorten_title</c> setting.</summary>
    public int ShortenTitle => settings.Get<int>("shorten_title", CurrentPath?.Invoke());

    /// <summary>The <c>update_title</c> setting.</summary>
    public bool UpdateTitle => settings.Get<bool>("update_title", CurrentPath?.Invoke());

    /// <summary>The <c>update_tmux_title</c> setting.</summary>
    public bool UpdateTmuxTitle => settings.Get<bool>("update_tmux_title", CurrentPath?.Invoke());

    /// <summary>The <c>line_numbers</c> setting.</summary>
    public string LineNumbers => Required<string>("line_numbers");

    /// <summary>The <c>relative_current_zero</c> setting.</summary>
    public bool RelativeCurrentZero => settings.Get<bool>("relative_current_zero", CurrentPath?.Invoke());

    /// <summary>The <c>one_indexed</c> setting.</summary>
    public bool OneIndexed => settings.Get<bool>("one_indexed", CurrentPath?.Invoke());

    /// <summary>The <c>unicode_ellipsis</c> setting.</summary>
    public bool UnicodeEllipsis => settings.Get<bool>("unicode_ellipsis", CurrentPath?.Invoke());

    /// <summary>The <c>bidi_support</c> setting.</summary>
    public bool BidiSupport => settings.Get<bool>("bidi_support", CurrentPath?.Invoke());

    /// <summary>The <c>show_cursor</c> setting.</summary>
    public bool ShowCursor => settings.Get<bool>("show_cursor", CurrentPath?.Invoke());

    /// <summary>The <c>display_size_in_main_column</c> setting.</summary>
    public bool DisplaySizeInMainColumn => settings.Get<bool>("display_size_in_main_column", CurrentPath?.Invoke());

    /// <summary>The <c>display_size_in_status_bar</c> setting.</summary>
    public bool DisplaySizeInStatusBar => settings.Get<bool>("display_size_in_status_bar", CurrentPath?.Invoke());

    /// <summary>The <c>display_free_space_in_status_bar</c> setting.</summary>
    public bool DisplayFreeSpaceInStatusBar => settings.Get<bool>("display_free_space_in_status_bar", CurrentPath?.Invoke());

    /// <summary>The <c>display_tags_in_all_columns</c> setting.</summary>
    public bool DisplayTagsInAllColumns => settings.Get<bool>("display_tags_in_all_columns", CurrentPath?.Invoke());

    /// <summary>The <c>size_in_bytes</c> setting.</summary>
    public bool SizeInBytes => settings.Get<bool>("size_in_bytes", CurrentPath?.Invoke());

    /// <summary>The <c>binary_size_prefix</c> setting.</summary>
    public bool BinarySizePrefix => settings.Get<bool>("binary_size_prefix", CurrentPath?.Invoke());

    /// <summary>The <c>hint_collapse_threshold</c> setting.</summary>
    public int HintCollapseThreshold => settings.Get<int>("hint_collapse_threshold", CurrentPath?.Invoke());


    // ---- Sorting ------------

    /// <summary>The <c>sort</c> setting.</summary>
    public string Sort => Required<string>("sort");

    /// <summary>The <c>sort_reverse</c> setting.</summary>
    public bool SortReverse => settings.Get<bool>("sort_reverse", CurrentPath?.Invoke());

    /// <summary>The <c>sort_case_insensitive</c> setting.</summary>
    public bool SortCaseInsensitive => settings.Get<bool>("sort_case_insensitive", CurrentPath?.Invoke());

    /// <summary>The <c>sort_directories_first</c> setting.</summary>
    public bool SortDirectoriesFirst => settings.Get<bool>("sort_directories_first", CurrentPath?.Invoke());

    /// <summary>The <c>sort_unicode</c> setting.</summary>
    public bool SortUnicode => settings.Get<bool>("sort_unicode", CurrentPath?.Invoke());


    // ---- Filtering and visibility ------------

    /// <summary>The <c>show_hidden</c> setting.</summary>
    public bool ShowHidden => settings.Get<bool>("show_hidden", CurrentPath?.Invoke());

    /// <summary>The <c>hidden_filter</c> setting.</summary>
    public string HiddenFilter => Required<string>("hidden_filter");

    /// <summary>The <c>global_inode_type_filter</c> setting.</summary>
    public string GlobalInodeTypeFilter => Required<string>("global_inode_type_filter");

    /// <summary>The <c>clear_filters_on_dir_change</c> setting.</summary>
    public bool ClearFiltersOnDirChange => settings.Get<bool>("clear_filters_on_dir_change", CurrentPath?.Invoke());

    /// <summary>The <c>show_hidden_bookmarks</c> setting.</summary>
    public bool ShowHiddenBookmarks => settings.Get<bool>("show_hidden_bookmarks", CurrentPath?.Invoke());

    /// <summary>The <c>freeze_files</c> setting.</summary>
    public bool FreezeFiles => settings.Get<bool>("freeze_files", CurrentPath?.Invoke());

    /// <summary>The <c>shared_copy_buffer</c> setting.</summary>
    /// <remarks>
    /// Off by default: two Cangers open on unrelated work should not have <c>dd</c> in one arm
    /// <c>pp</c> in the other. Ranger has no equivalent at all.
    /// </remarks>
    public bool SharedCopyBuffer => settings.Get<bool>("shared_copy_buffer", CurrentPath?.Invoke());


    // ---- Previews ------------

    /// <summary>The <c>preview_files</c> setting.</summary>
    public bool PreviewFiles => settings.Get<bool>("preview_files", CurrentPath?.Invoke());

    /// <summary>The <c>preview_directories</c> setting.</summary>
    public bool PreviewDirectories => settings.Get<bool>("preview_directories", CurrentPath?.Invoke());

    /// <summary>The <c>preview_images</c> setting.</summary>
    public bool PreviewImages => settings.Get<bool>("preview_images", CurrentPath?.Invoke());

    /// <summary>The <c>preview_images_method</c> setting.</summary>
    public string PreviewImagesMethod => Required<string>("preview_images_method");

    /// <summary>The <c>sixel_dithering</c> setting.</summary>
    public string SixelDithering => Required<string>("sixel_dithering");

    /// <summary>The <c>use_preview_script</c> setting.</summary>
    public bool UsePreviewScript => settings.Get<bool>("use_preview_script", CurrentPath?.Invoke());

    /// <summary>The <c>preview_script</c> setting.</summary>
    public string? PreviewScript => settings.Get<string>("preview_script", CurrentPath?.Invoke());

    /// <summary>The <c>preview_max_size</c> setting.</summary>
    public int PreviewMaxSize => settings.Get<int>("preview_max_size", CurrentPath?.Invoke());

    /// <summary>The <c>wrap_plaintext_previews</c> setting.</summary>
    public bool WrapPlaintextPreviews => settings.Get<bool>("wrap_plaintext_previews", CurrentPath?.Invoke());

    /// <summary>The <c>open_all_images</c> setting.</summary>
    public bool OpenAllImages => settings.Get<bool>("open_all_images", CurrentPath?.Invoke());

    /// <summary>The <c>iterm2_font_width</c> setting.</summary>
    public int ITerm2FontWidth => settings.Get<int>("iterm2_font_width", CurrentPath?.Invoke());

    /// <summary>The <c>iterm2_font_height</c> setting.</summary>
    public int ITerm2FontHeight => settings.Get<int>("iterm2_font_height", CurrentPath?.Invoke());

    /// <summary>The <c>w3m_delay</c> setting.</summary>
    public double W3mDelay => settings.Get<double>("w3m_delay", CurrentPath?.Invoke());

    /// <summary>The <c>w3m_offset</c> setting.</summary>
    public int W3mOffset => settings.Get<int>("w3m_offset", CurrentPath?.Invoke());


    // ---- Navigation and input ------------

    /// <summary>The <c>scroll_offset</c> setting.</summary>
    public int ScrollOffset => settings.Get<int>("scroll_offset", CurrentPath?.Invoke());

    /// <summary>The <c>wrap_scroll</c> setting.</summary>
    public bool WrapScroll => settings.Get<bool>("wrap_scroll", CurrentPath?.Invoke());

    /// <summary>The <c>mouse_enabled</c> setting.</summary>
    public bool MouseEnabled => settings.Get<bool>("mouse_enabled", CurrentPath?.Invoke());

    /// <summary>The <c>flushinput</c> setting.</summary>
    public bool Flushinput => settings.Get<bool>("flushinput", CurrentPath?.Invoke());

    /// <summary>The <c>idle_delay</c> setting.</summary>
    public int IdleDelay => settings.Get<int>("idle_delay", CurrentPath?.Invoke());

    /// <summary>The <c>xterm_alt_key</c> setting.</summary>
    public bool XtermAltKey => settings.Get<bool>("xterm_alt_key", CurrentPath?.Invoke());

    /// <summary>The <c>cd_bookmarks</c> setting.</summary>
    public bool CdBookmarks => settings.Get<bool>("cd_bookmarks", CurrentPath?.Invoke());

    /// <summary>The <c>cd_tab_case</c> setting.</summary>
    public string CdTabCase => Required<string>("cd_tab_case");

    /// <summary>The <c>cd_tab_fuzzy</c> setting.</summary>
    public bool CdTabFuzzy => settings.Get<bool>("cd_tab_fuzzy", CurrentPath?.Invoke());


    // ---- Tabs, history and state ------------

    /// <summary>The <c>max_history_size</c> setting.</summary>
    public int? MaxHistorySize => settings.Get<int>("max_history_size", CurrentPath?.Invoke());

    /// <summary>The <c>max_console_history_size</c> setting.</summary>
    public int? MaxConsoleHistorySize => settings.Get<int>("max_console_history_size", CurrentPath?.Invoke());

    /// <summary>The <c>save_console_history</c> setting.</summary>
    public bool SaveConsoleHistory => settings.Get<bool>("save_console_history", CurrentPath?.Invoke());

    /// <summary>The <c>save_tabs_on_exit</c> setting.</summary>
    public bool SaveTabsOnExit => settings.Get<bool>("save_tabs_on_exit", CurrentPath?.Invoke());

    /// <summary>The <c>filter_dead_tabs_on_startup</c> setting.</summary>
    public bool FilterDeadTabsOnStartup => settings.Get<bool>("filter_dead_tabs_on_startup", CurrentPath?.Invoke());

    /// <summary>The <c>renumber_tabs_on_tab_close</c> setting.</summary>
    public bool RenumberTabsOnTabClose => settings.Get<bool>("renumber_tabs_on_tab_close", CurrentPath?.Invoke());

    /// <summary>The <c>autosave_bookmarks</c> setting.</summary>
    public bool AutosaveBookmarks => settings.Get<bool>("autosave_bookmarks", CurrentPath?.Invoke());

    /// <summary>The <c>save_backtick_bookmark</c> setting.</summary>
    public bool SaveBacktickBookmark => settings.Get<bool>("save_backtick_bookmark", CurrentPath?.Invoke());

    /// <summary>The <c>metadata_deep_search</c> setting.</summary>
    public bool MetadataDeepSearch => settings.Get<bool>("metadata_deep_search", CurrentPath?.Invoke());


    // ---- Behaviour ------------

    /// <summary>The <c>confirm_on_delete</c> setting.</summary>
    public string ConfirmOnDelete => Required<string>("confirm_on_delete");

    /// <summary>The <c>confirm_on_trash</c> setting.</summary>
    public string ConfirmOnTrash => Required<string>("confirm_on_trash");

    /// <summary>The <c>automatically_count_files</c> setting.</summary>
    public bool AutomaticallyCountFiles => settings.Get<bool>("automatically_count_files", CurrentPath?.Invoke());

    /// <summary>The <c>autoupdate_cumulative_size</c> setting.</summary>
    public bool AutoupdateCumulativeSize => settings.Get<bool>("autoupdate_cumulative_size", CurrentPath?.Invoke());

    /// <summary>The <c>nested_canger_warning</c> setting.</summary>
    public string NestedCangerWarning => Required<string>("nested_canger_warning");


    // ---- Version control ------------

    /// <summary>The <c>vcs_aware</c> setting.</summary>
    public bool VcsAware => settings.Get<bool>("vcs_aware", CurrentPath?.Invoke());

    /// <summary>The <c>vcs_backend_git</c> setting.</summary>
    public string VcsBackendGit => Required<string>("vcs_backend_git");

    /// <summary>The <c>vcs_backend_hg</c> setting.</summary>
    public string VcsBackendMercurial => Required<string>("vcs_backend_hg");

    /// <summary>The <c>vcs_backend_svn</c> setting.</summary>
    public string VcsBackendSubversion => Required<string>("vcs_backend_svn");

    /// <summary>The <c>vcs_backend_bzr</c> setting.</summary>
    public string VcsBackendBazaar => Required<string>("vcs_backend_bzr");

    /// <summary>The <c>vcs_msg_length</c> setting.</summary>
    public int VcsMessageLength => settings.Get<int>("vcs_msg_length", CurrentPath?.Invoke());
}
