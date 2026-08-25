// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Canger.Vcs.Backends;

namespace Canger.Vcs;

/// <summary>
/// Finds repositories and keeps their status up to date, off the drawing path.
/// </summary>
/// <remarks>
/// <para>
/// Asking git for a status can take anything from a millisecond to several seconds depending on
/// the size of the tree and how warm the cache is. None of that can happen while a frame is being
/// drawn, so a request is queued and a single worker answers it; the listing shows whatever was
/// last known and is redrawn when a fresh answer arrives.
/// </para>
/// <para>
/// One worker rather than many on purpose. Several git processes over the same repository
/// contend for the same index lock and end up slower than one, and the queue keeps the most
/// recently asked-for directory at the front, which is the one the user is looking at.
/// </para>
/// </remarks>
/// <param name="onUpdated">Called when a repository's status changes, so the caller can redraw.</param>
public sealed class VcsService(Action? onUpdated = null) : IDisposable
{
    /// <summary>How long a repository's status is trusted before it is read again.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Which backends are enabled, by name.</summary>
    public HashSet<string> EnabledBackends { get; } = new(StringComparer.Ordinal) { "git" };

    /// <summary>Every backend Canger knows about.</summary>
    public static IReadOnlyList<IVcsBackend> AllBackends { get; } =
    [
        new GitBackend(),
        new MercurialBackend(),
        new SubversionBackend(),
        new BazaarBackend(),
    ];

    private readonly ConcurrentDictionary<string, VcsRepository?> _byDirectory =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, VcsRepository> _byRoot =
        new(StringComparer.Ordinal);

    private readonly Channel<VcsRepository> _queue =
        Channel.CreateUnbounded<VcsRepository>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;

    /// <summary>
    /// The repository a directory belongs to, if any.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The repository, or <see langword="null"/> when it is not under one.</returns>
    /// <remarks>
    /// The answer is remembered per directory, including the negative one: walking up to the root
    /// looking for a marker directory is a handful of filesystem calls, and it would otherwise
    /// happen for every entry of every listing.
    /// </remarks>
    public VcsRepository? RepositoryFor(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        return _byDirectory.GetOrAdd(directory, Find);
    }

    /// <summary>
    /// Asks for a directory's repository to be brought up to date.
    /// </summary>
    /// <param name="directory">The directory being shown.</param>
    /// <returns>The repository, whose status may still be the previous one.</returns>
    public VcsRepository? Request(string directory)
    {
        if (RepositoryFor(directory) is not { } repository)
        {
            return null;
        }

        if (IsStale(repository) && _queued.TryAdd(repository.Root, 0))
        {
            EnsureWorker();

            if (!_queue.Writer.TryWrite(repository))
            {
                _queued.TryRemove(repository.Root, out _);
            }
        }

        return repository;
    }

    /// <summary>Forgets everything, so the next request looks afresh.</summary>
    public void Reset()
    {
        _byDirectory.Clear();
        _byRoot.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();

        try
        {
            // A brief wait so a command that is mid-flight is not orphaned; not longer, because
            // quitting should never appear to hang.
            _worker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception e) when (e is AggregateException or OperationCanceledException)
        {
            // Shutting down; nothing it could report now is worth acting on.
        }

        _shutdown.Dispose();
    }

    /// <summary>Whether a repository's status is old enough to be worth reading again.</summary>
    private bool IsStale(VcsRepository repository) =>
        repository.UpdatedAt is not { } updated ||
        DateTimeOffset.UtcNow - updated > RefreshInterval;

    /// <summary>Walks up from a directory looking for a repository marker.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "A path that cannot be examined — a broken mount, a "
                                   + "permission denied — simply has no repository.")]
    private VcsRepository? Find(string directory)
    {
        try
        {
            DirectoryInfo? current = new(directory);

            while (current is not null)
            {
                foreach (IVcsBackend backend in AllBackends)
                {
                    if (!EnabledBackends.Contains(backend.Name))
                    {
                        continue;
                    }

                    string marker = Path.Join(current.FullName, backend.MarkerDirectory);

                    // A submodule's .git is a file rather than a directory, so both count.
                    if (!Directory.Exists(marker) && !File.Exists(marker))
                    {
                        continue;
                    }

                    // Interned by root, so every directory of one repository shares one object
                    // and therefore one refresh.
                    return _byRoot.GetOrAdd(current.FullName,
                                            root => new VcsRepository(root, backend));
                }

                current = current.Parent;
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private void EnsureWorker() => _worker ??= Task.Run(WorkAsync, CancellationToken.None);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
                     Justification = "The worker must outlive any single repository's failure; "
                                   + "one unreadable repository cannot be allowed to stop status "
                                   + "updates for every other.")]
    private async Task WorkAsync()
    {
        try
        {
            await foreach (VcsRepository repository in
                           _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                try
                {
                    repository.Refresh();
                }
                catch (Exception)
                {
                    // Refresh already handles what it can; anything reaching here is beyond
                    // this repository's control.
                }
                finally
                {
                    // Removed after refreshing, not before, so a request arriving mid-refresh
                    // queues another pass rather than being dropped.
                    _queued.TryRemove(repository.Root, out _);
                }

                onUpdated?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
