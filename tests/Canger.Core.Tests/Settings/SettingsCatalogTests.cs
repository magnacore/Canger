// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.Settings;

namespace Canger.Core.Tests.Settings;

/// <summary>
/// Keeps the catalog honest against ranger and against Canger's own shipped configuration.
/// </summary>
public partial class SettingsCatalogTests
{
    [GeneratedRegex(@"^\s*set\s+(?<name>[a-z_0-9]+)\s*(?<value>.*)$")]
    private static partial Regex SetLine { get; }

    [Fact]
    public void EverySettingHasASummary()
    {
        string[] undocumented =
            [.. SettingsCatalog.All.Values.Where(d => d.Summary.Length == 0).Select(d => d.Name)];

        Assert.Empty(undocumented);
    }

    [Fact]
    public void EnumeratedSettingsDefaultToAnAllowedValue()
    {
        foreach (SettingDefinition definition in SettingsCatalog.All.Values.Where(d => d.IsEnumerated))
        {
            if (definition.DefaultValue is null)
            {
                Assert.True(definition.AllowsNull,
                            $"{definition.Name} defaults to null but does not allow null");
                continue;
            }

            Assert.Contains((string)definition.DefaultValue, definition.AllowedValues!,
                            StringComparer.Ordinal);
        }
    }

    [Fact]
    public void EveryDefaultSatisfiesItsOwnSchema()
    {
        foreach (SettingDefinition definition in SettingsCatalog.All.Values)
        {
            definition.Validate(definition.DefaultValue);
        }
    }

    /// <summary>
    /// The catalog defaults and the shipped cc.conf must agree.
    /// </summary>
    /// <remarks>
    /// Canger carries working defaults in code so it runs correctly with no configuration at
    /// all, and also ships a cc.conf documenting those same values. Two sources of truth drift
    /// unless something checks them, which is what this test is for.
    /// </remarks>
    [Fact]
    public void ShippedConfigAgreesWithTheCatalogDefaults()
    {
        SettingsStore fromConfig = new();
        SettingsStore untouched = new();
        List<string> mismatches = [];

        foreach (string line in File.ReadLines(TestPaths.ShippedConfig("cc.conf")))
        {
            if (SetLine.Match(line.Trim()) is not { Success: true } match)
            {
                continue;
            }

            string name = match.Groups["name"].Value;
            if (SettingsCatalog.Find(name) is null)
            {
                mismatches.Add($"cc.conf sets unknown setting '{name}'");
                continue;
            }

            fromConfig.SetFromText(name, match.Groups["value"].Value);

            object? configured = fromConfig.Get(name);
            object? declared = untouched.Get(name);

            if (!ValuesMatch(configured, declared))
            {
                mismatches.Add(
                    $"{name}: cc.conf says '{Render(configured)}', catalog default is '{Render(declared)}'");
            }
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// Every setting ranger knows about must exist in Canger, and vice versa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two names that differ do so on purpose: ranger's <c>nested_ranger_warning</c> is
    /// Canger's <c>nested_canger_warning</c>, since it names the program.
    /// </para>
    /// <para>
    /// <see cref="CangerOnlySettings"/> is the list of everything Canger has that ranger does
    /// not, and it is meant to be short. A setting is added to it deliberately, in the knowledge
    /// that a configuration using it will not be understood by ranger.
    /// </para>
    /// </remarks>
    [Fact]
    public void CatalogCoversEverySettingRangerDefines()
    {
        string rangerSettings = Path.Join(
            Directory.GetParent(TestPaths.Root)!.FullName,
            "ranger-master", "ranger", "container", "settings.py");

        Assert.SkipWhen(!File.Exists(rangerSettings),
                        "ranger-master reference source is not present");

        string source = File.ReadAllText(rangerSettings);
        int start = source.IndexOf("ALLOWED_SETTINGS = {", StringComparison.Ordinal);
        int end = source.IndexOf('}', start);

        HashSet<string> expected =
        [
            .. SettingNameInDict().Matches(source[start..end]).Select(m => m.Groups[1].Value),
        ];
        expected.Remove("nested_ranger_warning");
        expected.Add("nested_canger_warning");
        expected.UnionWith(CangerOnlySettings);

        HashSet<string> actual = [.. SettingsCatalog.Names];

        Assert.Empty(expected.Except(actual, StringComparer.Ordinal));
        Assert.Empty(actual.Except(expected, StringComparer.Ordinal));
    }

    /// <summary>
    /// Settings Canger has and ranger does not.
    /// </summary>
    /// <remarks>
    /// <c>unlock_prompt</c> governs a feature ranger has no counterpart for at all — removable
    /// drives — and <c>shared_copy_buffer</c> another: ranger's copy buffer is an in-memory set
    /// on the file manager and cannot be shared between windows. So there is nothing for either
    /// to match. Everything else in the catalogue is ranger's, and this list existing is what
    /// keeps that true: a setting cannot be added without either matching ranger or being written
    /// down here.
    /// </remarks>
    private static readonly string[] CangerOnlySettings =
        ["unlock_prompt", "shared_copy_buffer"];

    [GeneratedRegex("""["']([a-z_0-9]+)["']\s*:""")]
    private static partial Regex SettingNameInDict();

    private static bool ValuesMatch(object? left, object? right) =>
        (left, right) switch
        {
            (IReadOnlyList<int> a, IReadOnlyList<int> b) => a.SequenceEqual(b),
            _ => Equals(left, right),
        };

    private static string Render(object? value) => value switch
    {
        null => "none",
        IReadOnlyList<int> list => string.Join(",", list),
        _ => value.ToString() ?? "?",
    };
}
