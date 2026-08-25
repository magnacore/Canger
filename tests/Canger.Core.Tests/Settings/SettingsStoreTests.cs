// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Settings;
using Canger.Core.Signals;

namespace Canger.Core.Tests.Settings;

public class SettingsStoreTests
{
    [Fact]
    public void NewStore_StartsAtTheCatalogDefaults()
    {
        SettingsStore settings = new();

        Assert.Equal("miller", settings.Get("viewmode"));
        Assert.Equal(8, settings.Get("scroll_offset"));
        Assert.Equal(true, settings.Get("sort_directories_first"));
        Assert.Equal(0.02, settings.Get("w3m_delay"));
        Assert.Equal([1, 3, 4], (IReadOnlyList<int>)settings.Get("column_ratios")!);
    }

    [Fact]
    public void Get_RejectsUnknownSettings() =>
        Assert.Throws<SettingValueException>(() => new SettingsStore().Get("no_such_setting"));

    [Fact]
    public void Set_RejectsAValueOfTheWrongType()
    {
        SettingsStore settings = new();

        // Ranger validates with assert, which vanishes under python -O. Canger always checks.
        Assert.Throws<SettingValueException>(() => settings.Set("scroll_offset", "banana"));
        Assert.Throws<SettingValueException>(() => settings.Set("show_hidden", 3));
    }

    [Fact]
    public void Set_RejectsAValueOutsideAnEnumeratedSet()
    {
        SettingsStore settings = new();

        SettingValueException error =
            Assert.Throws<SettingValueException>(() => settings.Set("viewmode", "cascade"));

        Assert.Contains("miller", error.Message, StringComparison.Ordinal);
        Assert.Contains("multipane", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_RejectsNullForASettingThatRequiresAValue() =>
        Assert.Throws<SettingValueException>(() => new SettingsStore().Set("viewmode", null));

    [Fact]
    public void Set_AcceptsNullForASettingThatAllowsIt()
    {
        SettingsStore settings = new();

        settings.Set("preview_script", null);

        Assert.Null(settings.Get("preview_script"));
    }

    [Fact]
    public void SetFromText_ParsesEachKind()
    {
        SettingsStore settings = new();

        settings.SetFromText("show_hidden", "true");
        settings.SetFromText("scroll_offset", "12");
        settings.SetFromText("w3m_delay", "0.25");
        settings.SetFromText("column_ratios", "2, 5, 7");
        settings.SetFromText("hidden_filter", @"^\.|\.pyc$");

        Assert.Equal(true, settings.Get("show_hidden"));
        Assert.Equal(12, settings.Get("scroll_offset"));
        Assert.Equal(0.25, settings.Get("w3m_delay"));
        Assert.Equal([2, 5, 7], (IReadOnlyList<int>)settings.Get("column_ratios")!);
        Assert.Equal(@"^\.|\.pyc$", settings.Get("hidden_filter"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("1")]
    public void SetFromText_AcceptsEveryTruthyWord(string text)
    {
        SettingsStore settings = new();
        settings.SetFromText("show_hidden", text);
        Assert.Equal(true, settings.Get("show_hidden"));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("off")]
    [InlineData("no")]
    [InlineData("0")]
    public void SetFromText_AcceptsEveryFalsyWord(string text)
    {
        SettingsStore settings = new();
        settings.Set("show_hidden", true);
        settings.SetFromText("show_hidden", text);
        Assert.Equal(false, settings.Get("show_hidden"));
    }

    [Fact]
    public void Toggle_InvertsABoolean()
    {
        SettingsStore settings = new();

        settings.Toggle("show_hidden");
        Assert.Equal(true, settings.Get("show_hidden"));

        settings.Toggle("show_hidden");
        Assert.Equal(false, settings.Get("show_hidden"));
    }

    [Fact]
    public void Toggle_CyclesAnEnumeratedSettingAndWraps()
    {
        // This is what "map ~ set viewmode!" relies on.
        SettingsStore settings = new();

        settings.Toggle("viewmode");
        Assert.Equal("multipane", settings.Get("viewmode"));

        settings.Toggle("viewmode");
        Assert.Equal("miller", settings.Get("viewmode"));
    }

    [Fact]
    public void Toggle_RejectsASettingThatIsNeitherBooleanNorEnumerated() =>
        Assert.Throws<SettingValueException>(() => new SettingsStore().Toggle("scroll_offset"));

    [Fact]
    public void Set_RunsSanitizeHandlersBeforeTheValueIsStored()
    {
        // The pipeline exists so subsystems can normalise a value on its way in. Ranger uses it
        // to clamp column_ratios and to turn a colorscheme name into an instance.
        SettingsStore settings = new();

        settings.Signals.Subscribe(
            SettingChange.SignalNameFor("scroll_offset"),
            signal =>
            {
                SettingChange change = (SettingChange)signal;
                change.Value = Math.Clamp((int)change.Value!, 0, 10);
            },
            SignalPriority.Sanitize);

        settings.Set("scroll_offset", 999);

        Assert.Equal(10, settings.Get("scroll_offset"));
    }

    [Fact]
    public void Set_PublishesBothTheGeneralAndThePerSettingSignal()
    {
        SettingsStore settings = new();
        List<string> seen = [];

        settings.Signals.Subscribe(SettingChange.SignalPrefix, _ => seen.Add("general"));
        settings.Signals.Subscribe(SettingChange.SignalNameFor("sort"), _ => seen.Add("specific"));
        settings.Signals.Subscribe(SettingChange.SignalNameFor("viewmode"), _ => seen.Add("other"));

        settings.Set("sort", "size");

        Assert.Equal(["general", "specific"], seen);
    }

    [Fact]
    public void Set_ReportsThePreviousValueToHandlers()
    {
        SettingsStore settings = new();
        object? previous = null;

        settings.Signals.Subscribe(
            SettingChange.SignalNameFor("sort"),
            signal => previous = ((SettingChange)signal).PreviousValue,
            SignalPriority.AfterSync);

        settings.Set("sort", "size");

        Assert.Equal("natural", previous);
    }

    [Fact]
    public void Set_RejectsAValueASanitizeHandlerMadeInvalid()
    {
        // A handler must not be able to smuggle a bad value past validation.
        SettingsStore settings = new();

        settings.Signals.Subscribe(
            SettingChange.SignalNameFor("viewmode"),
            signal => ((SettingChange)signal).Value = "cascade",
            SignalPriority.Sanitize);

        Assert.Throws<SettingValueException>(() => settings.Set("viewmode", "multipane"));
    }

    [Fact]
    public void Get_PrefersAPathScopedValueOverTheGlobalOne()
    {
        SettingsStore settings = new();

        settings.Set("sort", "mtime", SettingScope.ForPathPattern("^/tmp/downloads"));

        Assert.Equal("mtime", settings.Get("sort", "/tmp/downloads/inner"));
        Assert.Equal("natural", settings.Get("sort", "/home/user"));
        Assert.Equal("natural", settings.Get("sort"));
    }

    [Fact]
    public void Get_PrefersALaterPathScopeWhenTwoMatch()
    {
        SettingsStore settings = new();

        settings.Set("sort", "size", SettingScope.ForPathPattern("^/data"));
        settings.Set("sort", "mtime", SettingScope.ForPathPattern("^/data/photos"));

        Assert.Equal("mtime", settings.Get("sort", "/data/photos"));
    }

    [Fact]
    public void Get_PrefersATagScopedValueOverTheGlobalOne()
    {
        SettingsStore settings = new();

        settings.Set("sort", "size", SettingScope.ForTags(['x']));

        Assert.Equal("size", settings.Get("sort", path: null, tags: ['x']));
        Assert.Equal("natural", settings.Get("sort", path: null, tags: ['y']));
    }

    [Fact]
    public void Get_PrefersPathScopeOverTagScope()
    {
        // Resolution order is local, then tag, then global.
        SettingsStore settings = new();

        settings.Set("sort", "size", SettingScope.ForTags(['x']));
        settings.Set("sort", "mtime", SettingScope.ForPathPattern("^/data"));

        Assert.Equal("mtime", settings.Get("sort", "/data", ['x']));
    }

    [Fact]
    public void ForPath_BindsAViewToOneDirectory()
    {
        SettingsStore settings = new();
        settings.Set("sort", "mtime", SettingScope.ForPathPattern("^/data"));

        ISettings bound = settings.ForPath("/data/photos");

        Assert.Equal("mtime", bound.Get("sort"));
        Assert.Equal("natural", settings.Get("sort"));
    }

    [Fact]
    public void ForPath_WritesReachTheSharedStore()
    {
        SettingsStore settings = new();
        ISettings bound = settings.ForPath("/data");

        bound.Set("scroll_offset", 3);

        Assert.Equal(3, settings.Get("scroll_offset"));
    }

    [Fact]
    public void TypedFacade_ReadsThroughToTheStore()
    {
        SettingsStore settings = new();
        CangerSettings typed = new(settings);

        Assert.Equal("miller", typed.Viewmode);
        Assert.Equal(8, typed.ScrollOffset);
        Assert.True(typed.SortDirectoriesFirst);
        Assert.Equal([1, 3, 4], typed.ColumnRatios);
        Assert.Null(typed.PreviewScript);

        settings.Set("scroll_offset", 4);
        Assert.Equal(4, typed.ScrollOffset);
    }
}
