// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Something claiming a kind of file before the ordinary rules are asked.
/// </summary>
/// <remarks>
/// This exists so that a plugin need not take a key binding to claim a file type. Binding
/// <c>&lt;CR&gt;</c> and <c>&lt;RIGHT&gt;</c> to a plugin's command was the first attempt, and
/// removing the plugin then left the configuration naming a command that no longer existed —
/// neither key would open anything, not a file and not a folder. A plugin has to be removable
/// without taking navigation with it.
/// </remarks>
public class FileOpenerTests
{
    private static FakeFileManager Build()
    {
        InMemoryFileSystem files = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddDirectory("/home/audio")
            .AddFile("/home/audio/book.mka");

        FakeFileManager manager = new(files, "/home/audio");
        manager.CurrentTab.MoveCursor(0);

        return manager;
    }

    /// <summary>Whether the ordinary rules were reached.</summary>
    private static bool Opened(FakeFileManager manager) => manager.RecordedOpens.Opened.Count > 0;

    [Fact]
    public void OneThatClaimsTheFileStopsTheOrdinaryRules()
    {
        FakeFileManager manager = Build();
        List<string> claimed = [];

        manager.FileOpeners.Add(paths => { claimed.AddRange(paths); return true; });

        manager.Execute("open");

        Assert.Equal(["/home/audio/book.mka"], claimed);
        Assert.False(Opened(manager), "the file was opened as well as claimed");
    }

    [Fact]
    public void OneThatDeclinesLeavesTheFileToTheOrdinaryRules()
    {
        FakeFileManager manager = Build();

        manager.FileOpeners.Add(_ => false);

        manager.Execute("open");

        Assert.True(Opened(manager), "declining left the file unopened");
    }

    [Fact]
    public void NoneAtAllIsTheOrdinaryBehaviour()
    {
        // The property that matters when a plugin is removed: everything works as before.
        FakeFileManager manager = Build();

        manager.Execute("open");

        Assert.True(Opened(manager));
    }

    [Fact]
    public void TheFirstToClaimItEndsTheMatter()
    {
        FakeFileManager manager = Build();
        int asked = 0;

        manager.FileOpeners.Add(_ => { asked++; return true; });
        manager.FileOpeners.Add(_ => { asked++; return true; });

        manager.Execute("open");

        Assert.Equal(1, asked);
    }

    [Fact]
    public void ANamedProgramIsObeyedRatherThanIntercepted()
    {
        // `:open_with mpv` chose its program deliberately; something quietly taking the file
        // instead would be the opposite of what was asked.
        FakeFileManager manager = Build();
        bool asked = false;

        manager.FileOpeners.Add(_ => { asked = true; return true; });

        manager.Execute("open_with mpv");

        Assert.False(asked, "a named program was intercepted");
    }
}
