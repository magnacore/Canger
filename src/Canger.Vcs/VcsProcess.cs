// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;

namespace Canger.Vcs;

/// <summary>
/// Runs a version control program and collects what it said.
/// </summary>
/// <remarks>
/// Arguments are passed as a list rather than a command line, so nothing is ever handed to a
/// shell. A repository containing a file called <c>; rm -rf ~</c> is a perfectly ordinary
/// repository, and asking git about it must not be dangerous.
/// </remarks>
public static class VcsProcess
{
    /// <summary>How long a command is given before it is abandoned.</summary>
    /// <remarks>
    /// A repository on a slow or unreachable network mount would otherwise hang the worker for
    /// as long as the filesystem takes to give up, which can be minutes.
    /// </remarks>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs a command and returns its standard output.
    /// </summary>
    /// <param name="program">The program to run.</param>
    /// <param name="workingDirectory">Where to run it.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>What it wrote, with any single trailing newline removed.</returns>
    /// <exception cref="VcsException">It could not be run, timed out, or failed.</exception>
    public static string Run(string program, string workingDirectory,
                             params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);

        using Process process = Start(program, workingDirectory, arguments);

        // Read before waiting: a program that fills the pipe buffer blocks until someone drains
        // it, so waiting first would deadlock on any sizeable output.
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(Timeout))
        {
            Kill(process);
            throw new VcsException($"{program} {string.Join(' ', arguments)}: timed out");
        }

        if (process.ExitCode != 0)
        {
            throw new VcsException(
                $"{program} {string.Join(' ', arguments)}: {error.Trim()}".TrimEnd());
        }

        return output.EndsWith('\n') ? output[..^1] : output;
    }

    /// <summary>
    /// Runs a command and discards its output.
    /// </summary>
    /// <param name="program">The program to run.</param>
    /// <param name="workingDirectory">Where to run it.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <exception cref="VcsException">It could not be run, timed out, or failed.</exception>
    public static void RunSilently(string program, string workingDirectory,
                                   params string[] arguments) =>
        Run(program, workingDirectory, arguments);

    /// <summary>
    /// Splits output that a version control program separated with NUL bytes.
    /// </summary>
    /// <param name="output">What the program wrote.</param>
    /// <returns>The records, without the trailing empty one.</returns>
    /// <remarks>
    /// NUL separation is why the <c>-z</c> flags are used throughout: a filename may contain a
    /// newline, and splitting on newlines would then split a filename in half.
    /// </remarks>
    public static IEnumerable<string> SplitNul(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (string record in output.Split('\0'))
        {
            if (record.Length > 0)
            {
                yield return record;
            }
        }
    }

    private static Process Start(string program, string workingDirectory, string[] arguments)
    {
        ProcessStartInfo startInfo = new(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Asking for machine-readable output is pointless if the locale then translates it.
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        // Anything that would try to talk to the user has to be stopped from doing so: this runs
        // on a background thread with no terminal, and a password prompt would hang forever.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        try
        {
            return Process.Start(startInfo)
                   ?? throw new VcsException($"{program}: could not be started");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // Almost always "not installed", which is worth saying plainly rather than as an
            // error number.
            throw new VcsException($"{program}: {e.Message}", e);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            // It exited between the timeout and the kill, which is the outcome we wanted.
        }
    }
}
