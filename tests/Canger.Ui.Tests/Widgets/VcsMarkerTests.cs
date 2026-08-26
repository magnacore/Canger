// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;
using Canger.Vcs;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// The one-character version-control mark beside a name, and the colour it is drawn in.
/// </summary>
/// <remarks>
/// Pinned character by character against ranger's table (<c>gui/widgets/__init__.py:11-30</c>).
/// Three of these once differed and two of the three collided outright — <c>!</c> meant
/// <em>ignored</em> in Canger and <em>unknown</em> in ranger, so the same mark told someone
/// arriving from ranger the opposite of what it meant.
/// </remarks>
public class VcsMarkerTests
{
    [Theory]
    [InlineData(VcsStatus.Conflict, "X")]
    [InlineData(VcsStatus.Untracked, "?")]
    [InlineData(VcsStatus.Deleted, "-")]
    [InlineData(VcsStatus.Changed, "+")]
    [InlineData(VcsStatus.Staged, "*")]
    [InlineData(VcsStatus.Ignored, "·")]
    [InlineData(VcsStatus.Unknown, "!")]
    public void Marker_MatchesRangersCharacter(VcsStatus status, string expected)
    {
        Assert.Equal(expected, BrowserColumn.MarkerFor(status)?.Text);
    }

    [Fact]
    public void Marker_TicksAFileInSync()
    {
        // Busy in a source tree, and kept anyway: someone arriving from ranger who scans for the
        // tick and does not find it has to work out whether the file is clean or the file manager
        // is broken, which costs more than the noise.
        Assert.Equal("✓", BrowserColumn.MarkerFor(VcsStatus.Sync)?.Text);
        Assert.Equal(Color.Green, ColourOf(VcsStatus.Sync));
    }

    [Fact]
    public void Marker_ShowsNothingWhenThereIsNoStatusAtAll()
    {
        Assert.Null(BrowserColumn.MarkerFor(VcsStatus.None));
    }

    [Theory]
    [InlineData(VcsRemoteStatus.Diverged, "Y")]
    [InlineData(VcsRemoteStatus.Ahead, ">")]
    [InlineData(VcsRemoteStatus.Behind, "<")]
    [InlineData(VcsRemoteStatus.Sync, "=")]
    [InlineData(VcsRemoteStatus.None, "⌂")]
    public void RemoteMarker_MatchesRangersCharacter(VcsRemoteStatus status, string expected)
    {
        // Drawn on a directory that is a repository, so a listing of projects says at a glance
        // which of them have commits that are not pushed.
        Assert.Equal(expected, BrowserColumn.RemoteMarkerFor(status)?.Text);
    }

    /// <summary>The colour a remote marker resolves to in the default scheme.</summary>
    private static Color RemoteColourOf(VcsRemoteStatus status)
    {
        (string Text, ContextKey Context) marker = BrowserColumn.RemoteMarkerFor(status)!.Value;

        return new DefaultColorScheme().Resolve(
            StyleContext.Of(ContextKey.InBrowser, ContextKey.VcsRemote, marker.Context))
            .Foreground;
    }

    [Fact]
    public void RemoteMarker_IsColouredOnItsOwnScale()
    {
        // Not the file scale: green is "nothing to do", red is "the remote is ahead of you", blue
        // is "you have something to push" (colorschemes/default.py:173-184).
        Assert.Equal(Color.Green, RemoteColourOf(VcsRemoteStatus.Sync));
        Assert.Equal(Color.Green, RemoteColourOf(VcsRemoteStatus.None));
        Assert.Equal(Color.Red, RemoteColourOf(VcsRemoteStatus.Behind));
        Assert.Equal(Color.Blue, RemoteColourOf(VcsRemoteStatus.Ahead));
        Assert.Equal(Color.Magenta, RemoteColourOf(VcsRemoteStatus.Diverged));
    }

    [Theory]
    [InlineData(VcsStatus.Conflict, ContextKey.VcsConflict)]
    [InlineData(VcsStatus.Untracked, ContextKey.VcsUntracked)]
    [InlineData(VcsStatus.Deleted, ContextKey.VcsChanged)]
    [InlineData(VcsStatus.Changed, ContextKey.VcsChanged)]
    [InlineData(VcsStatus.Staged, ContextKey.VcsStaged)]
    [InlineData(VcsStatus.Ignored, ContextKey.VcsIgnored)]
    [InlineData(VcsStatus.Unknown, ContextKey.VcsUnknown)]
    public void Marker_CarriesTheColourContextRangerTagsItWith(VcsStatus status, ContextKey key)
    {
        // Staged is the one worth naming: it used to borrow `changed`, so a staged file was red
        // where ranger shows it green — the difference between "you have work to commit" and
        // "you have work to lose".
        Assert.Equal(key, BrowserColumn.MarkerFor(status)?.Context);
    }

    /// <summary>The colour a marker resolves to in the default scheme.</summary>
    private static Color ColourOf(VcsStatus status)
    {
        (string Text, ContextKey Context) marker = BrowserColumn.MarkerFor(status)!.Value;

        return new DefaultColorScheme().Resolve(
            StyleContext.Of(ContextKey.InBrowser, ContextKey.VcsFile, marker.Context)).Foreground;
    }

    [Fact]
    public void Marker_IsColouredAsRangerColoursIt()
    {
        // None of these keys was read by any scheme, so every mark drew in whatever colour the
        // row already had — which is half the reason the glyphs were carrying the whole signal.
        Assert.Equal(Color.Magenta, ColourOf(VcsStatus.Conflict));
        Assert.Equal(Color.Cyan, ColourOf(VcsStatus.Untracked));
        Assert.Equal(Color.Red, ColourOf(VcsStatus.Changed));
        Assert.Equal(Color.Red, ColourOf(VcsStatus.Deleted));
        Assert.Equal(Color.Red, ColourOf(VcsStatus.Unknown));
        Assert.Equal(Color.Green, ColourOf(VcsStatus.Staged));
    }

    [Fact]
    public void Marker_LeavesTheIgnoredMarkInTheDefaultColour()
    {
        // Ranger sets `fg = default` for ignored on purpose. The quiet mark stays quiet.
        Assert.Equal(Color.Default, ColourOf(VcsStatus.Ignored));
    }
}
