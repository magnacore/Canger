// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// What the bar does with an activity line too long for the room between its blocks.
/// </summary>
/// <remarks>
/// Reported as the clock disappearing on pause: holding playback adds <c>(Paused)</c>, the line
/// outgrows the space, and the bar dropped all of it — which is indistinguishable from nothing
/// playing at all.
/// </remarks>
public class ActivityLineFitTests
{
    private const string Line = "1/2  00:02:40 / 00:10:00 (60%) 1.5x (Paused)  vol 98%";

    [Fact]
    public void ALineThatFitsIsLeftAlone()
    {
        Assert.Equal(Line, StatusBar.Fit(Line, Line.Length));
        Assert.Equal(Line, StatusBar.Fit(Line, Line.Length + 20));
    }

    [Fact]
    public void ALineThatDoesNotFitKeepsBothOfItsEnds()
    {
        // The head is the clock, which is what the eye goes to; the tail is the state that has
        // just changed, which is why the line grew in the first place.
        string shown = StatusBar.Fit(Line, 34);

        Assert.StartsWith("1/2  00:02:40", shown, StringComparison.Ordinal);
        Assert.EndsWith("vol 98%", shown, StringComparison.Ordinal);
        Assert.Contains("~", shown, StringComparison.Ordinal);
        Assert.True(shown.Length <= 34, $"'{shown}' is {shown.Length} columns, room was 34");
    }

    [Theory]
    [InlineData(48)]
    [InlineData(40)]
    [InlineData(34)]
    [InlineData(28)]
    [InlineData(22)]
    [InlineData(18)]
    public void NoWordIsLeftAsAFragment(int room)
    {
        // The point of taking whole words. Cutting at whatever column the arithmetic lands on
        // leaves "sed)" and "00:1", which read as different words rather than shortened ones —
        // both were on screen while this was built by column instead.
        string shown = StatusBar.Fit(Line, room);

        foreach (string word in shown.Replace("~", " ", StringComparison.Ordinal)
                                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Contains($" {word} ", $" {Line} ", StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AWordWiderThanTheWholeSpaceIsCutRatherThanDropped()
    {
        // Nothing can be kept whole, and saying nothing would be worse: a single long name still
        // shows its beginning and its end.
        const string OneWord = "averyverylongsinglewordwithnospacesatallinit";

        string shown = StatusBar.Fit(OneWord, 24);

        Assert.StartsWith("averyvery", shown, StringComparison.Ordinal);
        Assert.EndsWith("allinit", shown, StringComparison.Ordinal);
        Assert.True(shown.Length <= 24, $"'{shown}' is {shown.Length} columns");
    }

    [Fact]
    public void TheEndIsNeverWhatIsThrownAway()
    {
        // Plain truncation would cut exactly the word that made the line too long — press pause
        // and watch the line get shorter without ever saying "Paused".
        Assert.EndsWith("vol 98%", StatusBar.Fit(Line, 30), StringComparison.Ordinal);
        Assert.EndsWith("vol 98%", StatusBar.Fit(Line, 24), StringComparison.Ordinal);
    }

    [Fact]
    public void ARoomTooSmallForASentenceShowsNothing()
    {
        // The old behaviour, kept where it is right: a clock cut to a few columns says less than
        // the space it occupies.
        Assert.Equal(string.Empty, StatusBar.Fit(Line, 12));
        Assert.Equal(string.Empty, StatusBar.Fit(Line, 0));
    }

    [Fact]
    public void WidthIsCountedInCellsNotCharacters()
    {
        // Six characters, twelve cells. Counting characters would leave the line wider than the
        // room and paint over the block beside it.
        const string Wide = "一二三四五六 (Paused)";

        string shown = StatusBar.Fit(Wide, 16);

        Assert.True(new Canger.Tui.Text.WideString(shown).Width <= 16,
                    $"'{shown}' is {new Canger.Tui.Text.WideString(shown).Width} cells wide");
    }

    [Fact]
    public void TheBarDrawsTheLineRatherThanDroppingIt()
    {
        // The rule is only worth anything if the bar applies it: this is the reported case, a
        // line that does not fit, and the clock has to survive it.
        InMemoryFileSystem files = new();
        files.AddFileOfSize("/home/a-file-with-a-considerable-name.mkv", 8, DateTimeOffset.UnixEpoch);

        DirectoryCache cache = new(files);
        Tab tab = new(cache, "/home", 20);
        tab.Current.Load(TestContext.Current.CancellationToken);

        StatusBar bar = new(new DefaultColorScheme()) { Tab = tab, FreeBytes = 1_000_000_000 };
        bar.Layout(new Rect(0, 0, 100, 1));
        bar.ActivityDescription = Line;

        ScreenBuffer screen = new(100, 1);
        bar.Render(screen);

        Assert.Contains("00:02:40", screen.TextAt(0), StringComparison.Ordinal);
        Assert.Contains("vol 98%", screen.TextAt(0), StringComparison.Ordinal);
    }
}
