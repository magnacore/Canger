// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Core.FileSystem;

/// <summary>
/// Turns a path as a person writes it into one the filesystem accepts.
/// </summary>
/// <remarks>
/// <para>
/// A configuration file is full of <c>~/Documents</c> and <c>$XDG_DATA_HOME/thing</c>, and neither
/// means anything to a system call. Ranger expands both inside <c>Tab</c> itself
/// (<c>core/tab.py:25</c>, <c>abspath(expanduser(path))</c>), so every path a tab is given gets
/// the treatment however it arrived.
/// </para>
/// <para>
/// Canger expanded them only in <c>:cd</c>, which meant <c>:tab_new ~/Books</c> opened a tab on a
/// directory literally named <c>~</c> beneath the current one — thirteen bindings in a real
/// configuration, every one of them opening an unusable tab. Doing it where ranger does covers
/// every caller at once.
/// </para>
/// </remarks>
public static class UserPath
{
    /// <summary>
    /// Expands <c>~</c> and <c>$NAME</c>, then makes the result absolute.
    /// </summary>
    /// <param name="path">The path as written.</param>
    /// <param name="relativeTo">
    /// What a relative path is relative to. When null the process's own directory is used, which
    /// is what <see cref="System.IO.Path.GetFullPath(string)"/> would do anyway.
    /// </param>
    /// <returns>An absolute path.</returns>
    public static string Expand(string path, string? relativeTo = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string expanded = ExpandVariables(path);

        // Only a leading tilde: `~` elsewhere in a path is an ordinary character, and plenty of
        // real filenames contain one.
        if (expanded.Length > 0 && expanded[0] == '~')
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            expanded = expanded.Length == 1
                ? home
                : Path.Join(home, expanded[1..].TrimStart('/'));
        }

        return relativeTo is { Length: > 0 } && !Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded, relativeTo)
            : Path.GetFullPath(expanded);
    }

    /// <summary>
    /// Substitutes environment variables written as <c>$NAME</c> or <c>${NAME}</c>.
    /// </summary>
    /// <param name="path">The path as typed.</param>
    /// <returns>The path with variables replaced.</returns>
    /// <remarks>
    /// An unset variable expands to nothing, as a shell does. That can leave a path that does not
    /// exist, which the caller then reports — better than silently going somewhere else.
    /// </remarks>
    public static string ExpandVariables(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!path.Contains('$', StringComparison.Ordinal))
        {
            return path;
        }

        StringBuilder result = new(path.Length);

        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] != '$')
            {
                result.Append(path[i]);
                continue;
            }

            bool braced = i + 1 < path.Length && path[i + 1] == '{';
            int start = braced ? i + 2 : i + 1;
            int end = start;

            while (end < path.Length &&
                   (char.IsLetterOrDigit(path[end]) || path[end] == '_'))
            {
                end++;
            }

            if (end == start)
            {
                // A lone `$`, or `${}`: nothing to look up, so it stays as written.
                result.Append(path[i]);
                continue;
            }

            result.Append(Environment.GetEnvironmentVariable(path[start..end]) ?? string.Empty);

            // Step past the name, and past the closing brace when there is one.
            i = braced && end < path.Length && path[end] == '}' ? end : end - 1;
        }

        return result.ToString();
    }
}
