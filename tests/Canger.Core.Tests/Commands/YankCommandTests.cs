// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.Processes;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// What <c>yp</c>, <c>yn</c>, <c>yd</c> and <c>y.</c> put on the clipboard.
/// </summary>
/// <remarks>
/// This command reported the value on the status line and never copied anything, behind a comment
/// saying the clipboard would follow "when the process runner lands". The runner landed a long
/// time before anyone pressed <c>yp</c> and found nothing in the clipboard.
/// </remarks>
public class YankCommandTests
{
    private static FakeFileManager Manager() =>
        new(new InMemoryFileSystem()
                .AddDirectory("/home/pictures")
                .AddFile("/home/notes.txt")
                .AddFile("/home/archive.tar.gz"),
            "/home");

    private static FakeFileManager.RecordingClipboard Clipboard(FakeFileManager manager) =>
        (FakeFileManager.RecordingClipboard)manager.Clipboard;

    private static void PutCursorOn(FakeFileManager manager, string name) =>
        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == name));

    [Fact]
    public void YankPath_CopiesTheWholePath()
    {
        // The report that started this: yank path on a folder put nothing anywhere.
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "pictures");

        manager.Execute("yank path");

        Assert.Equal("/home/pictures", Clipboard(manager).Last);
    }

    [Fact]
    public void YankName_CopiesJustTheName()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "notes.txt");

        manager.Execute("yank name");

        Assert.Equal("notes.txt", Clipboard(manager).Last);
    }

    [Fact]
    public void Yank_WithNoModeIsTheName()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "notes.txt");

        manager.Execute("yank");

        Assert.Equal("notes.txt", Clipboard(manager).Last);
    }

    [Fact]
    public void YankDir_CopiesTheContainingDirectory()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "notes.txt");

        manager.Execute("yank dir");

        Assert.Equal("/home", Clipboard(manager).Last);
    }

    [Fact]
    public void YankNameWithoutExtension_DropsTheExtension()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "notes.txt");

        manager.Execute("yank name_without_extension");

        Assert.Equal("notes", Clipboard(manager).Last);
    }

    [Fact]
    public void Yank_PutsAMarkedSelectionOneEntryPerLine()
    {
        // So the result can be pasted into a shell or an editor as a list.
        FakeFileManager manager = Manager();

        foreach (FsNode entry in manager.CurrentTab.Current.Entries)
        {
            entry.IsMarked = entry.Basename != "pictures";
        }

        manager.Execute("yank path");

        Assert.Equal("/home/archive.tar.gz\n/home/notes.txt", Clipboard(manager).Last);
    }

    [Fact]
    public void Yank_SaysWhichProgramTookIt()
    {
        FakeFileManager manager = Manager();
        PutCursorOn(manager, "notes.txt");

        manager.Execute("yank name");

        Assert.Contains(manager.Messages,
                        m => !m.IsError && m.Message.Contains("xclip", StringComparison.Ordinal));
    }

    [Fact]
    public void Yank_ComplainsWhenNoClipboardProgramIsInstalled()
    {
        // Ranger fails silently here, which looks exactly like the yank having worked.
        FakeFileManager manager = Manager();
        Clipboard(manager).Helper = null;
        PutCursorOn(manager, "notes.txt");

        manager.Execute("yank name");

        Assert.Contains(manager.Messages,
                        m => m.IsError && m.Message.Contains("no clipboard program",
                                                             StringComparison.Ordinal));
    }

    [Fact]
    public void KnownHelpers_ArePreferredInRangersOrder()
    {
        // pbcopy first for macOS, then X11, then Wayland — config/commands.py:2078.
        Assert.Equal(["pbcopy", "xclip", "xsel", "wl-copy"], SystemClipboard.KnownHelpers);
    }
}
