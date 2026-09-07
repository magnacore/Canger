// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.Settings;

namespace Canger.Core.Tests.Settings;

/// <summary>
/// A scoped assignment typed at the console rather than read from a file.
/// </summary>
/// <remarks>
/// <para>
/// The console used to answer "setlocal is only available in the configuration file so far", which
/// left a directory held at a sort by <c>setinregex</c> with no way to be re-sorted at all: a
/// path-scoped setting outranks the global one that <c>on</c> and <c>om</c> write.
/// </para>
/// <para>
/// The distinction under test is the fallback. A line that names no scope means the directory the
/// user is in — and only when there is one, which is why reading a configuration file still
/// refuses rather than guessing.
/// </para>
/// </remarks>
public class RuntimeScopedSettingsTests
{
    private const string Here = "/home/me/videos";

    private static (CangerSettings Settings, SettingsStore Store) Store()
    {
        SettingsStore store = new();
        store.SetFromText("sort", "natural");

        return (new CangerSettings(store), store);
    }

    /// <summary>A directive as the console builds it, knowing where the user is.</summary>
    private static SetDirective AtTheConsole(SettingsStore store, string? here = Here) =>
        new(store, () => here);

    [Theory]
    [InlineData("setlocal", "sort mtime")]
    [InlineData("setlocal", "sort=mtime")]
    [InlineData("setinpath", "sort mtime")]
    [InlineData("setinregex", "sort mtime")]
    public void ALineWithNoScopeAppliesToTheDirectoryTheUserIsIn(string directive, string line)
    {
        (CangerSettings settings, SettingsStore store) = Store();

        AtTheConsole(store).Execute(directive, line);

        Assert.Equal("mtime", store.Get<string>("sort", Here));
        Assert.Equal("natural", store.Get<string>("sort", "/home/me/other"));
        Assert.Equal("natural", store.Get<string>("sort", null));
    }

    [Fact]
    public void AnExplicitPathStillWinsOverWhereTheUserIs()
    {
        (_, SettingsStore store) = Store();

        AtTheConsole(store).Execute("setlocal", "path=/home/me/other sort mtime");

        Assert.Equal("mtime", store.Get<string>("sort", "/home/me/other"));
        Assert.Equal("natural", store.Get<string>("sort", Here));
    }

    [Fact]
    public void AnExplicitExpressionStillWinsOverWhereTheUserIs()
    {
        (_, SettingsStore store) = Store();

        AtTheConsole(store).Execute("setinregex", "re='/home/me/o.*r$' sort mtime");

        Assert.Equal("mtime", store.Get<string>("sort", "/home/me/other"));
        Assert.Equal("natural", store.Get<string>("sort", Here));
    }

    [Fact]
    public void TheImplicitScopeIsTheDirectoryAndNotAnythingContainingItsName()
    {
        // Anchored, so a rule for /home/me/videos does not also govern /home/me/videos-old. Ranger
        // uses the path as a raw expression for the regex spelling, which would match both.
        (_, SettingsStore store) = Store();

        AtTheConsole(store).Execute("setinregex", "sort mtime");

        Assert.Equal("mtime", store.Get<string>("sort", Here));
        Assert.Equal("natural", store.Get<string>("sort", Here + "-old"));
        Assert.Equal("natural", store.Get<string>("sort", Here + "/inner"));
    }

    [Fact]
    public void ADirectoryNameHoldingRegexPunctuationIsStillMatchedLiterally()
    {
        // `C#` is one of the reporter's own folders, and `+` and `(` are ordinary in a filename.
        const string Awkward = "/home/me/STUDY PASSIVE/C# (notes)+drafts";
        (_, SettingsStore store) = Store();

        AtTheConsole(store, Awkward).Execute("setlocal", "sort mtime");

        Assert.Equal("mtime", store.Get<string>("sort", Awkward));
    }

    [Fact]
    public void ATrailingBangTogglesWithinTheScope()
    {
        SettingsStore store = new();
        store.SetFromText("show_hidden", "false");

        AtTheConsole(store).Execute("setlocal", "show_hidden!");

        Assert.True(store.Get<bool>("show_hidden", Here));
        Assert.False(store.Get<bool>("show_hidden", "/home/me/other"));
    }

    [Fact]
    public void ReadingAConfigurationFileStillRefusesALineWithNoScope()
    {
        // There is no directory to be in while the file is read, so guessing would be worse than
        // saying so. Ranger's fallback is `fm.thisdir.path`, which is None at that point.
        SettingsStore store = new();
        SetDirective fromFile = new(store);

        Assert.Throws<SettingValueException>(() => fromFile.Execute("setlocal", "sort mtime"));
    }

    [Fact]
    public void AnUnknownScopeWordIsStillRejectedWhenAnExplicitOneWasMeant()
    {
        // `paths=` is a typo for `path=`, not a setting called `paths`. With nowhere to fall back
        // to, it has to be reported rather than silently applied somewhere.
        SettingsStore store = new();
        SetDirective fromFile = new(store);

        SettingValueException error =
            Assert.Throws<SettingValueException>(
                () => fromFile.Execute("setinregex", "paths=/x sort mtime"));

        Assert.Contains("paths=", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASettingWrittenAtTheConsoleOutranksTheGlobalOneChangedLater()
    {
        // The whole point of the report: `on` writes the global sort, and a directory with a rule
        // of its own keeps that rule.
        (_, SettingsStore store) = Store();

        AtTheConsole(store).Execute("setlocal", "sort mtime");
        store.SetFromText("sort", "basename");

        Assert.Equal("mtime", store.Get<string>("sort", Here));
        Assert.Equal("basename", store.Get<string>("sort", "/home/me/other"));
    }
}
