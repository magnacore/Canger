// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// Decides whether removing something needs confirming, and asks if it does.
/// </summary>
/// <remarks>
/// Shared by <c>:delete</c> and <c>:trash</c> so that the two cannot disagree about what counts
/// as risky. The default, <c>multiple</c>, asks only when more than one thing would go or when a
/// directory is not empty — the cases where a slip is expensive — and stays out of the way for
/// the single stray file that is most of what anyone deletes.
/// </remarks>
public static class DeletionConfirmation
{
    /// <summary>How many names are listed in the question before it is abbreviated.</summary>
    private const int NamesShown = 8;

    /// <summary>
    /// Runs an action, first asking the user to confirm if the setting calls for it.
    /// </summary>
    /// <param name="fileManager">Who asks, and whose settings decide.</param>
    /// <param name="selection">What would be removed.</param>
    /// <param name="setting">
    /// The resolved <c>confirm_on_delete</c> value: <c>always</c>, <c>never</c>, or
    /// <c>multiple</c>. Anything else is treated as <c>multiple</c>, so a bad value errs towards
    /// asking rather than towards silent deletion.
    /// </param>
    /// <param name="proceed">What to do once it is confirmed, or at once if it need not be.</param>
    public static void Confirm(IFileManager fileManager, IReadOnlyList<FsNode> selection,
                               string setting, Action proceed)
    {
        ArgumentNullException.ThrowIfNull(fileManager);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(proceed);

        if (!NeedsConfirming(fileManager, selection, setting))
        {
            proceed();
            return;
        }

        fileManager.Ask(
            $"Confirm deletion of {selection.Count} items: {Describe(selection)} (y/N)",
            answer =>
            {
                // Only an explicit yes proceeds; the question defaults to no, so a mistaken
                // keystroke or an Escape leaves everything where it is.
                if (char.ToLowerInvariant(answer) == 'y')
                {
                    proceed();
                }
            },
            ['n', 'N', 'y', 'Y']);
    }

    /// <summary>Whether the setting and the selection together call for a question.</summary>
    private static bool NeedsConfirming(IFileManager fileManager, IReadOnlyList<FsNode> selection,
                                        string setting) => setting switch
    {
        "never" => false,
        "always" => true,
        _ => selection.Count > 1 || selection.Any(e => IsNonEmptyDirectory(fileManager, e)),
    };

    /// <summary>
    /// Whether an entry is a directory with something in it.
    /// </summary>
    /// <remarks>
    /// A link to a directory is not one for this purpose: removing it removes the link and leaves
    /// the target alone, so it is no more dangerous than removing a file.
    /// </remarks>
    private static bool IsNonEmptyDirectory(IFileManager fileManager, FsNode entry)
    {
        if (!entry.IsDirectory || entry.IsSymbolicLink)
        {
            return false;
        }

        try
        {
            return fileManager.FileSystem.ListDirectory(entry.Path).Count > 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A directory that cannot even be listed is exactly the kind that should be asked
            // about rather than removed quietly.
            return true;
        }
    }

    /// <summary>Names what would go, abbreviated so the question fits on a line.</summary>
    private static string Describe(IReadOnlyList<FsNode> selection)
    {
        IEnumerable<string> names = selection.Take(NamesShown).Select(e => e.RelativePath);

        return selection.Count > NamesShown
            ? $"{string.Join(", ", names)} and {selection.Count - NamesShown} more"
            : string.Join(", ", names);
    }
}
