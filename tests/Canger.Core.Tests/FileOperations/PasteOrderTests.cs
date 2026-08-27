// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// Where a paste lands in the queue, and whether the extension-preserving one agrees.
/// </summary>
/// <remarks>
/// `paste_ext` is `paste` with a different rule for renaming clashes — `notes_0.md` rather than
/// `notes.md_0` — and it reaches the queue through the same method, so the flags have to behave
/// the same. That is easy to assume and worth pinning: a configuration that swaps the two keys
/// depends on it.
/// </remarks>
public class PasteOrderTests
{
    private static FakeFileManager Ready()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/one.txt")
            .AddDirectory("/home/dest");

        FakeFileManager manager = new(fs, "/home");
        manager.SetCopyBuffer([manager.CurrentTab.Current.Entries.First(e => e.Basename == "one.txt")],
                              cut: false);
        return manager;
    }

    [Fact]
    public void APasteStartsStraightAway()
    {
        // Ranger's default: `Loader.add` is appendleft unless told otherwise, so a paste pushes
        // ahead of whatever is queued (core/loader.py:353-366).
        FakeFileManager manager = Ready();

        manager.Execute("paste_ext");
        QueuedTask first = manager.Tasks.Tasks[0];
        manager.Execute("paste_ext");

        Assert.NotSame(first, manager.Tasks.Tasks[0]);
    }

    [Fact]
    public void PasteExtAcceptsAppendJustAsPasteDoes()
    {
        // The one a swapped binding rests on: `paste_ext append=True` queues behind rather than
        // pushing in front.
        FakeFileManager manager = Ready();

        manager.Execute("paste_ext");
        QueuedTask first = manager.Tasks.Tasks[0];
        manager.Execute("paste_ext append=True");

        Assert.Same(first, manager.Tasks.Tasks[0]);
        Assert.Equal(2, manager.Tasks.Tasks.Count);
    }

    [Fact]
    public void PlainPasteAcceptsAppendToo()
    {
        FakeFileManager manager = Ready();

        manager.Execute("paste");
        QueuedTask first = manager.Tasks.Tasks[0];
        manager.Execute("paste append=True");

        Assert.Same(first, manager.Tasks.Tasks[0]);
    }
}
