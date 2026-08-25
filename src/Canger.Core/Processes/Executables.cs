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
    public static void Invalidate()
    {
        Cache.Clear();
        _names = null;
    }

    private static IReadOnlyList<string>? _names;

    /// <summary>Every program on the PATH whose name begins with a prefix.</summary>
    /// <param name="prefix">What has been typed so far.</param>
    /// <returns>The matching names, in order, without duplicates.</returns>
    /// <remarks>
    /// What <c>:shell</c> completes against, as ranger's <c>get_executables()</c> does for the
    /// same command. The whole PATH is walked once and kept: it is a few thousand names on a
    /// normal machine, and walking it on every keystroke would be felt.
    /// </remarks>
    public static IReadOnlyList<string> Matching(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        _names ??= All();

        return [.. _names.Where(n => n.StartsWith(prefix, StringComparison.Ordinal))];
    }

    /// <summary>Walks the PATH once.</summary>
    private static List<string> All()
    {
        SortedSet<string> found = new(StringComparer.Ordinal);
        string? path = Environment.GetEnvironmentVariable("PATH");

        foreach (string directory in (path ?? string.Empty)
                     .Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (IsExecutable(file))
                    {
                        found.Add(Path.GetFileName(file));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A PATH entry that does not exist or cannot be read is ordinary; skip it.
            }
        }

        return [.. found];
    }

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
