// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.Settings;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Styling;

/// <summary>
/// Reading a colour out of the configuration.
/// </summary>
public class ColorNamesTests
{
    [Theory]
    [InlineData("blue", 4)]
    [InlineData("BLUE", 4)]
    [InlineData(" cyan ", 6)]
    [InlineData("bright_blue", 12)]
    [InlineData("bright-blue", 12)]
    [InlineData("0", 0)]
    [InlineData("231", 231)]
    public void ReadsTheColoursAConfigurationCanName(string text, int index)
    {
        Assert.True(ColorNames.TryParse(text, out Color color));
        Assert.Equal(Color.FromIndex(index), color);
    }

    [Fact]
    public void ReadsTheTerminalsOwnColour()
    {
        Assert.True(ColorNames.TryParse("default", out Color color));
        Assert.True(color.IsDefault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("chartreuse")]
    [InlineData("256")]
    [InlineData("-1")]
    public void RefusesWhatNamesNoColour(string? text)
    {
        // A blank and a typo answer the same way on purpose: both leave the colourscheme's own
        // choice standing, which is better than a bar drawn in a colour nobody asked for.
        Assert.False(ColorNames.TryParse(text, out _));
    }
}

/// <summary>
/// The colours a progress bar is drawn in, and the configuration that can override them.
/// </summary>
public class ProgressBarColorTests
{
    private static CellStyle Bar(IColorScheme scheme) =>
        scheme.Resolve(StyleContext.Of(ContextKey.InStatusbar, ContextKey.Loaded));

    [Fact]
    public void EveryShippedSchemeContrastsItsTextWithItsFill()
    {
        // The whole point of naming both halves. A scheme that left the text at the terminal's
        // default would be back where this started: legible or not depending on how the terminal
        // renders the fill, which is not something a scheme can know.
        foreach (ColorScheme scheme in
                 new ColorScheme[] { new DefaultColorScheme(), new JungleColorScheme() })
        {
            Assert.NotEqual(scheme.ProgressBarColor, scheme.ProgressBarTextColor);
            Assert.False(scheme.ProgressBarTextColor.IsDefault, scheme.Name);
            Assert.False(scheme.ProgressBarColor.IsDefault, scheme.Name);
        }
    }

    [Fact]
    public void DrawsTheBarInTheConfiguredColours()
    {
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Assert.True(scheme.SetProgressBarColors(Color.Magenta, Color.White.Bright()));

        Assert.Equal(Color.Magenta, Bar(scheme).Background);
        Assert.Equal(Color.White.Bright(), Bar(scheme).Foreground);
    }

    [Fact]
    public void ForgetsTheOverrideWhenItIsCleared()
    {
        SwitchableColorScheme scheme = new(new DefaultColorScheme());
        scheme.SetProgressBarColors(Color.Magenta, Color.White.Bright());

        Assert.True(scheme.SetProgressBarColors(null, null));
        Assert.Equal(new DefaultColorScheme().ProgressBarColor, Bar(scheme).Background);
    }

    [Fact]
    public void KeepsTheConfiguredColoursAcrossAChangeOfScheme()
    {
        // A scheme switched to later is a fresh object that has never been told. Without the
        // overrides being remembered, the colours would hold until the first `:set colorscheme`
        // and then revert with nothing to explain it.
        SwitchableColorScheme scheme = new(new DefaultColorScheme());
        scheme.SetProgressBarColors(Color.Magenta, Color.White.Bright());

        Assert.True(scheme.SwitchTo(new JungleColorScheme()));

        Assert.Equal(Color.Magenta, Bar(scheme).Background);
        Assert.Equal(Color.White.Bright(), Bar(scheme).Foreground);
    }

    [Fact]
    public void DiscardsStylesWorkedOutUnderTheOldColours()
    {
        // Every resolved style is memoised, so without discarding them the setting appears to do
        // nothing until something else happens to force a repaint.
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Color before = Bar(scheme).Background;
        scheme.SetProgressBarColors(Color.Magenta, Color.Black);

        Assert.NotEqual(before, Bar(scheme).Background);
    }

    [Fact]
    public void SaysWhenNothingChanged()
    {
        // The caller repaints the whole screen on a change, which is not free, so setting the
        // same colours again must not report one.
        SwitchableColorScheme scheme = new(new DefaultColorScheme());
        scheme.SetProgressBarColors(Color.Magenta, Color.Black);

        Assert.False(scheme.SetProgressBarColors(Color.Magenta, Color.Black));
    }

    [Fact]
    public void LeavesJunglesDirectoriesAloneWhenTheBarIsRecoloured()
    {
        // Jungle painted its directories, its line numbers and its progress bar from one property,
        // so configuring the bar would have repainted every directory name in the browser.
        SwitchableColorScheme scheme = new(new JungleColorScheme());
        StyleContext directory = StyleContext.Of(ContextKey.InBrowser, ContextKey.Directory);

        Color before = scheme.Resolve(directory).Foreground;
        scheme.SetProgressBarColors(Color.Magenta, Color.Black);

        Assert.Equal(before, scheme.Resolve(directory).Foreground);
        Assert.Equal(Color.Magenta, Bar(scheme).Background);
    }
}


/// <summary>
/// That the configuration actually reaches the colourscheme.
/// </summary>
/// <remarks>
/// Written because a control found nothing pinning it: with the whole step disconnected — the
/// scheme told <c>null, null</c> however the settings were written — none of 2 045 tests failed,
/// and the two settings would have been inert while everything looked well. The rule is reached
/// through <see cref="Browser.ApplyProgressBarColors"/>, which reads the settings itself, so
/// there is no seam left for a caller to get wrong.
/// </remarks>
public class ProgressBarSettingsTests
{
    private static CangerSettings Configured(params string[] lines)
    {
        SettingsStore store = new();
        ConfigurationReader reader = new();
        reader.Register(new SetDirective(store));
        reader.ReadLines(lines, "test");

        Assert.Empty(reader.Errors);

        return new CangerSettings(store);
    }

    private static CellStyle Bar(IColorScheme scheme) =>
        scheme.Resolve(StyleContext.Of(ContextKey.InStatusbar, ContextKey.Loaded));

    [Fact]
    public void TakesBothColoursFromTheConfiguration()
    {
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Assert.True(Browser.ApplyProgressBarColors(
            scheme,
            Configured("set progress_bar_color magenta",
                       "set progress_bar_text_color bright_white")));

        Assert.Equal(Color.Magenta, Bar(scheme).Background);
        Assert.Equal(Color.White.Bright(), Bar(scheme).Foreground);
    }

    [Fact]
    public void TakesEachColourFromItsOwnSetting()
    {
        // Reading one setting for both, or swapping them, is exactly the mistake this shape is
        // here to catch — and it looks entirely correct in the source.
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Browser.ApplyProgressBarColors(scheme, Configured("set progress_bar_color red"));

        Assert.Equal(Color.Red, Bar(scheme).Background);
        Assert.Equal(new DefaultColorScheme().ProgressBarTextColor, Bar(scheme).Foreground);
    }

    [Fact]
    public void LeavesTheSchemeAloneWhenNothingIsConfigured()
    {
        DefaultColorScheme untouched = new();
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Assert.False(Browser.ApplyProgressBarColors(scheme, Configured()));

        Assert.Equal(untouched.ProgressBarColor, Bar(scheme).Background);
        Assert.Equal(untouched.ProgressBarTextColor, Bar(scheme).Foreground);
    }

    [Fact]
    public void KeepsTheSchemesChoiceWhenTheSettingNamesNothing()
    {
        // A typo leaves the bar as it was rather than drawing it in some colour nobody asked for.
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Browser.ApplyProgressBarColors(scheme, Configured("set progress_bar_color chartreuse"));

        Assert.Equal(new DefaultColorScheme().ProgressBarColor, Bar(scheme).Background);
    }

    [Fact]
    public void TakesTheTerminalsOwnColourWhenAskedForIt()
    {
        // `default` is a colour a user can choose, and is not the same as leaving it blank.
        SwitchableColorScheme scheme = new(new DefaultColorScheme());

        Browser.ApplyProgressBarColors(scheme, Configured("set progress_bar_text_color default"));

        Assert.True(Bar(scheme).Foreground.IsDefault);
    }
}
