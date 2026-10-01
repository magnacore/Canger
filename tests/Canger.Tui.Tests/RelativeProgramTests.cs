// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using Canger.Core.Processes;
using Canger.Tui;

namespace Canger.Tui.Tests;

/// <summary>
/// Running a program named by a path relative to the directory being browsed.
/// </summary>
/// <remarks>
/// Reported: <c>:shell ./166</c> on a script in the current folder failed with "An error occurred
/// trying to start process './166'", where ranger runs it. Ranger hands every line to
/// <c>sh -c</c>, and the shell resolves <c>./166</c> against its own working directory.
/// </remarks>
public sealed class RelativeProgramTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-relative-" + Path.GetRandomFileName());

    public RelativeProgramTests()
    {
        Directory.CreateDirectory(_root);

        // Three shapes of the same thing. The language is not the point — the report named a C#
        // script, but what failed was the `./`, so a shell script and a python one fail alike —
        // and nor is the leading dot: a program named through a subdirectory is as relative.
        Write(Path.Join(_root, "166"), "#!/bin/sh\necho ran\n");
        Write(Path.Join(_root, "counter.py"), "#!/usr/bin/env python3\nprint('ran')\n");

        Directory.CreateDirectory(Path.Join(_root, "bin"));
        Write(Path.Join(_root, "bin", "deeper"), "#!/bin/sh\necho ran\n");
    }

    private static void Write(string path, string text)
    {
        File.WriteAllText(path, text);
        File.SetUnixFileMode(path,
                             UnixFileMode.UserRead | UnixFileMode.UserWrite |
                             UnixFileMode.UserExecute | UnixFileMode.GroupRead |
                             UnixFileMode.OtherRead);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void DotNetResolvesARelativeProgramAgainstItsOwnDirectoryNotTheOneAskedFor()
    {
        // The measurement the fix rests on. ProcessStartInfo.WorkingDirectory is where the child
        // starts, but the program name is resolved before that, against the directory the parent
        // happens to be in — so a relative path that plainly exists where the user is looking is
        // not found.
        ProcessStartInfo start = new()
        {
            FileName = "./166",
            WorkingDirectory = _root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };

        Assert.ThrowsAny<Exception>(() => Process.Start(start));
    }

    [Theory]
    // What was reported, with the dot.
    [InlineData("./166")]
    // The same script with an argument, since the splitter and the resolution meet there.
    [InlineData("./166 --verbose")]
    // Another language, because the report named a C# script and the language was never the point.
    [InlineData("./counter.py")]
    // Relative without a leading dot, which is as relative.
    [InlineData("bin/deeper")]
    public void TheRunnerRunsAProgramNamedRelativeToWhereItRuns(string line)
    {
        TerminalProcessRunner runner = new();

        ProcessResult result = runner.Run(new ProcessRequest(line, new ProcessFlags("s"), _root));

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AProgramOnThePathIsStillFoundOnThePath()
    {
        // The resolution must not take PATH lookup away: a name with no directory in it is the
        // ordinary case and is how every binding calls a tool.
        //
        // `uname` rather than `true`, which was the first choice and pinned nothing: `true` is a
        // shell builtin, so that line never takes the direct path this test is about. The control
        // is what said so — the mutation that makes every name absolute left the test passing.
        TerminalProcessRunner runner = new();

        ProcessResult result = runner.Run(new ProcessRequest("uname", new ProcessFlags("s"), _root));

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AnAbsoluteProgramIsLeftAlone()
    {
        TerminalProcessRunner runner = new();

        ProcessResult result = runner.Run(
            new ProcessRequest("/bin/sh -c 'exit 0'", new ProcessFlags("s"), _root));

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
    }
}
