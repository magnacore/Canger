// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Completing a path typed as an argument to <c>:shell</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reported as <c>:shell sudo mv canger /us&lt;Tab&gt;</c> going nowhere. Ranger does not complete
/// that either — its <c>shell.tab</c> matches the typed word against the basenames in the current
/// directory (<c>config/commands.py:342</c>), and no basename begins with a <c>/</c>, so nothing
/// can ever match. Driving ranger's own command class confirmed it returns nothing there.
/// </para>
/// <para>
/// Canger completes it anyway: the listing is simply the wrong set to search once the word carries
/// a separator, and the directory the path points at is the right one. This is a deliberate step
/// past ranger rather than a parity fix.
/// </para>
/// <para>
/// Against the real filesystem, because completion reads directories directly rather than through
/// the listing — a path being completed is usually one Canger has never loaded.
/// </para>
/// </remarks>
public sealed class ShellPathCompletionTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-shell-" + Path.GetRandomFileName());

    public ShellPathCompletionTests()
    {
        Directory.CreateDirectory(Path.Join(_root, "usr", "local"));
        Directory.CreateDirectory(Path.Join(_root, "usable"));
        Directory.CreateDirectory(Path.Join(_root, "two words"));
        File.WriteAllText(Path.Join(_root, "two words", "inside.txt"), string.Empty);
        Directory.CreateDirectory(Path.Join(_root, "sub", "alpha"));
        File.WriteAllText(Path.Join(_root, "userlist.txt"), string.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private IReadOnlyList<string> Complete(string line)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory(_root);
        FakeFileManager manager = new(fs, _root);

        return manager.Dispatcher.Build(line)?.Complete(1) ?? [];
    }

    [Fact]
    public void CompletesAnAbsolutePathInsteadOfSearchingTheListing()
    {
        // The reported case. Nothing in the current directory can begin with a `/`, so the old
        // answer was always the empty list however much of the path had been typed.
        Assert.Equal([$"shell sudo mv canger {_root}/usable/",
                      $"shell sudo mv canger {_root}/userlist.txt",
                      $"shell sudo mv canger {_root}/usr/"],
                     Complete($"shell sudo mv canger {_root}/us"));
    }

    [Fact]
    public void OffersFilesAsWellAsDirectories()
    {
        // Unlike `:cd`, a shell argument is as likely to be a file as a directory.
        Assert.Contains($"shell prog {_root}/userlist.txt",
                        Complete($"shell prog {_root}/user"), StringComparer.Ordinal);
    }

    [Fact]
    public void MarksADirectoryWithATrailingSlash()
    {
        // So the next Tab carries straight on into it without the separator being typed.
        Assert.Equal([$"shell prog {_root}/usr/local/"], Complete($"shell prog {_root}/usr/l"));
    }

    [Fact]
    public void KeepsTheTypedHeadExactlyAsItStands()
    {
        // A relative head stays relative; only the final name is filled in.
        Assert.Equal(["shell prog sub/alpha/"], Complete("shell prog sub/al"));
    }

    [Fact]
    public void QuotesTheWholeWordSoTheNextTabCanReadItBack()
    {
        // Quoting only the final name read well — `dir/'two words/'` is one word to the shell —
        // but the next Tab then had to make sense of a head with an opening quote stuck in the
        // middle of it, and nothing was ever found under `dir/'two words/`. The word is quoted as
        // a whole instead, which both the shell and this completer can read.
        Assert.Equal([$"shell prog '{_root}/two words/'"], Complete($"shell prog {_root}/two"));
    }

    [Fact]
    public void CarriesOnIntoADirectoryWhoseNameHasASpace()
    {
        // The second Tab of the case above. Splitting the line on its last space put the word
        // boundary inside the quotes, so this completed nothing however it was spelt.
        Assert.Equal([$"shell prog '{_root}/two words/inside.txt'"],
                     Complete($"shell prog '{_root}/two words/'"));
    }

    [Fact]
    public void KeepsATildeOutsideTheQuotingSoItStillExpands()
    {
        // A quoted tilde is handed to the program as a literal tilde rather than the home
        // directory. The shell expands `~'/x y/'`, because it reads the tilde prefix on its own.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string stem = "canger" + Path.GetRandomFileName().Replace(".", string.Empty,
                                                                  StringComparison.Ordinal);
        string spaced = Path.Join(home, stem + " test");

        Directory.CreateDirectory(spaced);

        try
        {
            // Typed as far as the space and no further: an unquoted space ends the word, there as
            // much as in a shell.
            string typed = "~/" + stem;

            Assert.Equal([$"~'/{Path.GetFileName(spaced)}/'"],
                         Complete($"shell prog {typed}")
                             .Select(line => line["shell prog ".Length..]));
        }
        finally
        {
            Directory.Delete(spaced, recursive: true);
        }
    }

    [Fact]
    public void TreatsATildeAsAPathRatherThanAName()
    {
        Assert.All(Complete("shell prog ~/"),
                   candidate => Assert.StartsWith("shell prog ~/", candidate,
                                                  StringComparison.Ordinal));
    }

    [Fact]
    public void OffersNothingForAPathThatDoesNotExist()
    {
        Assert.Empty(Complete($"shell prog {_root}/no/such/place/x"));
    }

    [Fact]
    public void StillCompletesAPlainNameFromTheListing()
    {
        // The word without a separator keeps ranger's behaviour, which is the listing and not the
        // filesystem — hidden files and the sort order are the ones the user is looking at.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddDirectory("/home")
            .AddFile("/home/notes.txt");
        FakeFileManager manager = new(fs, "/home");

        Assert.Equal(["shell prog notes.txt"],
                     manager.Dispatcher.Build("shell prog not")?.Complete(1) ?? []);
    }
}
