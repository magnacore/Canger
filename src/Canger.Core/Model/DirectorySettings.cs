// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Model;

/// <summary>
/// What a directory needs to know before it lists anything: how to order it and what to leave out.
/// </summary>
/// <remarks>
/// Gathered into one value so that a directory can be given all of it at the moment it is created,
/// rather than being made with the defaults and corrected afterwards. The correction was visible:
/// a directory opened under a sort other than the default was ordered by name for its first load,
/// which is when the cursor is placed, and the cursor then stayed on that entry as the real order
/// moved it down the list.
/// </remarks>
/// <param name="Order">The <c>sort</c> family, already resolved.</param>
/// <param name="ShowHidden">The <c>show_hidden</c> setting.</param>
/// <param name="HiddenPattern">The <c>hidden_filter</c> setting.</param>
/// <param name="AutoupdateCumulativeSize">The <c>autoupdate_cumulative_size</c> setting.</param>
public sealed record DirectorySettings(
    SortOrder Order,
    bool ShowHidden,
    string HiddenPattern,
    bool AutoupdateCumulativeSize)
{
    /// <summary>What a directory gets before any configuration has been read.</summary>
    public static DirectorySettings Default { get; } =
        new(SortOrder.Default, ShowHidden: false, HiddenPattern: string.Empty,
            AutoupdateCumulativeSize: false);

    /// <summary>Gives a directory these settings.</summary>
    /// <param name="directory">The directory to configure.</param>
    /// <remarks>
    /// Every setter re-derives the listing on a change and returns at once on a non-change, so
    /// this is cheap to apply to a directory that already has them.
    /// </remarks>
    public void ApplyTo(DirectoryNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        directory.ShowHidden = ShowHidden;
        directory.HiddenPattern = HiddenPattern;
        directory.SortOrder = Order;
        directory.AutoupdateCumulativeSize = AutoupdateCumulativeSize;
    }
}
