// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tests;

/// <summary>
/// Locates repository files that tests read, such as the shipped configuration and the sample
/// ranger configuration used for compatibility checks.
/// </summary>
internal static class TestPaths
{
    /// <summary>The Canger project root, found by walking up from the test assembly.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>The checked-in ranger configuration used as a compatibility fixture.</summary>
    /// <remarks>
    /// This is a real user's configuration, not a synthetic one. Canger must parse it unchanged,
    /// including the roughly 150 keybindings and settings the user customised.
    ///
    /// The directory has been spelled both ways over the life of the port, and the tests that
    /// read it skip themselves when it is missing — so a single wrong character was enough to
    /// turn the whole compatibility check off without anything failing. Both spellings are
    /// accepted rather than one being guessed at.
    /// </remarks>
    internal static string SampleRangerConfig { get; } = FindSampleConfig();

    /// <summary>A file under the shipped <c>config/</c> directory.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The absolute path.</returns>
    internal static string ShippedConfig(string name) => Path.Join(Root, "config", name);

    private static string FindSampleConfig()
    {
        string parent = Directory.GetParent(Root)!.FullName;

        foreach (string name in new[] { "ranger-settings", "ranger_settings" })
        {
            string candidate = Path.Join(parent, name, "rc.conf");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Join(parent, "ranger-settings", "rc.conf");
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Canger.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find Canger.slnx above '{AppContext.BaseDirectory}'.");
    }
}
