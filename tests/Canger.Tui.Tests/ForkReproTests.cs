// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;

namespace Canger.Tui.Tests;

/// <summary>
/// That a forked program does not crash the command that started it.
/// </summary>
/// <remarks>
/// <c>ProcessResult</c> was a positional record struct with <c>string Output = ""</c>. A record
/// struct's primary-constructor defaults do not apply to <c>new T()</c> — that zero-initialises —
/// so the fork and new-terminal paths, which return <c>new ProcessResult()</c>, left
/// <c>Output</c> null and the caller's <c>result.Output.Length</c> threw. Every <c>shell -f</c>
/// binding in a real configuration failed with "Object reference not set to an instance of an
/// object", which says nothing at all about the cause.
/// </remarks>
public class ProcessResultTests
{
    [Fact]
    public void DefaultConstructed_HasEmptyOutputRatherThanNull()
    {
        ProcessResult forked = new();

        Assert.Equal(string.Empty, forked.Output);
        Assert.Equal(0, forked.Output.Length);
    }

    [Fact]
    public void ExplicitlyEmpty_EqualsDefaultConstructed()
    {
        // Normalised on the way in, so the two ways of saying "printed nothing" are one value.
        Assert.Equal(new ProcessResult(), new ProcessResult(output: string.Empty));
    }

    [Fact]
    public void Output_SurvivesAWithExpression()
    {
        ProcessResult result = new(0, "hello");

        Assert.Equal("hello", (result with { ExitCode = 1 }).Output);
    }

    [Fact]
    public void AnErrorResult_StillHasReadableOutput()
    {
        ProcessResult failed = new(error: "could not start");

        Assert.Equal(string.Empty, failed.Output);
        Assert.False(failed.Succeeded);
    }

    [Fact]
    public void ForkingAProgram_ReportsNoErrorAndNoOutput()
    {
        TerminalProcessRunner runner = new();

        ProcessResult result = runner.Run(new ProcessRequest("echo hi", new ProcessFlags("f")));

        Assert.Null(result.Error);
        Assert.Equal(string.Empty, result.Output);
    }
}
