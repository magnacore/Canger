// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.TestSupport;

namespace Canger.Core.Tests.Commands;

/// <summary>
/// Completing the program itself at the front of a <c>:shell</c> line.
/// </summary>
/// <remarks>
/// <para>
/// Reported from a directory holding <c>015.cs</c> and <c>015.py</c>: typing <c>:shell ./0</c> and
/// pressing Tab completed nothing. The first word of the line is matched against the programs on
/// the <c>PATH</c>, and <c>./0</c> is not the start of any of them — nor could it be, since a name
/// carrying a separator is never looked up on the <c>PATH</c> at all.
/// </para>
/// <para>
/// Ranger has the same hole: its <c>shell.tab</c> offers <c>get_executables()</c> for the first
/// word and nothing else (<c>config/commands.py:326-329</c>). Completing a path there is a
/// deliberate step past ranger.
/// </para>
/// <para>
/// Only what can actually be run is offered — directories, to carry on into, and files with an
/// execute bit. The position names the program, and a file without one fails there whatever is
/// typed; the reported directory holds thirty notebooks beside the two scripts, and offering all
/// thirty-two would have buried the answer.
/// </para>
/// <para>
/// Against the real filesystem: completion reads directories directly, and an execute bit is not
/// something the in-memory filesystem carries.
/// </para>
/// </remarks>
public sealed class ShellProgramCompletionTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-program-" + Path.GetRandomFileName());

    public ShellProgramCompletionTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Join(_root, "02 notes"));
        Script("015.cs");
        Script("015.py");
        File.WriteAllText(Path.Join(_root, "016.ipynb"), string.Empty);
    }

    /// <summary>Writes a file and gives it an execute bit, as a runnable script has.</summary>
    private void Script(string name)
    {
        string path = Path.Join(_root, name);

        File.WriteAllText(path, "#!/bin/sh\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                   UnixFileMode.UserExecute);
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
    public void CompletesAScriptInTheDirectoryOnScreen()
    {
        // The reported case.
        Assert.Equal(["shell ./015.cs", "shell ./015.py"], Complete("shell ./01"));
    }

    [Fact]
    public void LeavesOutWhatCannotBeRun()
    {
        Assert.Empty(Complete("shell ./016"));
    }

    [Fact]
    public void OffersADirectoryToCarryOnInto()
    {
        Assert.Equal(["shell './02 notes/'"], Complete("shell ./02"));
    }

    [Fact]
    public void CompletesAnAbsolutePathToo()
    {
        Assert.Equal([$"shell {_root}/015.cs", $"shell {_root}/015.py"],
                     Complete($"shell {_root}/01"));
    }

    [Fact]
    public void KeepsTheFlagsThatCameBeforeIt()
    {
        Assert.Equal(["shell -w ./015.cs", "shell -w ./015.py"], Complete("shell -w ./01"));
    }

    [Fact]
    public void AProgramWithoutASeparatorStillComesFromThePath()
    {
        // `uname` rather than `true`, which is a shell builtin on some systems and not a file.
        Assert.Contains("shell uname", Complete("shell unam"));
    }

    [Fact]
    public void AProgramWithoutASeparatorIsNotLookedForHere()
    {
        // A bare `015.cs` is not what the shell would run — there is no `.` on the PATH — so the
        // listing is not searched for it. Ranger does the same.
        Assert.Empty(Complete("shell 015"));
    }
}
