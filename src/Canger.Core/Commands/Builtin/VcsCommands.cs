// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Vcs;

namespace Canger.Core.Commands.Builtin;

/// <summary>Adds the selection to the version control system's index.</summary>
[Command("stage", Summary = "Stage the selection for commit.")]
public sealed class StageCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        VcsAction.Run(FileManager, "stage",
                      static (backend, root, paths) => backend.Add(root, paths));
}

/// <summary>Takes the selection back out of the index.</summary>
[Command("unstage", Summary = "Unstage the selection.")]
public sealed class UnstageCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() =>
        VcsAction.Run(FileManager, "unstage",
                      static (backend, root, paths) => backend.Reset(root, paths));
}

/// <summary>The work shared by the staging commands.</summary>
internal static class VcsAction
{
    /// <summary>
    /// Runs an action against the repository the current directory belongs to.
    /// </summary>
    /// <param name="fileManager">What to act on.</param>
    /// <param name="verb">The command's name, for the messages.</param>
    /// <param name="action">What to do, given the backend, the root, and the paths.</param>
    public static void Run(IFileManager fileManager, string verb,
                           Action<IVcsBackend, string, IReadOnlyList<string>> action)
    {
        ArgumentNullException.ThrowIfNull(fileManager);
        ArgumentNullException.ThrowIfNull(action);

        if (fileManager.Vcs is not { } service)
        {
            fileManager.Notify($"{verb}: version control is off (set vcs_aware true)",
                               isError: true);
            return;
        }

        if (service.RepositoryFor(fileManager.CurrentDirectory.Path) is not { } repository)
        {
            fileManager.Notify($"{verb}: not in a repository", isError: true);
            return;
        }

        // Paths are given relative to the root, which is where the command runs.
        IReadOnlyList<string> paths =
        [
            .. fileManager.Selection.Select(entry => Path.GetRelativePath(repository.Root,
                                                                         entry.Path))
        ];

        if (paths.Count == 0)
        {
            fileManager.Notify($"{verb}: nothing selected", isError: true);
            return;
        }

        try
        {
            action(repository.Backend, repository.Root, paths);
        }
        catch (VcsException e)
        {
            fileManager.Notify($"{verb}: {e.Message}", isError: true);
            return;
        }

        // The status just changed, so what is on screen is now wrong; asking for a refresh puts
        // it right on the next frame.
        repository.Refresh();
        fileManager.Notify($"{verb}d {paths.Count}");
    }
}
