// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.Settings;

namespace Canger.Core.Tests.Settings;

/// <summary>
/// Settings scoped to a directory, and whether anything ever asks for them.
/// </summary>
/// <remarks>
/// <c>setinregex</c>, <c>setinpath</c> and <c>setintag</c> were parsed, stored and reported
/// without error — and never consulted, because the typed facade every reader goes through asked
/// for the global value and nothing else. A rule saying "sort this one directory by date" did
/// nothing at all.
///
/// Ranger falls back to <c>fm.thisdir.path</c> inside the lookup when no path is passed
/// (<c>container/settings.py:222-235</c>), which is what <see cref="CangerSettings.CurrentPath"/>
/// reproduces.
/// </remarks>
public class PathScopedSettingsTests
{
    private static (CangerSettings Settings, SettingsStore Store) Build(params string[] lines)
    {
        SettingsStore store = new();
        ConfigurationReader reader = new();
        reader.Register(new SetDirective(store));
        reader.ReadLines(lines, "test");

        Assert.Empty(reader.Errors);

        return (new CangerSettings(store), store);
    }

    [Fact]
    public void ARuleAppliesInsideTheDirectoryItNames()
    {
        (CangerSettings settings, _) = Build(
            "set sort natural",
            "setinregex re='/home/me/videos$' sort mtime");

        settings.CurrentPath = () => "/home/me/videos";

        Assert.Equal("mtime", settings.Sort);
    }

    [Fact]
    public void ARuleDoesNotApplyAnywhereElse()
    {
        (CangerSettings settings, _) = Build(
            "set sort natural",
            "setinregex re='/home/me/videos$' sort mtime");

        settings.CurrentPath = () => "/home/me/documents";

        Assert.Equal("natural", settings.Sort);
    }

    [Fact]
    public void WithNoCurrentPathTheGlobalValueIsUsed()
    {
        // What every read used to do, and why none of this worked.
        (CangerSettings settings, _) = Build(
            "set sort natural",
            "setinregex re='/home/me/videos$' sort mtime");

        Assert.Equal("natural", settings.Sort);
    }

    [Fact]
    public void ATildeInTheRuleMeansTheHomeDirectory()
    {
        // Which is how a real configuration writes them, and the form that was reported.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        (CangerSettings settings, _) = Build(
            "set sort natural",
            "setinregex re='~/videos$' sort mtime");

        settings.CurrentPath = () => Path.Join(home, "videos");

        Assert.Equal("mtime", settings.Sort);
    }

    [Fact]
    public void BooleanSettingsAreScopedToo()
    {
        // `sort_reverse` is the other half of the reported rule, and it is a bool — which goes
        // through a different accessor from the string one above.
        (CangerSettings settings, _) = Build(
            "set sort_reverse false",
            "setinregex re='/home/me/videos$' sort_reverse true");

        settings.CurrentPath = () => "/home/me/videos";
        Assert.True(settings.SortReverse);

        settings.CurrentPath = () => "/home/me/elsewhere";
        Assert.False(settings.SortReverse);
    }

    [Fact]
    public void TheLastMatchingRuleWins()
    {
        (CangerSettings settings, _) = Build(
            "setinregex re='videos$' sort mtime",
            "setinregex re='/home/me/videos$' sort size");

        settings.CurrentPath = () => "/home/me/videos";

        Assert.Equal("size", settings.Sort);
    }

    [Fact]
    public void SetInPathAnchorsAtTheEndSoItMatchesAnywhereInTheTree()
    {
        // `setinpath` escapes its operand and anchors it, so `path=build` is any build directory
        // rather than a pattern (`config/commands.py:558-586`).
        (CangerSettings settings, _) = Build(
            "set sort natural",
            "setinpath path=build sort size");

        settings.CurrentPath = () => "/anywhere/at/all/build";

        Assert.Equal("size", settings.Sort);
    }

    [Fact]
    public void ThePathIsReadAtEachLookupRatherThanOnce()
    {
        // Settings are read between frames as well as during them, so a value captured once
        // would be stale exactly when the user has just moved.
        (CangerSettings settings, _) = Build(
            "set sort natural",
            "setinregex re='/home/me/videos$' sort mtime");

        string where = "/home/me/documents";
        settings.CurrentPath = () => where;

        Assert.Equal("natural", settings.Sort);

        where = "/home/me/videos";
        Assert.Equal("mtime", settings.Sort);
    }
}
