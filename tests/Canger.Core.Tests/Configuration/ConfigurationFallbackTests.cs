// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.Settings;

namespace Canger.Core.Tests.Configuration;

/// <summary>
/// Lines a configuration file may contain that are not one of the handful of directives.
/// </summary>
public class ConfigurationFallbackTests
{
    [Fact]
    public void ExecuteLine_OffersAnUnknownNameToTheFallback()
    {
        // cc.conf is not a distinct format: every line is a command. Only a few need to reach
        // subsystems the command layer knows nothing about, and those are the directives.
        ConfigurationReader reader = new();
        List<string> seen = [];

        reader.Fallback = line =>
        {
            seen.Add(line);
            return true;
        };

        Assert.True(reader.ExecuteLine("default_linemode devicons"));
        Assert.Equal(["default_linemode devicons"], seen);
        Assert.Empty(reader.Errors);
    }

    [Fact]
    public void ExecuteLine_StillReportsALineTheFallbackDeclines()
    {
        ConfigurationReader reader = new();
        reader.Fallback = _ => false;

        Assert.False(reader.ExecuteLine("nonsense here"));
        Assert.Contains(reader.Errors, e => e.Message.Contains("unknown directive",
                                                              StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteLine_PrefersADirectiveOverTheFallback()
    {
        // A directive exists precisely because the command layer cannot do the job, so it must
        // never be shadowed by a command of the same name.
        SettingsStore store = new();
        ConfigurationReader reader = new();
        reader.Register(new SetDirective(store));

        bool fellThrough = false;
        reader.Fallback = _ => fellThrough = true;

        Assert.True(reader.ExecuteLine("set scroll_offset 11"));
        Assert.False(fellThrough);
        Assert.Equal(11, store.Get<int>("scroll_offset"));
    }

    [Fact]
    public void ExecuteLine_ReportsWhatTheFallbackThrewAgainstTheLine()
    {
        ConfigurationReader reader = new();
        reader.Fallback = _ => throw new InvalidOperationException("deliberate");

        Assert.False(reader.ExecuteLine("whatever", "cc.conf", 7));

        ConfigurationError error = Assert.Single(reader.Errors);
        Assert.Equal(7, error.LineNumber);
        Assert.Contains("deliberate", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Setting names Canger answers to beyond its own.
/// </summary>
public class SettingsAliasTests
{
    [Fact]
    public void Find_AcceptsTheRangerSpellingOfARenamedSetting()
    {
        // A configuration carried over from ranger should not be rejected over one word.
        SettingDefinition? definition = SettingsCatalog.Find("nested_ranger_warning");

        Assert.NotNull(definition);
        Assert.Equal("nested_canger_warning", definition.Name);
    }

    [Fact]
    public void Set_StoresAnAliasedNameUnderItsCanonicalName()
    {
        // Otherwise the value would land somewhere nothing reads.
        SettingsStore store = new();
        store.SetFromText("nested_ranger_warning", "error");

        Assert.Equal("error", store.Get<string>("nested_canger_warning"));
    }

    [Fact]
    public void Find_StillRejectsANameThatIsNotASettingAtAll()
    {
        Assert.Null(SettingsCatalog.Find("nested_badger_warning"));
    }
}
