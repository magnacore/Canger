// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Removing files, and the confirmation that stands between the key and the loss.
/// </summary>
public class DeleteCommandTests
{
    /// <summary>A file manager over a small tree, with the cursor somewhere useful.</summary>
    private static FakeFileManager Manager(string confirmOnDelete = "multiple")
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/a.txt", "a")
            .AddFile("/home/b.txt", "b")
            .AddDirectory("/home/empty")
            .AddFile("/home/full/inside.txt", "x");

        FakeFileManager manager = new(fs, "/home");
        manager.SettingsStore.Set("confirm_on_delete", confirmOnDelete);
        return manager;
    }

    private static void Run(FakeFileManager manager, string line) => manager.Execute(line);

    /// <summary>Puts the cursor on a named entry, which is what the selection follows.</summary>
    private static void PointAt(FakeFileManager manager, string name)
    {
        DirectoryNode directory = manager.CurrentTab.Current;
        string[] names = [.. directory.Entries.Select(e => e.RelativePath)];
        directory.Cursor.MoveTo(Array.IndexOf(names, name), directory.Entries);
    }

    /// <summary>Marks a named entry, which is what makes a selection more than one thing.</summary>
    private static void Mark(FakeFileManager manager, string name) =>
        manager.CurrentTab.Current.Entries.First(e => e.RelativePath == name).IsMarked = true;

    [Fact]
    public void Delete_RemovesASingleFileWithoutAsking()
    {
        // The common case is one stray file; a question every time would only train the user to
        // press y without reading it.
        FakeFileManager manager = Manager();
        PointAt(manager, "a.txt");

        Run(manager, "delete");

        Assert.Null(manager.PendingQuestion);
        Assert.False(manager.FileSystem.Exists("/home/a.txt"));
    }

    [Fact]
    public void Delete_AsksBeforeRemovingMoreThanOneThing()
    {
        FakeFileManager manager = Manager();
        Mark(manager, "a.txt");
        Mark(manager, "b.txt");

        Run(manager, "delete");

        Assert.NotNull(manager.PendingQuestion);
        Assert.True(manager.FileSystem.Exists("/home/a.txt"));

        manager.Answer('y');
        Assert.False(manager.FileSystem.Exists("/home/a.txt"));
        Assert.False(manager.FileSystem.Exists("/home/b.txt"));
    }

    [Fact]
    public void Delete_LeavesEverythingAloneWhenTheAnswerIsNo()
    {
        FakeFileManager manager = Manager();
        Mark(manager, "a.txt");
        Mark(manager, "b.txt");

        Run(manager, "delete");
        manager.Answer('n');

        Assert.True(manager.FileSystem.Exists("/home/a.txt"));
        Assert.True(manager.FileSystem.Exists("/home/b.txt"));
    }

    [Fact]
    public void Delete_LeavesEverythingAloneWhenTheQuestionIsAbandoned()
    {
        // An abandoned question never calls back, so nothing happens — which is what makes
        // Escape safe.
        FakeFileManager manager = Manager();
        Mark(manager, "a.txt");
        Mark(manager, "b.txt");

        Run(manager, "delete");

        Assert.True(manager.FileSystem.Exists("/home/a.txt"));
    }

    [Fact]
    public void Delete_AsksBeforeRemovingADirectoryThatIsNotEmpty()
    {
        FakeFileManager manager = Manager();
        PointAt(manager, "full");

        Run(manager, "delete");

        Assert.NotNull(manager.PendingQuestion);
        Assert.True(manager.FileSystem.Exists("/home/full/inside.txt"));

        manager.Answer('y');
        Assert.False(manager.FileSystem.Exists("/home/full"));
    }

    [Fact]
    public void Delete_RemovesAnEmptyDirectoryWithoutAsking()
    {
        // Nothing is lost, so there is nothing to warn about.
        FakeFileManager manager = Manager();
        PointAt(manager, "empty");

        Run(manager, "delete");

        Assert.Null(manager.PendingQuestion);
        Assert.False(manager.FileSystem.Exists("/home/empty"));
    }

    [Fact]
    public void Delete_AlwaysAsksWhenTheSettingSaysAlways()
    {
        FakeFileManager manager = Manager("always");
        PointAt(manager, "a.txt");

        Run(manager, "delete");

        Assert.NotNull(manager.PendingQuestion);
        Assert.True(manager.FileSystem.Exists("/home/a.txt"));
    }

    [Fact]
    public void Delete_NeverAsksWhenTheSettingSaysNever()
    {
        FakeFileManager manager = Manager("never");
        PointAt(manager, "full");

        Run(manager, "delete");

        Assert.Null(manager.PendingQuestion);
        Assert.False(manager.FileSystem.Exists("/home/full"));
    }

    [Fact]
    public void Delete_NamesWhatWouldGoInTheQuestion()
    {
        FakeFileManager manager = Manager();
        Mark(manager, "a.txt");
        Mark(manager, "b.txt");

        Run(manager, "delete");

        string question = manager.PendingQuestion!.Value.Question;
        Assert.Contains("2 items", question, StringComparison.Ordinal);
        Assert.Contains("a.txt", question, StringComparison.Ordinal);
        Assert.Contains("b.txt", question, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_DefaultsToNoSoAStrayKeyCannotConfirm()
    {
        FakeFileManager manager = Manager("always");
        PointAt(manager, "a.txt");

        Run(manager, "delete");

        Assert.Equal('n', manager.PendingQuestion!.Value.Choices[0]);
    }

    [Fact]
    public void Delete_SaysSoWhenThereIsNothingToRemove()
    {
        FakeFileManager manager = new(new InMemoryFileSystem().AddDirectory("/home"), "/home");

        Run(manager, "delete");

        Assert.Contains(manager.Messages, m => m.IsError);
    }

    [Fact]
    public void Trash_HonoursConfirmOnDeleteWhenTrashSaysLikeDelete()
    {
        // One setting governs both by default, so the two cannot drift apart in practice.
        FakeFileManager manager = Manager("always");
        manager.SettingsStore.Set("confirm_on_trash", "like_delete");
        PointAt(manager, "a.txt");

        Run(manager, "trash");

        Assert.NotNull(manager.PendingQuestion);
        Assert.Empty(manager.LaunchedPrograms);
    }

    [Fact]
    public void Trash_CanBeConfiguredIndependentlyOfDelete()
    {
        FakeFileManager manager = Manager("always");
        manager.SettingsStore.Set("confirm_on_trash", "never");
        PointAt(manager, "a.txt");

        Run(manager, "trash");

        Assert.Null(manager.PendingQuestion);
        Assert.Single(manager.LaunchedPrograms);
    }
}
