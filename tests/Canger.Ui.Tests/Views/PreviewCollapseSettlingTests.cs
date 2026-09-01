// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Views;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Views;

/// <summary>
/// The frame the preview column owes when its width was decided on stale information.
/// </summary>
/// <remarks>
/// <para>
/// Whether the column is worth its width cannot be known until the preview has been asked for,
/// and the preview cannot be asked for until it has been told how much room it has. So the
/// decision is always one frame behind — ranger's <c>old_collapse</c> is the same compromise.
/// </para>
/// <para>
/// Being a frame behind is invisible while frames keep coming, and very visible when they stop.
/// Leaving an empty directory collapsed the column on the frame that keystroke drew, and nothing
/// asked for another: it stayed wrong for <c>idle_delay</c> — two seconds by default — and then
/// snapped back on the idle tick.
/// </para>
/// </remarks>
public class PreviewCollapseSettlingTests
{
    private const int Width = 60;
    private const int Height = 10;

    private static (MillerView View, ScreenBuffer Screen, Tab Tab) Build(string cursorOn)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/user/a.txt")
            .AddFile("/home/user/sub/inside.txt");

        DirectoryCache cache = new(fs);
        Tab tab = new(cache, "/home/user", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        tab.MoveCursorTo(tab.Current.Entries.First(
            e => string.Equals(e.RelativePath, cursorOn, StringComparison.Ordinal)));

        return (new MillerView(new DefaultColorScheme()) { CollapsePreview = true },
                new ScreenBuffer(Width, Height), tab);
    }

    [Fact]
    public void AFrameThatDecidedOnStaleInformationAsksForAnother()
    {
        // The first frame has nothing to go on and assumes there is no preview. The cursor is on
        // a directory, so there is one, and the width it just used was wrong.
        (MillerView view, ScreenBuffer screen, Tab tab) = Build("sub");

        view.Render(screen, new Rect(0, 0, Width, Height), tab);

        Assert.True(view.NeedsAnotherFrame);
    }

    [Fact]
    public void TheFrameAfterThatIsSettled()
    {
        // And it must stop asking, or the loop would redraw for ever and never idle.
        (MillerView view, ScreenBuffer screen, Tab tab) = Build("sub");

        view.Render(screen, new Rect(0, 0, Width, Height), tab);
        view.Render(screen, new Rect(0, 0, Width, Height), tab);

        Assert.False(view.NeedsAnotherFrame);
    }

    [Fact]
    public void ASettledLayoutStaysSettledAcrossFurtherFrames()
    {
        (MillerView view, ScreenBuffer screen, Tab tab) = Build("sub");

        for (int frame = 0; frame < 5; frame++)
        {
            view.Render(screen, new Rect(0, 0, Width, Height), tab);
        }

        Assert.False(view.NeedsAnotherFrame);
    }

    [Fact]
    public void NothingIsOwedWhenCollapsingIsTurnedOff()
    {
        // With `collapse_preview` off the width never depends on the answer, so no frame can be
        // wrong about it.
        (MillerView view, ScreenBuffer screen, Tab tab) = Build("sub");
        view.CollapsePreview = false;

        view.Render(screen, new Rect(0, 0, Width, Height), tab);

        Assert.False(view.NeedsAnotherFrame);
    }
}
