// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// The top line: who and where you are, the file under the cursor, and the open tabs.
/// </summary>
/// <remarks>
/// The path is drawn a component at a time rather than as one string, so each part can be
/// coloured and, later, clicked. When the line is too narrow the path is shortened from the left,
/// because the end of a path identifies where you are far better than its beginning.
/// </remarks>
public sealed class TitleBar(IColorScheme colorScheme) : Widget
{
    /// <summary>
    /// The cells a tab's directory name may occupy before it is cut short.
    /// </summary>
    /// <remarks>
    /// Fifteen, as in ranger (<c>titlebar.py:157</c>). Enough for most names, and small enough
    /// that a handful of tabs still fit on one line beside the path.
    /// </remarks>
    private const int TabNameWidth = 15;

    /// <summary>The tab being shown.</summary>
    public Tab? Tab { get; set; }

    /// <summary>The user name shown on the left, when enabled.</summary>
    public string? UserName { get; set; }

    /// <summary>The host name shown on the left, when enabled.</summary>
    public string? HostName { get; set; }

    /// <summary>Whether the session is running as root, which is shown as a warning.</summary>
    public bool IsRoot { get; set; }

    /// <summary>Whether to abbreviate the home directory as a tilde.</summary>
    public bool TildeInTitlebar { get; set; }

    /// <summary>Whether to append the name of the file under the cursor.</summary>
    public bool ShowSelection { get; set; } = true;

    /// <summary>The keys typed so far, shown on the right while a sequence is incomplete.</summary>
    public string KeyBuffer { get; set; } = string.Empty;

    /// <summary>
    /// One character saying that background work is going on, or empty when none is.
    /// </summary>
    /// <remarks>
    /// Comes from <see cref="Canger.Core.Tasks.TaskQueue.Throbber"/>. It is the only sign a
    /// queued command gives without the task view open, so it is what tells the user their
    /// archive is being unpacked rather than ignored.
    /// </remarks>
    public string Throbber { get; set; } = string.Empty;

    /// <summary>The open tabs, in ascending number order, for the tab list on the right.</summary>
    public IReadOnlyList<TabHeading> Tabs { get; set; } = [];

    /// <summary>Which tab is active.</summary>
    public int ActiveTabNumber { get; set; } = 1;

    /// <summary>Whether a tab is labelled with its directory's name as well as its number.</summary>
    /// <remarks>The <c>dirname_in_tabs</c> setting; off in ranger's shipped defaults.</remarks>
    public bool DirnameInTabs { get; set; }

    /// <summary>The marker for a name too long to fit: <c>~</c>, or <c>…</c> when asked.</summary>
    /// <remarks>The <c>unicode_ellipsis</c> setting.</remarks>
    public string Ellipsis { get; set; } = "~";

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        CellStyle baseStyle = colorScheme.Resolve(StyleContext.Of(ContextKey.InTitlebar));
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, 1, baseStyle);

        // The right-hand side is laid out first so the path knows how much room it has left.
        int rightWidth = DrawRight(screen);
        int spinner = DrawThrobber(screen, rightWidth);
        DrawLeft(screen, Math.Max(Bounds.Width - rightWidth - spinner, 0));
    }

    /// <summary>Draws the user, host and path, shortening from the left when it will not fit.</summary>
    private void DrawLeft(ScreenBuffer screen, int available)
    {
        int x = Bounds.X;

        // Dropped rather than truncated when the tabs have taken the line: half a hostname is
        // no use to anyone, and writing it anyway would land on top of them.
        if (UserName is { Length: > 0 } user && HostName is { Length: > 0 } host
            && CellWidth.Of($"{user}@{host}:") <= available)
        {
            CellStyle style = colorScheme.Resolve(
                StyleContext.Of(ContextKey.InTitlebar, ContextKey.Hostname)
                            .With(IsRoot, ContextKey.Bad)
                            .With(!IsRoot, ContextKey.Good));

            x += screen.Write(x, Bounds.Y, $"{user}@{host}", style);
            x += screen.Write(x, Bounds.Y, ":", colorScheme.Resolve(
                StyleContext.Of(ContextKey.InTitlebar)));
        }

        if (Tab is not { } tab)
        {
            return;
        }

        string path = DisplayPath(tab.Path);
        string selected = ShowSelection && tab.Selected is { } node ? node.RelativePath : string.Empty;

        int remaining = Bounds.X + available - x;
        if (remaining <= 0)
        {
            return;
        }

        // Shorten from the left: the end of a path says where you are, the start rarely does.
        string text = path + (selected.Length > 0 ? "/" + selected : string.Empty);
        WideString wide = new(text);
        if (wide.Width > remaining)
        {
            text = "..." + wide.Slice(wide.Width - remaining + 3, remaining - 3);
        }

        CellStyle directoryStyle = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InTitlebar, ContextKey.Directory));
        int written = screen.Write(x, Bounds.Y, new WideString(text).Truncate(remaining),
                                   directoryStyle);

        // The selected filename is drawn in the plain title colour so it stands apart from the
        // path it sits at the end of.
        if (selected.Length > 0 && written > selected.Length)
        {
            CellStyle fileStyle = colorScheme.Resolve(
                StyleContext.Of(ContextKey.InTitlebar, ContextKey.File));
            screen.Recolor(x + written - selected.Length, Bounds.Y, selected.Length, fileStyle);
        }
    }

    /// <summary>The version-control branch, when there is one to show.</summary>
    public string? Branch { get; set; }

    /// <summary>
    /// A character standing for how the repository compares with its remote.
    /// </summary>
    /// <remarks>
    /// Arrows rather than words: this sits beside the branch on a line that is already crowded,
    /// and the direction is the whole message.
    /// </remarks>
    public string RemoteMarker { get; set; } = string.Empty;

    /// <summary>Draws the key buffer and the tab list, and reports how much width they took.</summary>
    private int DrawRight(ScreenBuffer screen)
    {
        List<(string Text, CellStyle Style)> segments = [];

        // The branch goes first, furthest from the tabs: it changes rarely, so it belongs where
        // it can be read at a glance rather than where the eye is already busy.
        if (Branch is { Length: > 0 } branch)
        {
            segments.Add(($" {branch}{RemoteMarker} ", colorScheme.Resolve(
                StyleContext.Of(ContextKey.InTitlebar, ContextKey.VcsInfo))));
        }

        if (KeyBuffer.Length > 0)
        {
            segments.Add((KeyBuffer + " ", colorScheme.Resolve(
                StyleContext.Of(ContextKey.InTitlebar, ContextKey.KeyBuffer))));
        }

        // The tabs are offered the whole line, not what the branch has left of it: they outrank
        // it, so it is the branch that gives way below rather than a tab going missing for it.
        segments.AddRange(VisibleTabs(Bounds.Width));

        // Whatever is left over is dropped from the front, which is the order of least value:
        // the branch before the key buffer, and both before any tab.
        int width = segments.Sum(s => CellWidth.Of(s.Text));

        while (width > Bounds.Width && segments.Count > 1)
        {
            width -= CellWidth.Of(segments[0].Text);
            segments.RemoveAt(0);
        }

        if (width == 0 || width > Bounds.Width)
        {
            return 0;
        }

        int x = Bounds.Right - width;
        foreach ((string text, CellStyle style) in segments)
        {
            x += screen.Write(x, Bounds.Y, text, style);
        }

        return width;
    }

    /// <summary>Picks the tabs that fit, and renders each.</summary>
    /// <param name="budget">The cells the tab list may occupy.</param>
    /// <returns>The drawable segments, in left-to-right order.</returns>
    /// <remarks>
    /// A single tab is not worth listing — it would say what the path on the left already says —
    /// and ranger hides the list for the same reason (<c>titlebar.py:145</c>).
    ///
    /// When they do not all fit, the list grows outwards from the active tab rather than being
    /// cut from one end, so the tab you are actually on is the one guaranteed to be visible.
    /// Ranger has no equivalent because its tab parts are <c>fixed=True</c> and its bar shrinks
    /// the path instead (<c>gui/bar.py</c>) — which works until the tabs alone are wider than the
    /// line. Canger used to drop the entire right-hand side in that case, so a narrow terminal
    /// showed no tabs at all and gave no hint that any were open.
    /// </remarks>
    private List<(string Text, CellStyle Style)> VisibleTabs(int budget)
    {
        if (Tabs.Count <= 1)
        {
            return [];
        }

        int active = Math.Max(Tabs.ToList().FindIndex(t => t.Number == ActiveTabNumber), 0);
        string[] texts = [.. Tabs.Select(TabText)];
        int used = CellWidth.Of(texts[active]);
        int first = active;
        int last = active;

        // Alternate outwards, taking the later tab first so the list reads forwards.
        for (bool grew = true; grew; )
        {
            grew = false;

            if (last + 1 < texts.Length && used + CellWidth.Of(texts[last + 1]) <= budget)
            {
                used += CellWidth.Of(texts[++last]);
                grew = true;
            }

            if (first > 0 && used + CellWidth.Of(texts[first - 1]) <= budget)
            {
                used += CellWidth.Of(texts[--first]);
                grew = true;
            }
        }

        List<(string Text, CellStyle Style)> segments = [];

        for (int i = first; i <= last; i++)
        {
            segments.Add((
                texts[i],
                colorScheme.Resolve(
                    StyleContext.Of(ContextKey.InTitlebar, ContextKey.Tab)
                                .With(Tabs[i].Number == ActiveTabNumber, ContextKey.Good))));
        }

        return segments;
    }

    /// <summary>Draws the spinner that says work is going on.</summary>
    /// <param name="screen">Where to draw.</param>
    /// <param name="rightWidth">The cells the tab list and its neighbours took.</param>
    /// <returns>The extra cells claimed, which is one only when there was nothing to sit in.</returns>
    /// <remarks>
    /// Ranger puts it at <c>wid - right_sumsize</c> (<c>titlebar.py:44-46</c>) — the first cell of
    /// the right-hand group, which is always the leading space of whatever is there. So it
    /// normally costs nothing. With nothing on the right at all it takes the last column instead,
    /// which ranger never has to do because its bar always carries a space and a key buffer.
    /// </remarks>
    private int DrawThrobber(ScreenBuffer screen, int rightWidth)
    {
        if (Throbber is not { Length: > 0 } mark || Bounds.Width <= 2)
        {
            return 0;
        }

        CellStyle style = colorScheme.Resolve(StyleContext.Of(ContextKey.InTitlebar));

        if (rightWidth > 0)
        {
            screen.Write(Bounds.Right - rightWidth, Bounds.Y, mark[..1], style);
            return 0;
        }

        screen.Write(Bounds.Right - 1, Bounds.Y, mark[..1], style);
        return 1;
    }

    /// <summary>Abbreviates the home directory when the setting asks for it.</summary>
    private string DisplayPath(string path)
    {
        if (!TildeInTitlebar)
        {
            return path;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal)
            ? "~" + path[home.Length..]
            : path;
    }

    /// <summary>How one tab is labelled in the list on the right.</summary>
    /// <param name="tab">The tab to label.</param>
    /// <returns>Its number, and its directory's name when <c>dirname_in_tabs</c> is on.</returns>
    /// <remarks>
    /// Ranger's rule (<c>gui/widgets/titlebar.py:151-161</c>): a leading space and the number,
    /// then <c>:name</c> — where the root directory, whose basename is empty, is written
    /// <c>:/</c>, and a name longer than fifteen is cut to fourteen plus an ellipsis. The leading
    /// space is what separates one tab from the next, so there is no trailing one.
    ///
    /// The cut is by display width rather than by character count, which is where this parts
    /// company with ranger: it is the terminal's cells that run out, and a name of CJK characters
    /// is twice as wide as Python's <c>len</c> believes. For a name of ASCII the two agree.
    /// </remarks>
    private string TabText(TabHeading tab)
    {
        string text = " " + tab.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // A label is a name the configuration chose for this tab, so it displaces the directory's
        // own name — but not the number. Ranger drops the number here only because it has no
        // separate notion of a label: `fm.tab_new(narg='Books')` keys the tab *dictionary* by the
        // string (`core/actions.py:1357-1363`), so `str(tabname)` is all there is left to draw.
        // Canger numbers tabs properly, and the number is what you type to switch to one, so
        // losing it to a label would cost the reader the only part they act on.
        if (tab.Label is { Length: > 0 } label)
        {
            return $"{text}:{label}";
        }

        if (!DirnameInTabs)
        {
            return text;
        }

        string dirname = Path.GetFileName(tab.Path?.TrimEnd('/') ?? string.Empty);

        // Only the root directory has no name of its own, so it is written as itself.
        return dirname.Length == 0
            ? $"{text}:/"
            : $"{text}:{new WideString(dirname).Truncate(TabNameWidth, Ellipsis)}";
    }
}

/// <summary>One entry in the title bar's tab list.</summary>
/// <param name="Number">The tab's number, which is also what is typed to switch to it.</param>
/// <param name="Path">The directory the tab is showing; its name is drawn beside the number.</param>
/// <param name="Label">
/// What to call the tab instead of its number, when the configuration named it — as
/// <c>map bk eval fm.tab_new(narg='Books', path="~/Books")</c> does.
/// </param>
public readonly record struct TabHeading(int Number, string Path, string? Label = null);
