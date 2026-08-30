// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.TestSupport;

namespace Canger.Core.Tests.Model;

/// <summary>
/// Noticing that the directory on screen has been removed.
/// </summary>
/// <remarks>
/// <see cref="DirectoryNode.LoadIfOutdated"/> already stats the directory on every draw to decide
/// whether to re-read it, so noticing costs nothing that was not being paid. What it did not do
/// was distinguish the two ways that stat can fail — and they call for opposite answers.
/// </remarks>
public class VanishedNoticeTests
{
    private static (DirectoryNode Node, InMemoryFileSystem Fs) Build()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home/manuj")
            .AddFile("/home/manuj/Test/notes.md", "hello");

        DirectoryNode node = new DirectoryCache(fs).Get("/home/manuj/Test");
        node.Load(TestContext.Current.CancellationToken);

        return (node, fs);
    }

    [Fact]
    public void ADirectoryThatIsStillThereHasNotVanished()
    {
        (DirectoryNode node, _) = Build();

        node.LoadIfOutdated(TestContext.Current.CancellationToken);

        Assert.False(node.HasVanished);
    }

    [Fact]
    public void ADeletedDirectoryIsNoticed()
    {
        (DirectoryNode node, InMemoryFileSystem fs) = Build();
        fs.DeleteRecursive("/home/manuj/Test");

        node.LoadIfOutdated(TestContext.Current.CancellationToken);

        Assert.True(node.HasVanished);
    }

    [Fact]
    public void ADirectoryThatMerelyCannotBeReadHasNotVanished()
    {
        // The safety property. A network share that has stopped answering, or a directory whose
        // parent has had its search permission taken away, fails the same stat — and it will come
        // back. Being moved out of it would be the surprise, and the listing already on screen is
        // more use than an empty one.
        (DirectoryNode node, InMemoryFileSystem fs) = Build();
        fs.MakeInaccessible("/home/manuj/Test");

        node.LoadIfOutdated(TestContext.Current.CancellationToken);

        Assert.False(node.HasVanished);
    }

    [Fact]
    public void ADirectoryThatComesBackIsNoLongerConsideredGone()
    {
        // Belt and braces: the flag is a fact about now, not a verdict recorded once.
        (DirectoryNode node, InMemoryFileSystem fs) = Build();
        fs.DeleteRecursive("/home/manuj/Test");
        node.LoadIfOutdated(TestContext.Current.CancellationToken);

        fs.AddFile("/home/manuj/Test/notes.md", "hello again");
        node.LoadIfOutdated(TestContext.Current.CancellationToken);

        Assert.False(node.HasVanished);
    }
}
