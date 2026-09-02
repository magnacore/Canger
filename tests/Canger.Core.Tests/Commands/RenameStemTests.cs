// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands.Builtin;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Opening the rename prompt with the extension kept and the name cleared.
/// </summary>
/// <remarks>
/// <c>cw</c> clears the whole name, so renaming a file typed out as
/// <c>2024-01-07_15-06-24-part-005-019r-021p.mkv</c> means typing <c>.mkv</c> back for no reason,
/// and <c>a</c> keeps the name so the stem has to be deleted by hand. Ranger has neither; this is
/// a deliberate addition.
/// </remarks>
public class RenameStemTests
{
    /// <summary>Where the cursor sits, written with a bar so the intent is readable.</summary>
    private static string Prompt(string filename)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/" + filename);

        FakeFileManager manager = new(fs, "/home");

        // Dotfiles are filtered out by default, and `.bashrc` is one of the cases under test.
        manager.CurrentTab.Current.ShowHidden = true;

        manager.CurrentTab.MoveCursorTo(
            manager.CurrentTab.Current.Entries.First(e => e.Basename == filename));

        manager.Execute("rename_stem");

        (string Text, int Cursor) opened = manager.ConsoleOpenings[^1];
        return opened.Text[..opened.Cursor] + "|" + opened.Text[opened.Cursor..];
    }

    [Fact]
    public void ClearsTheNameAndKeepsTheExtension()
    {
        Assert.Equal("rename |.txt", Prompt("demo.txt"));
    }

    [Fact]
    public void HandlesANameFullOfDotsDashesAndDigits()
    {
        // The reported case, and the one the `a` then Ctrl-W trick cannot do: a backward word
        // delete stops at the first separator and leaves most of the name behind.
        Assert.Equal("rename |.mkv",
                     Prompt("2024-01-07_15-06-24-part-005-019r-021p.mkv"));
    }

    [Theory]
    [InlineData("archive.tar.gz", "rename |.tar.gz")]
    [InlineData("archive.tar.lz", "rename |.tar.lz")]
    [InlineData("archive.tar.bz2", "rename |.tar.bz2")]
    [InlineData("archive.TAR.ZST", "rename |.TAR.ZST")]
    public void KeepsBothHalvesOfACompoundExtension(string filename, string expected)
    {
        // Taking the last dot alone would leave `archive.tar` behind, which is useless on exactly
        // the files this is most wanted for.
        Assert.Equal(expected, Prompt(filename));
    }

    [Fact]
    public void KeepsTheCaseOfTheExtension()
    {
        // `FsNode.Extension` lowercases, which is right for matching a rule and wrong for putting
        // the text back on screen.
        Assert.Equal("rename |.MKV", Prompt("holiday.MKV"));
    }

    [Theory]
    [InlineData("README")]
    [InlineData(".bashrc")]
    [InlineData("notes.")]
    public void OffersAnEmptyNameWhenThereIsNoExtension(string filename)
    {
        // A leading dot does not start an extension and a trailing dot ends nothing — ranger's
        // rule, which `FsNode.Extension` already follows.
        Assert.Equal("rename |", Prompt(filename));
    }

    [Fact]
    public void OnlyTarIsCompound()
    {
        // `.tar.gz` is one extension; `a.b.c` is not.
        Assert.Equal("rename |.c", Prompt("a.b.c"));
        Assert.Equal(".tar", RenameStemCommand.ExtensionOf("backup.tar"));
    }

    [Fact]
    public void DoublesAPerCentSoItSurvivesTheSecondExpansion()
    {
        Assert.Equal("rename |.%%zip", Prompt("odd.%zip"));
    }
}
