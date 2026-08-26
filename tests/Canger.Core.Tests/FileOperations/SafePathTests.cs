// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// The names a clash produces, over a run of copies rather than just the first one.
/// </summary>
/// <remarks>
/// Every test here used to check a single collision, which is the one case where ranger's scheme
/// looks fine. Pasting the same file four times is what showed the problem: three of the four
/// copies were numbered and the odd one out was the oldest.
/// </remarks>
public class SafePathTests
{
    [Fact]
    public void KeepingExtension_NumbersEveryCopyFromZero()
    {
        // The reported sequence. It used to be notes_, notes_0, notes_1, notes_2.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/notes.md");
        List<string> made = [];

        for (int i = 0; i < 4; i++)
        {
            string next = SafePath.MakeUniqueKeepingExtension(fs, "/home/notes.md");
            fs.AddFile(next);
            made.Add(next);
        }

        Assert.Equal(
            ["/home/notes_0.md", "/home/notes_1.md", "/home/notes_2.md", "/home/notes_3.md"],
            made);
    }

    [Fact]
    public void Plain_NumbersEveryCopyFromZero()
    {
        // The same rule for the variant that puts the suffix on the very end.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/notes.md");
        List<string> made = [];

        for (int i = 0; i < 3; i++)
        {
            string next = SafePath.MakeUnique(fs, "/home/notes.md");
            fs.AddFile(next);
            made.Add(next);
        }

        Assert.Equal(["/home/notes.md_0", "/home/notes.md_1", "/home/notes.md_2"], made);
    }

    [Fact]
    public void AFreeNameIsLeftAlone()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/home");

        Assert.Equal("/home/notes.md", SafePath.MakeUnique(fs, "/home/notes.md"));
        Assert.Equal("/home/notes.md",
                     SafePath.MakeUniqueKeepingExtension(fs, "/home/notes.md"));
    }

    [Fact]
    public void KeepingExtension_LeavesADotfileWhole()
    {
        // A leading dot does not begin an extension, so .bashrc must not become _0.bashrc.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/home/.bashrc");

        Assert.Equal("/home/.bashrc_0",
                     SafePath.MakeUniqueKeepingExtension(fs, "/home/.bashrc"));
    }

    [Fact]
    public void KeepingExtension_SkipsNamesAlreadyTaken()
    {
        // Copies made before this change are still there, and must not be written over.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/notes.md")
            .AddFile("/home/notes_0.md")
            .AddFile("/home/notes_1.md");

        Assert.Equal("/home/notes_2.md",
                     SafePath.MakeUniqueKeepingExtension(fs, "/home/notes.md"));
    }

    [Fact]
    public void KeepingExtension_IgnoresTheOldBareUnderscoreName()
    {
        // notes_.md left over from the previous scheme is a different name, so numbering starts
        // at zero beside it rather than treating it as the zeroth copy.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/home/notes.md")
            .AddFile("/home/notes_.md");

        Assert.Equal("/home/notes_0.md",
                     SafePath.MakeUniqueKeepingExtension(fs, "/home/notes.md"));
    }
}
