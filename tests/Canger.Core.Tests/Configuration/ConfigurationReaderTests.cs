// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.Settings;

namespace Canger.Core.Tests.Configuration;

public class ConfigurationReaderTests
{
    private static (ConfigurationReader Reader, SettingsStore Settings) Build()
    {
        SettingsStore settings = new();
        ConfigurationReader reader = new();
        reader.Register(new SetDirective(settings));
        return (reader, settings);
    }

    [Fact]
    public void ReadLines_AppliesSetDirectives()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([
            "set show_hidden true",
            "set scroll_offset 3",
            "set column_ratios 1,2",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal(true, settings.Get("show_hidden"));
        Assert.Equal(3, settings.Get("scroll_offset"));
        Assert.Equal([1, 2], (IReadOnlyList<int>)settings.Get("column_ratios")!);
    }

    [Fact]
    public void ReadLines_SkipsCommentsAndBlankLines()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([
            "",
            "   ",
            "# set show_hidden true",
            "   # indented comment",
            "set show_hidden true",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal(true, settings.Get("show_hidden"));
    }

    [Fact]
    public void ReadLines_AcceptsBothAssignmentForms()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines(["set sort=size", "set scroll_offset 5"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("size", settings.Get("sort"));
        Assert.Equal(5, settings.Get("scroll_offset"));
    }

    [Fact]
    public void ReadLines_TreatsABareSetAsAssigningTheEmptyString()
    {
        // cc.conf clears global_inode_type_filter with a bare "set" line, so this form must work.
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines(["set global_inode_type_filter"]);

        Assert.Empty(reader.Errors);
        Assert.Equal(string.Empty, settings.Get("global_inode_type_filter"));
    }

    [Fact]
    public void ReadLines_TogglesWithATrailingBang()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines(["set show_hidden!", "set viewmode!"]);

        Assert.Empty(reader.Errors);
        Assert.Equal(true, settings.Get("show_hidden"));
        Assert.Equal("multipane", settings.Get("viewmode"));
    }

    [Fact]
    public void ReadLines_PreservesRegularExpressionsVerbatim()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([@"set hidden_filter ^\.|\.(?:pyc|pyo|bak|swp)$|^lost\+found$"]);

        Assert.Empty(reader.Errors);
        Assert.Equal(@"^\.|\.(?:pyc|pyo|bak|swp)$|^lost\+found$", settings.Get("hidden_filter"));
    }

    [Fact]
    public void ReadLines_ScopesSetinpathToDirectoriesEndingInThePath()
    {
        // ranger anchors the escaped path at the end, so the scope applies to the directory
        // itself and not to its children (ranger/config/commands.py:568).
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines(["setinpath path=/data sort=mtime"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("mtime", settings.Get("sort", "/data"));
        Assert.Equal("natural", settings.Get("sort", "/data/photos"));
        Assert.Equal("natural", settings.Get("sort", "/elsewhere"));
    }

    [Fact]
    public void ReadLines_ScopesARelativeSetinpathToAnyMatchingDirectory()
    {
        // Because the pattern is only anchored at the end, "path=build" matches a build
        // directory anywhere in the tree. That is documented ranger behaviour, not an accident.
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines(["setinpath path=build sort=mtime"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("mtime", settings.Get("sort", "/home/user/project/build"));
        Assert.Equal("mtime", settings.Get("sort", "/srv/other/build"));
        Assert.Equal("natural", settings.Get("sort", "/home/user/project"));
    }

    [Fact]
    public void ReadLines_AcceptsAQuotedOperandContainingSpaces()
    {
        // Directory names with spaces are ordinary, and the sample configuration is full of
        // them. Both quote styles must work.
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([
            "setinregex re='/My Documents/Work Files$' sort=mtime",
            "setinpath path=\"/Some Where\" sort_reverse=true",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal("mtime", settings.Get("sort", "/home/u/My Documents/Work Files"));
        Assert.Equal(true, settings.Get("sort_reverse", "/mnt/Some Where"));
    }

    [Fact]
    public void ReadLines_AcceptsTheAlternativeOperandNames()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([
            "setinregex regex=/photos$ sort=mtime",
            "setinpath pattern=/music sort_reverse=true",
        ]);

        Assert.Empty(reader.Errors);
        Assert.Equal("mtime", settings.Get("sort", "/home/u/photos"));
        Assert.Equal(true, settings.Get("sort_reverse", "/home/u/music"));
    }

    [Fact]
    public void ReadLines_ScopesSetinregexToMatchingPaths()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([@"setinregex re=.*\.git sort=size"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("size", settings.Get("sort", "/home/user/project/.git"));
        Assert.Equal("natural", settings.Get("sort", "/home/user/project"));
    }

    [Fact]
    public void ReadLines_ScopesSetintagToTaggedFiles()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines(["setintag ab sort=size"]);

        Assert.Empty(reader.Errors);
        Assert.Equal("size", settings.Get("sort", path: null, tags: ['a']));
        Assert.Equal("size", settings.Get("sort", path: null, tags: ['b']));
        Assert.Equal("natural", settings.Get("sort", path: null, tags: ['c']));
    }

    [Fact]
    public void ReadLines_RecordsUnknownDirectivesAndKeepsGoing()
    {
        // A single bad line must not leave the user with an unconfigured file manager.
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([
            "set show_hidden true",
            "map gg move to=0",       // not registered yet; arrives with the keybinding layer
            "set scroll_offset 4",
        ]);

        Assert.Single(reader.Errors);
        Assert.Contains("map", reader.Errors[0].Message, StringComparison.Ordinal);
        Assert.Equal(2, reader.Errors[0].LineNumber);

        Assert.Equal(true, settings.Get("show_hidden"));
        Assert.Equal(4, settings.Get("scroll_offset"));
    }

    [Fact]
    public void ReadLines_RecordsBadValuesAndKeepsGoing()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        reader.ReadLines([
            "set scroll_offset banana",
            "set viewmode cascade",
            "set no_such_setting 1",
            "set show_hidden true",
        ]);

        Assert.Equal(3, reader.Errors.Count);
        Assert.Equal(true, settings.Get("show_hidden"));
    }

    [Fact]
    public void ReadFiles_AppliesLaterFilesOnTopOfEarlierOnes()
    {
        (ConfigurationReader reader, SettingsStore settings) = Build();

        string directory = Path.Join(Path.GetTempPath(), "canger-conf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            string shipped = Path.Join(directory, "shipped.conf");
            string user = Path.Join(directory, "user.conf");
            File.WriteAllLines(shipped, ["set scroll_offset 8", "set sort natural"]);
            File.WriteAllLines(user, ["set sort size"]);

            reader.ReadFiles([shipped, Path.Join(directory, "missing.conf"), user]);

            Assert.Empty(reader.Errors);
            // The user's file overrides what it mentions and inherits what it does not.
            Assert.Equal("size", settings.Get("sort"));
            Assert.Equal(8, settings.Get("scroll_offset"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Canger must parse the shipped cc.conf with no errors other than directives that later
    /// phases will register.
    /// </summary>
    [Fact]
    public void ShippedConfigParsesWithoutSettingErrors()
    {
        (ConfigurationReader reader, _) = Build();

        reader.ReadFile(TestPaths.ShippedConfig("cc.conf"));

        string[] unexpected =
        [
            .. reader.Errors
                .Where(e => !IsNotYetImplementedDirective(e))
                .Select(e => e.ToString()),
        ];

        Assert.Empty(unexpected);
    }

    /// <summary>
    /// The real user configuration in <c>ranger-settings/rc.conf</c> must parse too.
    /// </summary>
    /// <remarks>
    /// That file predates the ranger release Canger is ported from and carries roughly 150
    /// personal customisations, so it is a far better compatibility check than anything
    /// synthetic. Every one of its <c>set</c> lines has to be understood.
    /// </remarks>
    [Fact]
    public void SampleRangerConfigSetLinesAllParse()
    {
        Assert.SkipWhen(!File.Exists(TestPaths.SampleRangerConfig),
                        "sample ranger configuration is not present");

        (ConfigurationReader reader, _) = Build();

        // nested_ranger_warning is the one setting Canger renames, because it names the program.
        IEnumerable<string> lines = File.ReadLines(TestPaths.SampleRangerConfig)
            .Select(line => line.Replace("nested_ranger_warning", "nested_canger_warning",
                                         StringComparison.Ordinal));

        reader.ReadLines(lines, "ranger-settings/rc.conf");

        string[] settingErrors =
        [
            .. reader.Errors
                .Where(e => !IsNotYetImplementedDirective(e))
                .Select(e => e.ToString()),
        ];

        Assert.Empty(settingErrors);
    }

    /// <summary>
    /// Whether an error is only about a directive a later phase will register, such as the
    /// keybinding and alias families.
    /// </summary>
    private static bool IsNotYetImplementedDirective(ConfigurationError error) =>
        error.Message.StartsWith("unknown directive", StringComparison.Ordinal);
}
