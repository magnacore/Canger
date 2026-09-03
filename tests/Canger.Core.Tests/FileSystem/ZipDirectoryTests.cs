// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileSystem;

/// <summary>
/// Reading what a zip holds from the index at its end.
/// </summary>
/// <remarks>
/// Checked against archives the real tool wrote, because the point is to agree with what
/// Info-ZIP puts on disk rather than with a reading of the specification.
/// </remarks>
public sealed class ZipDirectoryTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-zipdir-" + Path.GetRandomFileName());

    public ZipDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    /// <summary>Builds a small tree and zips it; says whether the tool was there.</summary>
    private bool Build(string archive, params (string Name, int Size)[] files)
    {
        string source = Path.Join(_root, "src");
        Directory.CreateDirectory(source);

        foreach ((string name, int size) in files)
        {
            string path = Path.Join(source, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, new string('x', size));
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("zip",
                                                                        $"-q -r {archive} src")
            {
                WorkingDirectory = _root,
                UseShellExecute = false,
            });

            process?.WaitForExit();

            return process?.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    [Fact]
    public void ListsEveryEntryWithTheSizeItWillOccupy()
    {
        Assert.SkipUnless(Build("out.zip", ("a.txt", 1000), ("sub/b.txt", 2500)),
                          "zip is not installed");

        IReadOnlyList<(string Name, long Size)> entries =
            ZipDirectory.Entries(Path.Join(_root, "out.zip"));

        Assert.Equal(1000, entries.Single(e => e.Name.EndsWith("a.txt", StringComparison.Ordinal))
                                  .Size);
        Assert.Equal(2500, entries.Single(e => e.Name.EndsWith("b.txt", StringComparison.Ordinal))
                                  .Size);
    }

    [Fact]
    public void AddsUpToWhatTheArchiveWillProduce()
    {
        Assert.SkipUnless(Build("out.zip", ("a.txt", 1000), ("sub/b.txt", 2500)),
                          "zip is not installed");

        // Directory entries weigh nothing, so the total is exactly the files.
        Assert.Equal(3500, ZipDirectory.UncompressedSize(Path.Join(_root, "out.zip")));
    }

    [Fact]
    public void FindsTheIndexPastATrailingComment()
    {
        // The end record is not at a fixed position: a comment of any length may follow it, so it
        // has to be searched for backwards.
        Assert.SkipUnless(Build("out.zip", ("a.txt", 1000)), "zip is not installed");

        string archive = Path.Join(_root, "out.zip");
        byte[] original = File.ReadAllBytes(archive);

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("zip", "-z -q out.zip")
            {
                WorkingDirectory = _root,
                UseShellExecute = false,
                RedirectStandardInput = true,
            });

            Assert.SkipWhen(process is null, "zip is not installed");
            process!.StandardInput.WriteLine("a comment long enough to move the end record");
            process.StandardInput.Close();
            process.WaitForExit();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Assert.Skip("zip is not installed");
        }

        Assert.True(new FileInfo(archive).Length > original.Length, "no comment was added");
        Assert.Equal(1000, ZipDirectory.UncompressedSize(archive));
    }

    [Fact]
    public void SaysNothingAboutATruncatedArchive()
    {
        Assert.SkipUnless(Build("out.zip", ("a.txt", 1000)), "zip is not installed");

        string archive = Path.Join(_root, "out.zip");
        byte[] whole = File.ReadAllBytes(archive);
        string cut = Path.Join(_root, "cut.zip");
        File.WriteAllBytes(cut, whole[..(whole.Length / 2)]);

        Assert.Empty(ZipDirectory.Entries(cut));
        Assert.Null(ZipDirectory.UncompressedSize(cut));
    }

    [Fact]
    public void SaysNothingAboutSomethingThatIsNotAZip()
    {
        string plain = Path.Join(_root, "plain.txt");
        File.WriteAllText(plain, "not an archive");

        Assert.Empty(ZipDirectory.Entries(plain));
    }

    [Fact]
    public void SaysNothingAboutAMissingFile()
    {
        Assert.Empty(ZipDirectory.Entries(Path.Join(_root, "not-here.zip")));
    }

    [Fact]
    public void AnswersThroughTheGeneralReaderToo()
    {
        // A caller asking "how much comes out of this file" should not have to know which shape
        // it is; CompressedStreamSize hands a zip to this class.
        Assert.SkipUnless(Build("out.zip", ("a.txt", 1000), ("sub/b.txt", 2500)),
                          "zip is not installed");

        Assert.Equal(3500, CompressedStreamSize.Of(Path.Join(_root, "out.zip")));
    }
}
