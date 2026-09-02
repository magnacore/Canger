// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using Canger.Core.Processes;

namespace Canger.Tui;

/// <summary>
/// Runs external programs, handing the terminal over and taking it back.
/// </summary>
/// <remarks>
/// <para>
/// Most of the work here is not launching the process but managing the terminal around it. An
/// editor needs cooked input, echo, the normal screen and a visible cursor; Canger needs the
/// opposite. Getting the handover wrong leaves the user with an editor that cannot be typed into,
/// or a shell with no echo after Canger exits.
/// </para>
/// <para>
/// Which of those a program needs depends on the flags. A silent background job never touches
/// the terminal at all, so suspending for it would make the screen flicker for no reason.
/// </para>
/// </remarks>
public sealed class TerminalProcessRunner(Terminal? terminal = null) : IProcessRunner
{
    /// <summary>
    /// Where to start a program when the caller did not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Environment.CurrentDirectory"/> directly, which <em>throws</em> when the
    /// process's own working directory has been deleted — <c>getcwd(2)</c> has nothing to
    /// return. Another instance of Canger removing the directory this one is sitting in is
    /// enough, and then the next external program of any kind — a preview, a plugin announcing
    /// where you are, anything at all — took the whole browser down with an unhandled exception
    /// rather than failing on its own.
    /// </para>
    /// <para>
    /// Home, then the root: the first thing that exists. A program started somewhere other than
    /// where the user thought is a small surprise; a file manager that quits is not.
    /// </para>
    /// </remarks>
    private static string Here => StartIn(null, static () => Environment.CurrentDirectory);

    /// <summary>
    /// Decides where to start a program.
    /// </summary>
    /// <param name="requested">Where the caller asked for, or <see langword="null"/>.</param>
    /// <param name="current">Reads the process's own working directory.</param>
    /// <returns>A directory to start in.</returns>
    internal static string StartIn(string? requested, Func<string> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (requested is { Length: > 0 })
        {
            return requested;
        }

        try
        {
            return current();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Directory.Exists(home) ? home : "/";
        }
    }

    /// <summary>
    /// Points a process at a command line, through a shell only when one is needed.
    /// </summary>
    /// <param name="start">What to fill in.</param>
    /// <param name="command">The line to run.</param>
    /// <remarks>
    /// <para>
    /// <c>sh -c "the whole line"</c> makes the line a single argument, and Linux caps one
    /// argument at 131 072 bytes however much room <c>argv</c> has in total — two megabytes on an
    /// ordinary system. Numbering 2 561 files built a line of 380 530 bytes and failed with
    /// "Argument list too long" before the program was even reached, though the same paths as
    /// separate arguments are a fifth of what <c>argv</c> would allow.
    /// </para>
    /// <para>
    /// So a line that needs nothing from a shell is not given one, which raises the ceiling
    /// roughly sixteenfold. <see cref="CommandLine.TrySplit"/> decides, and refuses to decide
    /// whenever there is any doubt — a pipe, a redirection, a variable, a glob — in which case
    /// this is exactly what it always was.
    /// </para>
    /// <para>
    /// .NET looks along <c>PATH</c> for a program named without a directory, as a shell would, so
    /// <c>file-number</c> is found in <c>~/.local/bin</c> either way.
    /// </para>
    /// </remarks>
    private static void Aim(ProcessStartInfo start, string command)
    {
        if (CommandLine.TrySplit(command) is { Count: > 0 } words)
        {
            start.FileName = words[0];

            for (int i = 1; i < words.Count; i++)
            {
                start.ArgumentList.Add(words[i]);
            }

            return;
        }

        start.FileName = Shell;
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
    }

    /// <summary>The shell that interprets command lines.</summary>
    private static string Shell =>
        Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell &&
        !shell.Contains("fish", StringComparison.Ordinal)
            ? shell
            : "/bin/sh";

    /// <summary>Called before the terminal is handed over, so the caller can clean up.</summary>
    public event EventHandler? Suspending;

    /// <summary>Called after the terminal is taken back, so the caller can repaint.</summary>
    public event EventHandler? Resumed;

    /// <inheritdoc />
    public ProcessResult Run(ProcessRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Command);

        ProcessFlags flags = request.Flags;
        string command = request.Command;

        if (flags.AsRoot)
        {
            // -E keeps the environment, so the program still sees the user's settings.
            command = $"sudo -E {Shell} -c {Quote(command)}";
        }

        if (flags.NewTerminal)
        {
            return RunInNewTerminal(command, request.WorkingDirectory);
        }

        // Only a program that will actually use the terminal is worth suspending for.
        bool needsTerminal = !flags.Silent && !flags.Fork && !flags.Pipe;

        return needsTerminal
            ? RunInForeground(command, request, flags)
            : RunDetached(command, request, flags);
    }

    /// <inheritdoc />
    public ProcessResult RunCapturingOutput(ProcessRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Command);

        Suspending?.Invoke(this, EventArgs.Empty);
        terminal?.Suspend();

        try
        {
            using Process? process = Start(request.Command, request.WorkingDirectory,
                                           captureOutput: true, discardOutput: false);

            if (process is null)
            {
                return new ProcessResult(error: "could not start the program");
            }

            // Read before waiting: a program that fills the pipe would block forever otherwise,
            // and a chooser listing a large tree fills it easily.
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                                      or InvalidOperationException or IOException)
        {
            return new ProcessResult(error: e.Message);
        }
        finally
        {
            terminal?.Resume();
            Resumed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public ProcessResult RunWithInput(ProcessRequest request, string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Command);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            ProcessStartInfo start = new()
            {
                UseShellExecute = false,
                WorkingDirectory = request.WorkingDirectory ?? Here,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Aim(start, request.Command);

            using Process? process = Process.Start(start);

            if (process is null)
            {
                return new ProcessResult(error: "could not start the program");
            }

            // Verbatim, and then the pipe is closed so the program stops waiting. Not
            // `WriteLine`: a trailing newline is part of a passphrase as far as cryptsetup is
            // concerned, and a key file ending in one is the wrong key.
            process.StandardInput.Write(input);
            process.StandardInput.Close();

            // Both pipes drained before waiting, or a program that fills either would block for
            // ever with nothing reading it.
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, output,
                                     error.Length > 0 ? error : null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                                      or InvalidOperationException or IOException)
        {
            return new ProcessResult(error: e.Message);
        }
    }

    /// <summary>Runs a program that takes over the terminal, such as an editor.</summary>
    /// <inheritdoc />
    public IBackgroundProcess? StartInBackground(ProcessRequest request)
    {
        ProcessStartInfo start = new()
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory ?? Here,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // Empty rather than inherited: a program that stopped to ask a question would take
            // the keystrokes meant for the browser and never be answered.
            RedirectStandardInput = true,
        };

        Aim(start, request.Command);

        try
        {
            Process? process = Process.Start(start);

            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();

            return new BackgroundProcess(process);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                                       or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private ProcessResult RunInForeground(string command, ProcessRequest request,
                                          ProcessFlags flags)
    {
        Suspending?.Invoke(this, EventArgs.Empty);
        terminal?.Suspend();

        try
        {
            using Process? process = Start(command, request.WorkingDirectory, captureOutput: false,
                                           discardOutput: false);

            if (process is null)
            {
                return new ProcessResult(error: "could not start the program");
            }

            process.WaitForExit();

            if (flags.WaitForKey)
            {
                // Without this the interface would repaint over the program's last words before
                // they could be read.
                Console.Out.Write("\nPress any key to continue...");
                Console.Out.Flush();
                WaitForAnyKey(terminal);

                // The keystroke is read raw and never echoed, so without this the cursor stops
                // immediately after the "...". `Suspend` leaves the primary screen and its cursor
                // exactly as they are, so the *next* external program began drawing onto the end
                // of this line -- `Press any key to continue...──── Summing media duration ────`,
                // a run and a half later. The old `Console.In.Read()` ended on an echoed Enter and
                // left the cursor at column zero by accident; this does it on purpose.
                Console.Out.Write('\n');
                Console.Out.Flush();
            }

            return new ProcessResult(process.ExitCode);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                              or InvalidOperationException or IOException)
        {
            return new ProcessResult(error: e.Message);
        }
        finally
        {
            terminal?.Resume();
            Resumed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Runs a program that does not need the terminal.</summary>
    private static ProcessResult RunDetached(string command, ProcessRequest request,
                                             ProcessFlags flags)
    {
        try
        {
            // A forked program keeps running while Canger draws, so it must not be able to write
            // to the terminal: a video player announcing its codecs scrolls the alt screen and
            // corrupts everything on it. Detaching the three streams is what ranger does, and
            // the new session keeps the program alive independently of Canger.
            string line = flags.Fork ? Detached(command) : command;

            using Process? process = Start(line, request.WorkingDirectory,
                                           captureOutput: flags.Pipe,
                                           discardOutput: flags.Silent);

            if (process is null)
            {
                return new ProcessResult(error: "could not start the program");
            }

            if (flags.Fork)
            {
                // The shell exits as soon as it has launched the program, so this waits only for
                // that — not for the program, which is the point of forking.
                process.WaitForExit();
                return new ProcessResult();
            }

            string output = flags.Pipe ? process.StandardOutput.ReadToEnd() : string.Empty;
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                              or InvalidOperationException or IOException)
        {
            return new ProcessResult(error: e.Message);
        }
    }

    /// <summary>Runs a program in a separate terminal window.</summary>
    private static ProcessResult RunInNewTerminal(string command, string? workingDirectory)
    {
        if (TerminalEmulators.Detect() is not { } emulator)
        {
            return new ProcessResult(error: "no terminal emulator found");
        }

        ProcessStartInfo start = new()
        {
            FileName = emulator.Program,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Here,
        };

        foreach (string argument in emulator.BuildArguments(Shell, command))
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            // A new window is by definition not waited for.
            using Process? process = Process.Start(start);
            return process is null
                ? new ProcessResult(error: "could not start the terminal emulator")
                : new ProcessResult();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
                              or InvalidOperationException or IOException)
        {
            return new ProcessResult(error: e.Message);
        }
    }

    private static Process? Start(string command, string? workingDirectory, bool captureOutput,
                                  bool discardOutput)
    {
        ProcessStartInfo start = new()
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Here,
            RedirectStandardOutput = captureOutput || discardOutput,
            RedirectStandardError = discardOutput,
        };

        Aim(start, command);

        Process? process = Process.Start(start);

        if (process is not null && discardOutput)
        {
            // Reading and throwing away keeps the pipe from filling and blocking the program.
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }

    /// <summary>Waits for a single key, with the terminal in its normal cooked mode.</summary>
    private static void WaitForAnyKey(Terminal? terminal)
    {
        if (terminal is not null)
        {
            // Through Canger's own terminal, never System.Console: reading from `Console.In`
            // hands it the tty, and it reconfigures it — measured turning `ICRNL` off, which
            // Canger's raw mode deliberately leaves on. It also does its own line editing, so
            // "press any key" accepted only Enter and echoed every other key onto the screen.
            terminal.WaitForKeyPress();
            return;
        }

        try
        {
            Console.In.Read();
        }
        catch (IOException)
        {
            // No terminal to read from; carrying on is better than failing here.
        }
    }

    /// <summary>
    /// Wraps a command so it runs with no terminal of its own and outlives Canger.
    /// </summary>
    /// <param name="command">The command line as the rule wrote it.</param>
    /// <returns>A command line to hand to the shell.</returns>
    /// <remarks>
    /// <c>setsid</c> puts the program in its own session, so closing the terminal does not send
    /// it a hangup. Where it is unavailable the redirections alone still keep the screen clean,
    /// which is the part that matters most.
    /// </remarks>
    private static string Detached(string command)
    {
        string quoted = Quote(command);
        string inner = $"{Shell} -c {quoted} </dev/null >/dev/null 2>&1 &";

        return HasSetsid.Value ? $"setsid {Shell} -c {quoted} </dev/null >/dev/null 2>&1 &" : inner;
    }

    /// <summary>Whether <c>setsid</c> is on the path.</summary>
    private static readonly Lazy<bool> HasSetsid = new(() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(d => d.Length > 0 && File.Exists(Path.Join(d, "setsid"))));

    /// <summary>Quotes a command so a shell treats it as one argument.</summary>
    private static string Quote(string value) =>
        "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}
