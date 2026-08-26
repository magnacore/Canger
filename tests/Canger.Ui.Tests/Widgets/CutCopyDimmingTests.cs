// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// What <c>dd</c> and <c>yy</c> look like: the files waiting on the clipboard are dimmed until
/// they are pasted or the clipboard is cleared.
/// </summary>
/// <remarks>
/// Both stock schemes have had the rule since the start — bold on bright black, ranger's
/// <c>colorschemes/default.py:70-77</c> — but nothing ever produced the <c>cut</c> or
/// <c>copied</c> keys, so cutting a file changed nothing on screen. Rendering is the part worth
/// pinning down.
/// </remarks>
public class CutCopyDimmingTests
{
    private const int Width = 40;

    private static (BrowserColumn Column, ScreenBuffer Screen, DirectoryNode Directory,
                    InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/alpha.txt")
            .AddFile("/home/beta.txt")
            .AddFile("/home/gamma.txt");

        DirectoryNode directory = new(fs, "/home", fs.GetStatus("/home", followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        BrowserColumn column = new(new DefaultColorScheme())
        {
            Directory = directory,
            IsMainColumn = true,
            ShowSize = false,
        };
        column.Layout(new Rect(0, 0, Width, 10));

        return (column, new ScreenBuffer(Width, 10), directory, fs);
    }

    /// <summary>Whether a row is drawn in the dimmed clipboard colour.</summary>
    private static bool IsDimmed(ScreenBuffer screen, int row)
    {
        for (int x = 0; x < Width; x++)
        {
            if (screen[x, row].Rune.Value != ' ')
            {
                CellStyle style = screen[x, row].Style;
                return style.Foreground == Color.Black.Bright()
                       && style.Attributes.HasFlag(CellAttributes.Bold);
            }
        }

        return false;
    }

    [Fact]
    public void Draw_DimsAFileWaitingToBeCopied()
    {
        (BrowserColumn column, ScreenBuffer screen, _, _) = Build();

        column.CopyBuffer = new HashSet<string>(StringComparer.Ordinal) { "/home/beta.txt" };
        column.CopyBufferIsCut = false;
        column.Render(screen);

        Assert.True(IsDimmed(screen, 1), "beta.txt is on the clipboard");
        Assert.False(IsDimmed(screen, 2), "gamma.txt is not");
    }

    [Fact]
    public void Draw_DimsAFileWaitingToBeMoved()
    {
        // `cut` and `copied` are distinct keys, and the stock schemes dim both the same way.
        (BrowserColumn column, ScreenBuffer screen, _, _) = Build();

        column.CopyBuffer = new HashSet<string>(StringComparer.Ordinal) { "/home/beta.txt" };
        column.CopyBufferIsCut = true;
        column.Render(screen);

        Assert.True(IsDimmed(screen, 1));
    }

    [Fact]
    public void Draw_LeavesTheRowUnderTheCursorAlone()
    {
        // Ranger's `if not context.selected and (context.cut or context.copied)`. The cursor row
        // is drawn in reverse video, and dimming it too would make it unreadable.
        (BrowserColumn column, ScreenBuffer screen, _, _) = Build();

        column.CopyBuffer = new HashSet<string>(StringComparer.Ordinal) { "/home/alpha.txt" };
        column.Render(screen);

        Assert.False(IsDimmed(screen, 0), "the cursor sits on alpha.txt");
    }

    [Fact]
    public void Draw_KeepsTheDimmingAfterTheListingIsRebuilt()
    {
        // The reported requirement: leave the directory and come back and the marking is still
        // there. Coming back reloads, which builds new entries, so matching has to be by path.
        (BrowserColumn column, ScreenBuffer screen, DirectoryNode directory,
         InMemoryFileSystem fs) = Build();

        column.CopyBuffer = new HashSet<string>(StringComparer.Ordinal) { "/home/beta.txt" };
        column.Render(screen);
        Assert.True(IsDimmed(screen, 1));

        fs.AddFile("/home/delta.txt");
        directory.Load(TestContext.Current.CancellationToken);
        column.Render(screen);

        // alpha, beta, delta, gamma — beta is still where it was, and still dimmed.
        Assert.True(IsDimmed(screen, 1), "the clipboard outlives the listing it was filled from");
    }

    [Fact]
    public void Draw_RestoresTheColourWhenTheClipboardIsCleared()
    {
        // What `uy` leaves behind: `SetCopyBuffer([], cut: false)`, and the listing looks normal.
        (BrowserColumn column, ScreenBuffer screen, _, _) = Build();

        column.CopyBuffer = new HashSet<string>(StringComparer.Ordinal) { "/home/beta.txt" };
        column.Render(screen);
        Assert.True(IsDimmed(screen, 1));

        column.CopyBuffer = new HashSet<string>(StringComparer.Ordinal);
        column.Render(screen);

        Assert.False(IsDimmed(screen, 1));
    }
}
