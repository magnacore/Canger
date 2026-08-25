// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// State that could not be read must never be written over.
/// </summary>
/// <remarks>
/// <para>
/// The same mistake in three places: a read failure produced an empty result, indistinguishable
/// from an empty file, and the next save wrote that emptiness over everything the user had. Tags
/// cleared before it knew it could read; bookmarks merged onto an empty dictionary and so dropped
/// every key not touched this session; the metadata database was replaced wholesale by whatever
/// one command had just set.
/// </para>
/// <para>
/// The trigger is ordinary: a state file left root-owned by one <c>sudo canger</c>, or any
/// EACCES, EIO or EMFILE. The directory stays writable, so the rename succeeds and the loss is
/// silent. Ranger guards all three (<c>container/tags.py:74-83</c>,
/// <c>container/bookmarks.py:222-245</c>, <c>core/metadata.py:116-125</c>).
/// </para>
/// </remarks>
public sealed class UnreadableStateTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-unreadable-" + Path.GetRandomFileName());

    public UnreadableStateTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (string file in Directory.EnumerateFiles(_root))
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Makes a file unreadable the way a root-owned one is.</summary>
    private static void MakeUnreadable(string path) => File.SetUnixFileMode(path, UnixFileMode.None);

    [Fact]
    public void Tags_KeepEverythingWhenTheFileCannotBeRead()
    {
        string path = Path.Join(_root, "tagged");
        File.WriteAllLines(path, ["/home/me/one.txt", "w:/home/me/two.txt", "/home/me/three.txt"]);
        string before = File.ReadAllText(path);

        MakeUnreadable(path);

        Tags tags = new(path);
        tags.Toggle(["/home/me/four.txt"]);

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Tags_StillWorkWhenTheFileCanBeRead()
    {
        // The refusal must not be a refusal to work at all.
        string path = Path.Join(_root, "tagged");
        File.WriteAllLines(path, ["/home/me/one.txt"]);

        Tags tags = new(path);
        tags.Toggle(["/home/me/two.txt"]);

        Assert.Contains("/home/me/two.txt", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("/home/me/one.txt", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Bookmarks_KeepEverythingWhenTheFileCannotBeRead()
    {
        string path = Path.Join(_root, "bookmarks");
        File.WriteAllLines(path, ["h:/home/me", "w:/home/me/work", "p:/home/me/projects"]);
        string before = File.ReadAllText(path);

        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home/me");
        Bookmarks bookmarks = new(fs, path);
        bookmarks.Load();

        MakeUnreadable(path);

        bookmarks.Set('x', "/home/me/somewhere");
        bookmarks.Save();

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Bookmarks_StillSaveWhenTheFileCanBeRead()
    {
        string path = Path.Join(_root, "bookmarks");
        File.WriteAllLines(path, ["h:/home/me"]);

        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home/me");
        Bookmarks bookmarks = new(fs, path);
        bookmarks.Load();
        bookmarks.Set('w', "/home/me/work");
        bookmarks.Save();

        string saved = File.ReadAllText(path);
        Assert.Contains("h:/home/me", saved, StringComparison.Ordinal);
        Assert.Contains("w:/home/me/work", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_RefuseToWriteADatabaseItCouldNotParse()
    {
        // Hand-editable, and living among the user's own files rather than in Canger's state
        // directory — which makes it the worst of the three to have got wrong.
        string directory = Path.Join(_root, "books");
        Directory.CreateDirectory(directory);

        string database = Path.Join(directory, ".metadata.json");
        File.WriteAllText(database, """{"a.pdf": {"title": "Kept"}, "b.pdf": {"year": 1999}}""");
        string before = File.ReadAllText(database);

        MetadataManager manager = new(Canger.Core.FileSystem.LocalFileSystem.Instance);
        manager.Set(Path.Join(directory, "c.pdf"),
                    new Dictionary<string, string> { ["title"] = "New" });

        Assert.Equal(before, File.ReadAllText(database));
    }

    [Fact]
    public void Metadata_StillWritesADatabaseItCouldParse()
    {
        string directory = Path.Join(_root, "readable");
        Directory.CreateDirectory(directory);

        string database = Path.Join(directory, ".metadata.json");
        File.WriteAllText(database, """{"a.pdf": {"title": "Kept"}}""");

        MetadataManager manager = new(Canger.Core.FileSystem.LocalFileSystem.Instance);
        manager.Set(Path.Join(directory, "b.pdf"),
                    new Dictionary<string, string> { ["title"] = "Added" });

        string saved = File.ReadAllText(database);
        Assert.Contains("Kept", saved, StringComparison.Ordinal);
        Assert.Contains("Added", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_LeavesNoTemporaryFileBehind()
    {
        // The write is now temp-then-rename; the temp must not survive it.
        string directory = Path.Join(_root, "temps");
        Directory.CreateDirectory(directory);

        MetadataManager manager = new(Canger.Core.FileSystem.LocalFileSystem.Instance);
        manager.Set(Path.Join(directory, "a.pdf"),
                    new Dictionary<string, string> { ["title"] = "One" });

        Assert.False(File.Exists(Path.Join(directory, ".metadata.json.new")));
        Assert.True(File.Exists(Path.Join(directory, ".metadata.json")));
    }
}
