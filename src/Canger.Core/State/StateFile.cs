// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.State;

/// <summary>
/// Writing a state file so that a reader never sees half of one.
/// </summary>
/// <remarks>
/// Shared by everything under <c>~/.local/share/canger</c> that more than one running Canger can
/// touch. The rules were learned by <see cref="Tags"/> and are worth stating once rather than
/// being rediscovered by the next file that needs them.
/// </remarks>
internal static class StateFile
{
    /// <summary>Where a state file really lives, following a link if it is one.</summary>
    /// <param name="path">The configured path.</param>
    /// <returns>The path to rename over.</returns>
    /// <remarks>
    /// <c>rename(2)</c> replaces a symbolic link rather than what it points at, so replacing the
    /// file in place would break the link and leave later changes accumulating in an untracked
    /// regular file — until the next re-install of the dotfiles put the stale copy back and took
    /// everything since with it. Keeping a state file as a link into a dotfiles repository is a
    /// common enough arrangement that ranger has the same branch.
    /// </remarks>
    internal static string RealPath(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>Replaces a state file in one step.</summary>
    /// <param name="path">Where the file belongs, link or not.</param>
    /// <param name="lines">The whole contents.</param>
    /// <remarks>
    /// Written beside the target and renamed over it, so a second instance reading at the wrong
    /// moment sees the old file or the new one and never a partial write. Exceptions are left to
    /// the caller: what to do when state cannot be saved differs by what the state is.
    /// </remarks>
    internal static void Replace(string path, IEnumerable<string> lines)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);

        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".new";

        File.WriteAllLines(temporary, lines);
        File.Move(temporary, RealPath(path), overwrite: true);
    }
}
