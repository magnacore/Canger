// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;

namespace Canger.Core.Processes;

/// <summary>
/// Finds out which programs are installed.
/// </summary>
/// <remarks>
/// The file launcher asks this constantly — a configuration of two hundred rules may test a hundred
/// programs before finding one that applies — so answers are cached. The set of installed
/// programs does not change meaningfully during a session.
/// </remarks>
public static class Executables
{
    private static readonly ConcurrentDictionary<string, bool> Cache = new(StringComparer.Ordinal);

    /// <summary>Whether a program can be found and run.</summary>
    /// <param name="program">The name, or an absolute path.</param>
    /// <returns><see langword="true"/> when it exists and is executable.</returns>
    public static bool Exists(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        return Cache.GetOrAdd(program, Search);
    }

    /// <summary>Forgets what has been looked up, so a newly installed program is noticed.</summary>
    public static void Invalidate() => Cache.Clear();

    private static bool Search(string program)
    {
        if (Path.IsPathRooted(program))
        {
            return IsExecutable(program);
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return false;
        }

        foreach (string directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsExecutable(Path.Join(directory, program)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            return File.Exists(path) &&
                   (File.GetUnixFileMode(path) &
                    (UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                     UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
