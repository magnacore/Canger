// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Frozen;

namespace Canger.Core.Settings;

/// <summary>
/// The schema for every Canger setting.
/// </summary>
/// <remarks>
/// <para>
/// The names, types and enumerated value sets come from ranger's
/// <c>ALLOWED_SETTINGS</c> and <c>ALLOWED_VALUES</c>
/// (<c>ranger/container/settings.py:24-128</c>). The defaults are the effective ones — the
/// values ranger's shipped <c>rc.conf</c> assigns — rather than ranger's bare type fallbacks,
/// so Canger behaves correctly even with no configuration file present.
/// </para>
/// <para>
/// <c>SettingsCatalogTests</c> asserts these defaults agree with <c>config/cc.conf</c>, so the
/// two cannot drift apart.
/// </para>
/// </remarks>
public static class SettingsCatalog
{
    private static readonly SettingDefinition[] Definitions =
    [
        // ---- Appearance and layout -------------------------------------------------------
        Str("viewmode", "miller", ["miller", "multipane"],
            "Column layout: miller shows the hierarchy, multipane shows all tabs side by side."),
        IntList("column_ratios", [1, 3, 4],
            "Relative widths of the miller columns."),
        Str("colorscheme", "default", null,
            "Name of the colorscheme to use."),
        Str("draw_borders", "none", ["none", "both", "outline", "separators"],
            "Which borders to draw around and between columns."),
        Str("draw_borders_multipane", null, ["none", "both", "outline", "separators", "active-pane"],
            "Border style in multipane view. Falls back to draw_borders when unset.",
            allowsNull: true),
        Bool("padding_right", true,
            "Leave a padding column on the right when there is no preview."),
        Bool("collapse_preview", true,
            "Reclaim the preview column's width when there is nothing to preview."),
        Bool("status_bar_on_top", false,
            "Put the status bar above the columns instead of below."),
        Bool("draw_progress_bar_in_status_bar", true,
            "Overlay task progress on the status bar."),
        Bool("dirname_in_tabs", false,
            "Show the directory name in each tab label."),
        Bool("hostname_in_titlebar", true,
            "Show user@host in the title bar."),
        Bool("tilde_in_titlebar", false,
            "Abbreviate the home directory as ~ in the title bar."),
        Bool("show_selection_in_titlebar", true,
            "Show the selected file's name in the title bar."),
        Int("shorten_title", 3,
            "How many directory components to keep in the terminal title. 0 disables shortening."),
        Bool("update_title", false,
            "Set the terminal window title."),
        Bool("update_tmux_title", true,
            "Set the tmux or screen window name."),
        Str("line_numbers", "false", ["false", "absolute", "relative"],
            "Line numbering mode in the main column."),
        Bool("relative_current_zero", false,
            "With relative line numbers, show 0 rather than the absolute number on the cursor line."),
        Bool("one_indexed", false,
            "Number lines from 1 instead of 0."),
        Bool("unicode_ellipsis", false,
            "Use a single-character ellipsis when truncating names."),
        Bool("bidi_support", false,
            "Reorder right-to-left text for display."),
        Bool("show_cursor", false,
            "Leave the terminal's hardware cursor visible."),
        Bool("display_size_in_main_column", true,
            "Show file sizes in the main column."),
        Bool("display_size_in_status_bar", true,
            "Show the size of the selected file in the status bar."),
        Bool("display_free_space_in_status_bar", true,
            "Show free space on the current filesystem in the status bar."),
        Bool("display_tags_in_all_columns", true,
            "Show tag markers in every column, not only the main one."),
        Bool("size_in_bytes", false,
            "Show exact byte counts instead of human-readable sizes."),
        Bool("binary_size_prefix", false,
            "Use binary prefixes (KiB, MiB) instead of decimal ones."),
        Int("hint_collapse_threshold", 10,
            "Collapse the keybinding hint list once it exceeds this many entries."),

        // ---- Sorting ---------------------------------------------------------------------
        Str("sort", "natural",
            ["natural", "basename", "size", "mtime", "ctime", "atime", "random", "type", "extension"],
            "Which key to sort directory entries by."),
        Bool("sort_reverse", false, "Reverse the sort order."),
        Bool("sort_case_insensitive", true, "Ignore case when sorting by name."),
        Bool("sort_directories_first", true, "List directories before files."),
        Bool("sort_unicode", false, "Sort by Unicode collation rather than raw code points."),

        // ---- Filtering and visibility ----------------------------------------------------
        Bool("show_hidden", false, "Show files the hidden filter would otherwise remove."),
        Str("hidden_filter", @"^\.", null,
            "Regular expression matching names to hide."),
        Str("global_inode_type_filter", "", null,
            "Restrict every directory to these inode types: any of d, f and l."),
        Bool("clear_filters_on_dir_change", false,
            "Drop the active filter when entering another directory."),
        Bool("show_hidden_bookmarks", true,
            "Include bookmarks pointing inside hidden directories."),
        Bool("freeze_files", false,
            "Stop re-reading file metadata, for use on very slow filesystems."),

        // ---- Previews --------------------------------------------------------------------
        Bool("preview_files", true, "Preview the selected file."),
        Bool("preview_directories", true, "Preview the contents of the selected directory."),
        Bool("preview_images", false, "Draw image previews using the terminal's graphics protocol."),
        Str("preview_images_method", "w3m",
            ["w3m", "iterm2", "terminology", "sixel", "urxvt", "urxvt-full", "kitty", "ueberzug"],
            "Which terminal image protocol to use."),
        Str("sixel_dithering", "FloydSteinberg", ["None", "Riemersma", "FloydSteinberg"],
            "Dithering mode for SIXEL previews."),
        Bool("use_preview_script", true, "Generate previews with the external preview script."),
        Str("preview_script", null, null,
            "Path to the preview script. Defaults to scope.sh in the config directory.",
            allowsNull: true),
        Int("preview_max_size", 0,
            "Skip previews for files larger than this many bytes. 0 means no limit."),
        Bool("wrap_plaintext_previews", false, "Wrap long lines in text previews."),
        Bool("open_all_images", true,
            "Hand every image in the directory to the image viewer, not just the selected one."),
        Int("iterm2_font_width", 8, "Character cell width in pixels, for the iTerm2 protocol."),
        Int("iterm2_font_height", 11, "Character cell height in pixels, for the iTerm2 protocol."),
        Float("w3m_delay", 0.02, "Seconds to wait after drawing with w3mimgdisplay."),
        Int("w3m_offset", 0, "Pixel offset correction for w3mimgdisplay."),

        // ---- Navigation and input --------------------------------------------------------
        Int("scroll_offset", 8, "Keep this many lines visible above and below the cursor."),
        Bool("wrap_scroll", false, "Wrap around when scrolling past either end of a listing."),
        Bool("mouse_enabled", true, "Respond to mouse input."),
        Bool("flushinput", true, "Discard input typed while a slow operation was running."),
        Int("idle_delay", 2000, "Milliseconds of inactivity before Canger stops polling."),
        Bool("xterm_alt_key", false, "Interpret xterm's alternative Alt-key encoding."),
        Bool("cd_bookmarks", true, "Offer bookmarks as completions for :cd."),
        Str("cd_tab_case", "sensitive", ["sensitive", "insensitive", "smart"],
            "Case sensitivity of :cd tab completion."),
        Bool("cd_tab_fuzzy", false, "Match :cd completions loosely rather than by prefix."),

        // ---- Tabs, history and state -----------------------------------------------------
        Int("max_history_size", 20, "Directory history entries kept per tab.", allowsNull: true),
        Int("max_console_history_size", 50, "Console history entries kept.", allowsNull: true),
        Bool("save_console_history", true, "Persist console history between runs."),
        Bool("save_tabs_on_exit", false, "Restore open tabs on the next run."),
        Bool("filter_dead_tabs_on_startup", false, "Drop restored tabs whose path no longer exists."),
        Bool("renumber_tabs_on_tab_close", false, "Renumber tabs so they stay consecutive."),
        Bool("autosave_bookmarks", true, "Write bookmarks to disk as soon as they change."),
        Bool("save_backtick_bookmark", true, "Persist the previous-directory bookmark."),
        Bool("metadata_deep_search", false, "Search parent directories for metadata files."),

        // ---- Behaviour -------------------------------------------------------------------
        Str("confirm_on_delete", "multiple", ["multiple", "always", "never"],
            "When to ask for confirmation before deleting."),
        Str("confirm_on_trash", "like_delete", ["like_delete", "multiple", "always", "never"],
            "When to ask for confirmation before moving to the trash."),
        Bool("automatically_count_files", true, "Count the entries in directories for display."),
        Bool("autoupdate_cumulative_size", false, "Keep recalculating directory sizes as they change."),
        Str("nested_canger_warning", "true", ["true", "false", "error"],
            "Warn when starting Canger inside a shell that Canger itself started."),

        // ---- Version control -------------------------------------------------------------
        Bool("vcs_aware", false, "Show version-control status alongside files."),
        Str("vcs_backend_git", "enabled", ["enabled", "disabled", "local"], "Git integration."),
        Str("vcs_backend_hg", "disabled", ["disabled", "local", "enabled"], "Mercurial integration."),
        Str("vcs_backend_svn", "disabled", ["disabled", "local", "enabled"], "Subversion integration."),
        Str("vcs_backend_bzr", "disabled", ["disabled", "local", "enabled"], "Bazaar integration."),
        Int("vcs_msg_length", 50, "Characters of the latest commit message to show."),
    ];

    /// <summary>Every setting, keyed by name.</summary>
    public static readonly FrozenDictionary<string, SettingDefinition> All =
        Definitions.ToFrozenDictionary(d => d.Name, StringComparer.Ordinal);

    /// <summary>Setting names in alphabetical order, for tab completion.</summary>
    public static readonly IReadOnlyList<string> Names =
        [.. Definitions.Select(d => d.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Ranger names Canger still answers to.
    /// </summary>
    /// <remarks>
    /// A setting whose name carried the program's own name had to be renamed, but a configuration
    /// file carried over from ranger will still use the old spelling — and a whole file being
    /// rejected over one word would be a poor greeting. The ranger name is accepted silently.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nested_ranger_warning"] = "nested_canger_warning",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Looks up a setting's schema.</summary>
    /// <param name="name">The setting name, which may be a name ranger used.</param>
    /// <returns>The definition, or <see langword="null"/> when no such setting exists.</returns>
    public static SettingDefinition? Find(string name)
    {
        if (All.TryGetValue(name, out SettingDefinition? definition))
        {
            return definition;
        }

        return Aliases.TryGetValue(name, out string? canonical)
               && All.TryGetValue(canonical, out definition)
            ? definition
            : null;
    }

    /// <summary>Looks up a setting's schema, throwing when it does not exist.</summary>
    /// <param name="name">The setting name.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="SettingValueException">No setting has that name.</exception>
    public static SettingDefinition Require(string name) =>
        Find(name) ?? throw new SettingValueException($"No such setting: '{name}'.");

    private static SettingDefinition Bool(string name, bool value, string summary) =>
        new(name, SettingKind.Boolean, value, Summary: summary);

    private static SettingDefinition Int(string name, int value, string summary,
                                         bool allowsNull = false) =>
        new(name, SettingKind.Integer, value, AllowsNull: allowsNull, Summary: summary);

    private static SettingDefinition Float(string name, double value, string summary) =>
        new(name, SettingKind.Float, value, Summary: summary);

    private static SettingDefinition Str(string name, string? value, IReadOnlyList<string>? allowed,
                                         string summary, bool allowsNull = false) =>
        new(name, SettingKind.String, value, allowed, allowsNull, summary);

    private static SettingDefinition IntList(string name, int[] value, string summary) =>
        new(name, SettingKind.IntegerList, value, Summary: summary);
}
