// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Copying in one running Canger and pasting in another.
/// </summary>
/// <remarks>
/// Two file managers over one buffer file, which is what two windows are. Ranger cannot do this
/// at all — its <c>copy_buffer</c> is an in-memory set — so this is a deliberate addition, and it
/// is off unless <c>shared_copy_buffer</c> says otherwise.
/// </remarks>
public sealed class SharedCopyBufferAcrossInstancesTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-share-" + Path.GetRandomFileName());

    private string BufferFile => Path.Join(_root, "copybuffer");

    public SharedCopyBufferAcrossInstancesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A window: its own file manager, sharing one buffer file with the others.</summary>
    private FakeFileManager Window(bool sharing = true)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/from")
            .AddDirectory("/home/to")
            .AddFile("/home/from/one.txt", "one")
            .AddFile("/home/from/two.txt", "two");

        FakeFileManager manager = new(fs, "/home/from")
        {
            SharedCopyBuffer = new SharedCopyBuffer(BufferFile),
        };

        manager.SettingsStore.SetFromText("shared_copy_buffer", sharing ? "true" : "false");
        return manager;
    }

    private static void Copy(FakeFileManager window, bool cut, params string[] names)
    {
        window.SetCopyBufferPaths([.. names.Select(n => "/home/from/" + n)], cut);
    }

    [Fact]
    public void ACopyInOneWindowIsPastedInAnother()
    {
        // The whole point of the feature.
        FakeFileManager a = Window();
        FakeFileManager b = Window();

        Copy(a, cut: false, "one.txt");
        Assert.True(b.RefreshSharedCopyBuffer());

        Assert.Equal(["/home/from/one.txt"], b.CopyBuffer);
        Assert.False(b.IsCutPending);
    }

    [Fact]
    public void ACutInOneWindowArrivesAsACutInAnother()
    {
        FakeFileManager a = Window();
        FakeFileManager b = Window();

        Copy(a, cut: true, "one.txt", "two.txt");
        Assert.True(b.RefreshSharedCopyBuffer());

        Assert.Equal(["/home/from/one.txt", "/home/from/two.txt"], b.CopyBuffer);
        Assert.True(b.IsCutPending);
    }

    [Fact]
    public void PastingACutEmptiesTheBufferForEveryWindow()
    {
        // Otherwise the window that cut the files would still show them dimmed, and pasting there
        // too would try to move what has already moved.
        FakeFileManager a = Window();
        FakeFileManager b = Window();

        Copy(a, cut: true, "one.txt");
        b.RefreshSharedCopyBuffer();

        b.Execute("paste");

        Assert.Empty(b.CopyBuffer);
        Assert.True(a.RefreshSharedCopyBuffer());
        Assert.Empty(a.CopyBuffer);
        Assert.False(a.IsCutPending);
    }

    [Fact]
    public void PastingACopyLeavesItAvailableToEveryWindow()
    {
        // A copy can be pasted again, here and elsewhere.
        FakeFileManager a = Window();
        FakeFileManager b = Window();

        Copy(a, cut: false, "one.txt");
        b.RefreshSharedCopyBuffer();
        b.Execute("paste");

        Assert.Equal(["/home/from/one.txt"], b.CopyBuffer);
        Assert.Null(a.SharedCopyBuffer!.ReadIfChanged());
    }

    [Fact]
    public void TheLastWindowToCopyWins()
    {
        // Two windows copying at once is a clipboard, not a merge: whoever went last is what
        // gets pasted.
        FakeFileManager a = Window();
        FakeFileManager b = Window();
        FakeFileManager c = Window();

        Copy(a, cut: false, "one.txt");
        Copy(b, cut: true, "two.txt");

        Assert.True(c.RefreshSharedCopyBuffer());
        Assert.Equal(["/home/from/two.txt"], c.CopyBuffer);
        Assert.True(c.IsCutPending);
    }

    [Fact]
    public void NothingIsSharedWhenTheSettingIsOff()
    {
        // The default, and it must be complete: nothing written, nothing read.
        FakeFileManager a = Window(sharing: false);
        FakeFileManager b = Window(sharing: false);

        Copy(a, cut: false, "one.txt");

        Assert.False(File.Exists(BufferFile));
        Assert.False(b.RefreshSharedCopyBuffer());
        Assert.Empty(b.CopyBuffer);
    }

    [Fact]
    public void AWindowDoesNotReReadItsOwnCopy()
    {
        // Otherwise every copy would rebuild the buffer a second time for no reason.
        FakeFileManager a = Window();

        Copy(a, cut: false, "one.txt");

        Assert.False(a.RefreshSharedCopyBuffer());
        Assert.Equal(["/home/from/one.txt"], a.CopyBuffer);
    }

    [Fact]
    public void ABufferLeftBehindByAClosedWindowIsStillThere()
    {
        // Decided deliberately: the buffer behaves like a clipboard and outlives the windows, so
        // a cut is still a cut tomorrow.
        Window().SetCopyBufferPaths(["/home/from/one.txt"], cut: true);

        FakeFileManager later = Window();
        Assert.True(later.RefreshSharedCopyBuffer());

        Assert.Equal(["/home/from/one.txt"], later.CopyBuffer);
        Assert.True(later.IsCutPending);
    }
}
