// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;
using Canger.Vcs;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// Where the version-control mark sits in a row.
/// </summary>
/// <remarks>
/// Against a real repository, because the mark only appears once a repository has been found and
/// refreshed, and a fake that skipped either would test the drawing of something that never
/// happens. These exist because the mark was drawn on the <em>left</em>, before the name, and
/// every one of the 1,566 tests passed: the table was pinned character by character and its
/// position was not pinned at all.
/// </remarks>
public sealed class VcsMarkerPlacementTests : IDisposable
{
    private const int Width = 40;

    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-vcsrow-" + Path.GetRandomFileName());

    private static bool GitIsInstalled =>
        Environment.GetEnvironmentVariable("PATH")?.Split(':')
            .Any(d => File.Exists(Path.Join(d, "git"))) ?? false;

    public VcsMarkerPlacementTests()
    {
        Directory.CreateDirectory(_root);

        if (!GitIsInstalled)
        {
            return;
        }

        Git("init", "-q", "-b", "main", ".");
        Git("config", "user.email", "test@example.invalid");
        Git("config", "user.name", "Test");
        File.WriteAllText(Path.Join(_root, "kept.txt"), "clean\n");
        Git("add", "kept.txt");
        Git("commit", "-q", "-m", "first");
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private void Git(params string[] arguments)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = _root };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(start);
        process?.WaitForExit();
    }

    /// <summary>The row for the one tracked file, drawn with version control on.</summary>
    private string Row()
    {
        LocalFileSystem fs = new();
        DirectoryNode directory = new(fs, _root, fs.GetStatus(_root, followSymbolicLinks: true));
        directory.Load(TestContext.Current.CancellationToken);

        VcsService vcs = new();
        vcs.RepositoryFor(_root)?.Refresh();

        BrowserColumn column = new(new DefaultColorScheme())
        {
            Directory = directory,
            IsMainColumn = true,
            ShowSize = true,
            Vcs = vcs,
        };
        column.Layout(new Rect(0, 0, Width, 6));

        ScreenBuffer screen = new(Width, 6);
        column.Render(screen);

        int row = Enumerable.Range(0, 6)
            .First(r => screen.TextAt(r).Contains("kept.txt", StringComparison.Ordinal));

        return screen.TextAt(row);
    }

    [Fact]
    public void TheMarkIsAtTheEndOfTheRow()
    {
        // Ranger puts the marks on `predisplay_right` and prepends the size to them
        // (browsercolumn.py:400-423), so the row reads: name, gap, size, gap, mark.
        Assert.SkipUnless(GitIsInstalled, "git is not installed");

        string row = Row();

        Assert.EndsWith("✓", row.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkIsNotBesideTheName()
    {
        // The defect: it was drawn between the line number and the name, so the name started one
        // or two columns further right and the mark led the row instead of ending it.
        Assert.SkipUnless(GitIsInstalled, "git is not installed");

        string row = Row();
        int mark = row.IndexOf('✓', StringComparison.Ordinal);
        int name = row.IndexOf("kept.txt", StringComparison.Ordinal);

        Assert.True(mark > name, "the mark should follow the name: " + row);
    }
}
