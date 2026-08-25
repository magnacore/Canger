// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui.Rendering;
using Canger.Ui.Styling;

namespace Canger.Ui.Tests.Styling;

/// <summary>
/// The four colourschemes and how one is chosen.
/// </summary>
public class ColorSchemeRegistryTests
{
    private static StyleContext Row(params ContextKey[] keys) => StyleContext.Of(keys);

    [Fact]
    public void Registry_HoldsTheFourSchemesRangerShips()
    {
        Assert.Equal(["default", "jungle", "snow", "solarized"], new ColorSchemeRegistry().Names);
    }

    [Fact]
    public void CreateOrDefault_FallsBackRatherThanFailingOnAMisspelledName()
    {
        // A typo in the configuration should cost the colours, not the session.
        Assert.Equal("default", new ColorSchemeRegistry().CreateOrDefault("solarised").Name);
    }

    [Fact]
    public void Create_ReturnsNothingForANameThatDoesNotExist()
    {
        Assert.Null(new ColorSchemeRegistry().Create("nonesuch"));
    }

    [Fact]
    public void Create_BuildsAFreshInstanceEachTime()
    {
        // Each scheme caches its own results, so sharing one instance would share that cache.
        ColorSchemeRegistry registry = new();

        Assert.NotSame(registry.Create("snow"), registry.Create("snow"));
    }

    [Fact]
    public void Jungle_PaintsDirectoriesGreenWhereTheDefaultPaintsThemBlue()
    {
        StyleContext directory = Row(ContextKey.InBrowser, ContextKey.Directory);

        Assert.Equal(Color.Blue.Bright(), new DefaultColorScheme().Resolve(directory).Foreground);
        Assert.Equal(Color.Green, new JungleColorScheme().Resolve(directory).Foreground);
    }

    [Fact]
    public void Jungle_LeavesAMarkedOrLinkedDirectoryToTheDefault()
    {
        // Those already have a colour that says something; overriding them would lose it.
        JungleColorScheme jungle = new();

        Assert.NotEqual(Color.Green,
                        jungle.Resolve(Row(ContextKey.InBrowser, ContextKey.Directory,
                                           ContextKey.Link, ContextKey.Good)).Foreground);
        Assert.NotEqual(Color.Green,
                        jungle.Resolve(Row(ContextKey.InBrowser, ContextKey.Directory,
                                           ContextKey.MainColumn, ContextKey.Marked)).Foreground);
    }

    [Fact]
    public void Jungle_PaintsTheHostnameRedOnlyWhenRunningAsRoot()
    {
        JungleColorScheme jungle = new();

        Assert.Equal(Color.Blue,
                     jungle.Resolve(Row(ContextKey.InTitlebar, ContextKey.Hostname)).Foreground);
        Assert.Equal(Color.Red,
                     jungle.Resolve(Row(ContextKey.InTitlebar, ContextKey.Hostname,
                                        ContextKey.Bad)).Foreground);
    }

    [Theory]
    [InlineData(ContextKey.Directory)]
    [InlineData(ContextKey.Executable)]
    [InlineData(ContextKey.Link)]
    [InlineData(ContextKey.Device)]
    public void Snow_SetsNoColourAtAll(ContextKey key)
    {
        // The whole scheme works through bold and reverse, so it survives a terminal with no
        // palette to speak of.
        CellStyle style = new SnowColorScheme().Resolve(Row(ContextKey.InBrowser, key));

        Assert.Equal(Color.Default, style.Foreground);
        Assert.Equal(Color.Default, style.Background);
    }

    [Fact]
    public void Snow_MarksDirectoriesAndTheCursorWithAttributesInstead()
    {
        SnowColorScheme snow = new();

        Assert.True(snow.Resolve(Row(ContextKey.InBrowser, ContextKey.Directory))
                        .Attributes.HasFlag(CellAttributes.Bold));
        Assert.True(snow.Resolve(Row(ContextKey.InBrowser, ContextKey.Selected))
                        .Attributes.HasFlag(CellAttributes.Reverse));
    }

    [Fact]
    public void Solarized_UsesPaletteIndicesRatherThanTheBasicColours()
    {
        SolarizedColorScheme solarized = new();

        Assert.Equal(Color.FromIndex(33),
                     solarized.Resolve(Row(ContextKey.InBrowser, ContextKey.Directory)).Foreground);
        Assert.Equal(Color.FromIndex(244),
                     solarized.Resolve(Row(ContextKey.InBrowser)).Foreground);
    }

    [Fact]
    public void Solarized_PutsAWarningOnTheBackgroundWhenTheRowIsAlreadyReversed()
    {
        // On a reversed row the foreground is what the terminal paints behind the text, so a
        // foreground warning would be invisible.
        SolarizedColorScheme solarized = new();

        CellStyle plain = solarized.Resolve(Row(ContextKey.InBrowser, ContextKey.BadInfo));
        CellStyle selected = solarized.Resolve(Row(ContextKey.InBrowser, ContextKey.BadInfo,
                                                   ContextKey.Selected));

        Assert.Equal(Color.Magenta, plain.Foreground);
        Assert.Equal(Color.Magenta, selected.Background);
    }

    [Fact]
    public void EverySchemeReturnsTheTerminalDefaultForReset()
    {
        ColorSchemeRegistry registry = new();

        foreach (string name in registry.Names)
        {
            Assert.Equal(CellStyle.Default,
                         registry.CreateOrDefault(name).Resolve(Row(ContextKey.Reset)));
        }
    }

    [Fact]
    public void SwitchableScheme_ForwardsToWhicheverSchemeIsCurrent()
    {
        SwitchableColorScheme scheme = new(new DefaultColorScheme());
        StyleContext directory = Row(ContextKey.InBrowser, ContextKey.Directory);

        Assert.Equal(Color.Blue.Bright(), scheme.Resolve(directory).Foreground);

        Assert.True(scheme.SwitchTo(new JungleColorScheme()));
        Assert.Equal(Color.Green, scheme.Resolve(directory).Foreground);
        Assert.Equal("jungle", scheme.Name);
    }

    [Fact]
    public void SwitchableScheme_ReportsNoChangeWhenAskedForTheSchemeItAlreadyHas()
    {
        // The caller repaints on a change, and repainting every frame would undo the diffing.
        SwitchableColorScheme scheme = new(new SnowColorScheme());

        Assert.False(scheme.SwitchTo(new SnowColorScheme()));
    }
}
