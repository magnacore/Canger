// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// The spinner that says background work is going on.
/// </summary>
/// <remarks>
/// Without the task view open it is the only sign a queued command gives, so it is what tells
/// the user their archive is being unpacked rather than ignored. Ranger's characters and rule
/// (<c>core/loader.py:333-351</c>).
/// </remarks>
public class ThrobberTests
{
    /// <summary>A task that never finishes on its own.</summary>
    private sealed class Endless : ILoadable
    {
        public string Description => "waiting";

        public double? Progress => null;

        public IEnumerator<Unit> Steps()
        {
            while (true)
            {
                yield return Unit.Value;
            }
        }
    }

    [Fact]
    public void Throbber_TurnsOncePerSliceAndWrapsRound()
    {
        TaskQueue queue = new();
        queue.Add(new Endless());

        List<char> frames = [];

        for (int i = 0; i < 5; i++)
        {
            queue.Work(TimeSpan.Zero);
            frames.Add(queue.Throbber);
        }

        Assert.Equal(['-', '\\', '|', '/', '-'], frames);
    }

    [Fact]
    public void Throbber_StandsStillWhileNothingIsRunning()
    {
        // `Work` is what turns it, and `Work` does nothing with an empty queue — so an idle
        // Canger does not have a spinner quietly rotating behind a hidden flag.
        TaskQueue queue = new();
        char before = queue.Throbber;

        queue.Work(TimeSpan.Zero);

        Assert.Equal(before, queue.Throbber);
        Assert.False(queue.HasWork);
    }

    [Fact]
    public void Throbber_ShowsAHashWhileTheTaskIsPaused()
    {
        // The one state a spinner cannot express: a stopped spinner and a slow one look alike.
        TaskQueue queue = new();
        QueuedTask task = queue.Add(new Endless());
        queue.Work(TimeSpan.Zero);

        queue.Pause(task);

        Assert.Equal('#', queue.Throbber);
    }

    [Fact]
    public void Throbber_TurnsAgainOnceResumed()
    {
        TaskQueue queue = new();
        QueuedTask task = queue.Add(new Endless());
        queue.Pause(task);
        queue.Resume(task);
        queue.Work(TimeSpan.Zero);

        Assert.NotEqual('#', queue.Throbber);
    }
}
