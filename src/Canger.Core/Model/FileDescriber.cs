// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Canger.Core.Model;

/// <summary>
/// Describes files by asking <c>file(1)</c>, off the drawing path.
/// </summary>
/// <remarks>
/// Ranger asks <c>file</c> synchronously while drawing each row, which stalls a large directory on
/// one process launch per entry. Here the answer is cached and computed on the thread pool: a row
/// with no answer yet shows nothing, and the redraw raised when the answer lands fills it in. The
/// cache is keyed by path and modification time, so an edited file is described again.
/// </remarks>
/// <param name="onDescribed">Called when an answer arrives, so the caller can redraw.</param>
public sealed class FileDescriber(Action? onDescribed = null) : IFileDescriber, IDisposable
{
    /// <summary>What is shown for a file <c>file(1)</c> could not identify.</summary>
    public const string Unknown = "unknown";

    /// <summary>How long <c>file</c> is given before its answer is abandoned.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    /// <inheritdoc />
    public string Describe(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string key = KeyFor(path);

        if (_cache.TryGetValue(key, out string? description))
        {
            return description;
        }

        // TryAdd both claims the work and rejects a second request for a file already in flight,
        // which matters because a redraw asks about the same rows over and over.
        if (!_shutdown.IsCancellationRequested && _inFlight.TryAdd(key, 0))
        {
            _ = Task.Run(() => Ask(path, key), _shutdown.Token);
        }

        return string.Empty;
    }

    /// <summary>Forgets every answer, so files are described afresh.</summary>
    public void Clear() => _cache.Clear();

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    /// <summary>Keys an answer by path and modification time, so an edit invalidates it.</summary>
    private static string KeyFor(string path)
    {
        try
        {
            return $"{path} {File.GetLastWriteTimeUtc(path).Ticks}";
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "Describing a file is decoration; nothing it can fail with "
                                   + "should be able to reach the drawing loop.")]
    private void Ask(string path, string key)
    {
        string description;

        try
        {
            // -L follows symbolic links and -b omits the filename. The path is passed as an
            // argument rather than through a shell, so a name cannot be read as an option or
            // as anything else.
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("file")
                {
                    ArgumentList = { "-Lb", "--", path },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();

            description = process.WaitForExit(Deadline) && process.ExitCode == 0
                ? output.Trim()
                : Unknown;

            if (description.Length == 0)
            {
                description = Unknown;
            }
        }
        catch (Exception)
        {
            description = Unknown;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }

        _cache[key] = description;
        onDescribed?.Invoke();
    }
}
