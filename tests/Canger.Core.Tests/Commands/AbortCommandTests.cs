// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What Ctrl-C does.
/// </summary>
/// <remarks>
/// The binding ships in ranger's own configuration and in Canger's, but in ranger it can never
/// fire: Ctrl-C arrives as a signal and kills the process first. Canger delivers it as a
/// keystroke, so this is the first time the binding means anything.
/// </remarks>
public class AbortCommandTests
{
    private static FakeFileManager Manager() =>
        new(new InMemoryFileSystem().AddFile("/home/a.txt"), "/home");

    /// <summary>A task that never finishes on its own, so it can be found still running.</summary>
    private sealed class Endless : ILoadable
    {
        public string Description => "copying things";

        public bool IsFinished { get; private set; }

        public double? Progress => 0.5;

        public IEnumerator<Unit> Steps()
        {
            while (!IsFinished)
            {
                yield return Unit.Value;
            }
        }
    }

    [Fact]
    public void Abort_CancelsWhatIsRunning()
    {
        FakeFileManager manager = Manager();
        manager.Tasks.Add(new Endless());

        manager.Execute("abort");

        Assert.Contains(manager.Messages, m => m.Message.Contains("copying things",
                                                                 StringComparison.Ordinal));
        Assert.True(manager.Tasks.Current is null or { IsComplete: true });
    }

    [Fact]
    public void Abort_SaysHowToQuitWhenThereIsNothingToCancel()
    {
        // Ctrl-C is what someone presses when they want out, so answering that is more use than
        // doing nothing at all.
        FakeFileManager manager = Manager();

        manager.Execute("abort");

        Assert.Contains(manager.Messages, m => m.Message.Contains("quit", StringComparison.Ordinal));
    }

    [Fact]
    public void Abort_DoesNotQuit()
    {
        // The point is to stop the copy, not the program.
        FakeFileManager manager = Manager();
        manager.Tasks.Add(new Endless());

        manager.Execute("abort");

        Assert.False(manager.HasQuit);
    }
}
