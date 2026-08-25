// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;

namespace Canger.App;

/// <summary>
/// Tabs remembered from one session to the next.
/// </summary>
/// <remarks>
/// <para>
/// The file is a queue of records rather than a single snapshot, which is what lets several
/// sessions be open at once: each quitting session appends its own record, and each starting
/// session consumes the first. Ranger's format exactly (<c>core/fm.py:551-555</c> and
/// <c>core/main.py:159-180</c>) — paths separated by NUL, records terminated by two.
/// </para>
/// <para>
/// NUL is the one byte a path cannot contain, which is why it separates them.
/// </para>
/// </remarks>
internal static class SavedTabs
{
    private const string RecordSeparator = "\0\0";

    /// <summary>Appends this session's tabs to the file.</summary>
    /// <param name="path">The <c>tabs</c> file in the data directory.</param>
    /// <param name="tabPaths">The directory each tab is showing, in tab order.</param>
    /// <remarks>
    /// A single tab is not worth remembering: reopening Canger gives you one anyway, and saving
    /// it would mean every ordinary quit left a record behind for the next start to consume.
    /// Ranger declines for the same reason (<c>core/fm.py:551</c>).
    /// </remarks>
    internal static void Save(string path, IReadOnlyList<string> tabPaths)
    {
        if (tabPaths.Count <= 1)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.AppendAllText(path, string.Join('\0', tabPaths) + RecordSeparator);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing the tab list is not worth refusing to quit over.
        }
    }

    /// <summary>Takes the next record from the file, removing it.</summary>
    /// <param name="path">The <c>tabs</c> file in the data directory.</param>
    /// <param name="filterDead">Whether to drop paths that are no longer directories.</param>
    /// <returns>The remembered paths, oldest record first, or empty when there are none.</returns>
    internal static IReadOnlyList<string> Take(string path, bool filterDead)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            string content = File.ReadAllText(path);
            int end = content.IndexOf(RecordSeparator, StringComparison.Ordinal);
            string record = end < 0 ? content : content[..end];
            string rest = end < 0 ? string.Empty : content[(end + RecordSeparator.Length)..];

            // The record is consumed whether or not it turns out to hold anything usable, so a
            // file that cannot be restored does not stop every future session as well.
            if (rest.Length > 0)
            {
                File.WriteAllText(path, rest);
            }
            else
            {
                File.Delete(path);
            }

            string[] paths = record.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            return filterDead ? [.. paths.Where(Directory.Exists)] : paths;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
