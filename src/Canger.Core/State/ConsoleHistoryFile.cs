// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.State;

/// <summary>
/// The console's command history, kept between sessions.
/// </summary>
/// <remarks>
/// <para>
/// One command per line, oldest first, in <c>~/.local/share/canger/history</c> — the same file
/// name and format ranger uses (<c>gui/widgets/console.py:45-91</c>), so the two can be pointed
/// at each other's file.
/// </para>
/// <para>
/// Reading is not gated on <c>save_console_history</c>: ranger loads whatever is there and lets
/// the setting decide only whether the session writes anything back. Turning saving off therefore
/// freezes the file rather than hiding it.
/// </para>
/// </remarks>
public static class ConsoleHistoryFile
{
    /// <summary>Reads the saved commands into a history.</summary>
    /// <param name="history">Where to put them.</param>
    /// <param name="path">The file to read.</param>
    /// <returns>Why it could not be read, or <see langword="null"/> when it was.</returns>
    public static string? Load(History<string> history, string path)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            foreach (string line in File.ReadLines(path))
            {
                // Blank lines are skipped rather than stored: an empty entry would be something
                // the user could scroll onto and could never have typed.
                if (line.Length > 0)
                {
                    history.Add(line);
                }
            }

            // Left pointing at the newest entry, so the first press of Up offers the last thing
            // that was run rather than the oldest.
            history.FastForward();

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Text.DecoderFallbackException)
        {
            return e.Message;
        }
    }

    /// <summary>Writes the commands out.</summary>
    /// <param name="history">What to write.</param>
    /// <param name="path">The file to write.</param>
    /// <returns>Why it could not be written, or <see langword="null"/> when it was.</returns>
    public static string? Save(History<string> history, string path)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            // Written to a temporary file and moved into place, so a session that dies partway
            // through cannot leave a truncated history behind. Ranger writes in place.
            string temporary = path + ".new";

            File.WriteAllLines(temporary, history.Entries.Where(e => e.Length > 0));
            File.Move(temporary, RealPath(path), overwrite: true);

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
    /// <summary>Where a state file really lives, following a link if it is one.</summary>
    /// <param name="path">The configured path.</param>
    /// <returns>The path to rename over.</returns>
    /// <remarks>
    /// <c>rename(2)</c> replaces a symbolic link rather than what it points at, so replacing the
    /// file in place would break a link into a dotfiles repository and leave later changes
    /// accumulating in an untracked file. Ranger has the same branch
    /// (<c>container/bookmarks.py:200-204</c>).
    /// </remarks>
    private static string RealPath(string path)
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

}
