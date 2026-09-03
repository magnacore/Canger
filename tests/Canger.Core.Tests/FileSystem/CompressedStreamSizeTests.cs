// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileSystem;

/// <summary>
/// Reading a compressed file's declared uncompressed size.
/// </summary>
/// <remarks>
/// Written against real archives made by the real tools rather than against hand-built bytes,
/// because the point of this class is to agree with what gzip, lzip and xz actually write. Every
/// case is checked against the size of the input that went in, which is the figure tar will count
/// up to while unpacking.
/// </remarks>
public sealed class CompressedStreamSizeTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-sizes-" + Path.GetRandomFileName());

    public CompressedStreamSizeTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Runs a compressor, and says whether it was there to run.</summary>
    private bool Compress(string tool, string arguments, string output)
    {
        try
        {
            using FileStream target = File.Create(Path.Join(_root, output));
            using Process? process = Process.Start(new ProcessStartInfo(tool, arguments)
            {
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            process.StandardOutput.BaseStream.CopyTo(target);
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <summary>Writes compressible input, so the archive is nothing like the size of the data.</summary>
    private string Input(int copies = 40_000)
    {
        string path = Path.Join(_root, "input");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("compressible text\n", copies)));

        return path;
    }

    [Theory]
    [InlineData("lzip", "-c input")]
    [InlineData("gzip", "-c input")]
    [InlineData("xz", "-c input")]
    public void ReadsTheSizeTheCompressorRecorded(string tool, string arguments)
    {
        long expected = new FileInfo(Input()).Length;
        Assert.SkipUnless(Compress(tool, arguments, "out"), $"{tool} is not installed");

        string archive = Path.Join(_root, "out");

        // The whole point: the file on disk is nowhere near what comes out of it, so a caller
        // using the archive's own size would be wrong by the compression ratio.
        Assert.True(new FileInfo(archive).Length < expected / 10);
        Assert.Equal(expected, CompressedStreamSize.Of(archive));
    }

    [Fact]
    public void SumsEveryMemberOfAConcatenatedLzipFile()
    {
        // Two members appended is a valid lzip file, and the trailer describes only the last one.
        // Walking the chain to the start is what gets this right; measured against `lzip -l`.
        long one = new FileInfo(Input()).Length;
        Assert.SkipUnless(Compress("lzip", "-c input", "one.lz"), "lzip is not installed");

        string pair = Path.Join(_root, "two.lz");
        byte[] member = File.ReadAllBytes(Path.Join(_root, "one.lz"));
        File.WriteAllBytes(pair, [.. member, .. member]);

        Assert.Equal(one * 2, CompressedStreamSize.Of(pair));
    }

    [Fact]
    public void SumsEveryBlockOfAThreadedXzFile()
    {
        // Compressing across threads writes several blocks into one stream, each with its own
        // index record. Reading only the first would report a fraction of the file.
        long expected = new FileInfo(Input(400_000)).Length;
        Assert.SkipUnless(Compress("xz", "-c -T4 --block-size=65536 input", "out.xz"),
                          "xz is not installed");

        Assert.Equal(expected, CompressedStreamSize.Of(Path.Join(_root, "out.xz")));
    }

    [Fact]
    public void SaysNothingAboutAFormatThatRecordsNoSize()
    {
        // bzip2 stores no uncompressed size anywhere, and guessing one is what this class exists
        // to avoid. Unpacking such an archive shows bytes.
        Input();
        Assert.SkipUnless(Compress("bzip2", "-c input", "out.bz2"), "bzip2 is not installed");

        Assert.Null(CompressedStreamSize.Of(Path.Join(_root, "out.bz2")));
    }

    [Fact]
    public void SaysNothingAboutAnUncompressedFile()
    {
        Assert.Null(CompressedStreamSize.Of(Input()));
    }

    [Fact]
    public void SurvivesATruncatedArchive()
    {
        // A half-copied download is a file like any other, and must not throw at the caller.
        Input();
        Assert.SkipUnless(Compress("lzip", "-c input", "whole.lz"), "lzip is not installed");

        byte[] whole = File.ReadAllBytes(Path.Join(_root, "whole.lz"));
        string cut = Path.Join(_root, "cut.lz");
        File.WriteAllBytes(cut, whole[..(whole.Length / 2)]);

        Assert.Null(CompressedStreamSize.Of(cut));
    }

    [Fact]
    public void SaysNothingAboutAMissingFile()
    {
        Assert.Null(CompressedStreamSize.Of(Path.Join(_root, "not-here.lz")));
    }

    [Fact]
    public void SaysNothingAboutAnEmptyFile()
    {
        string empty = Path.Join(_root, "empty.lz");
        File.WriteAllBytes(empty, []);

        Assert.Null(CompressedStreamSize.Of(empty));
    }
}
