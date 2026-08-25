// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using Canger.Core.Processes;

namespace Canger.Tui;

/// <summary>
/// A real process running alongside the interface, with both its output streams drained.
/// </summary>
/// <remarks>
/// The draining is the point. A pipe holds about 64 KB; once it is full the program blocks
/// writing to it and never finishes, so a background command that talks too much would hang
/// forever with nothing to show why. Reading continuously on the framework's own callback
/// threads keeps that from happening whether or not anybody ever looks at the text.
/// </remarks>
internal sealed class BackgroundProcess : IBackgroundProcess
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();
    private readonly Lock _gate = new();
    private bool _disposed;
    private bool _drained;

    /// <summary>Wraps a started process and begins draining it.</summary>
    /// <param name="process">A process started with both output streams redirected.</param>
    internal BackgroundProcess(Process process)
    {
        _process = process;

        // The handlers run on framework threads while the queue reads from the interface thread,
        // so every touch of either buffer is under the lock.
        _process.OutputDataReceived += (_, e) => Append(_output, e.Data);
        _process.ErrorDataReceived += (_, e) => Append(_error, e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <inheritdoc />
    public bool HasExited => _process.HasExited;

    /// <inheritdoc />
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    /// <inheritdoc />
    public string StandardOutput => Read(_output);

    /// <inheritdoc />
    public string StandardError => Read(_error);

    /// <inheritdoc />
    public bool WaitForExit(TimeSpan timeout)
    {
        bool exited = timeout <= TimeSpan.Zero
            ? _process.HasExited
            : _process.WaitForExit(timeout);

        if (exited && !_drained)
        {
            _drained = true;

            // `HasExited` and the timed overload both return before the output handlers have
            // necessarily delivered their last lines; only the untimed wait joins them. The
            // process has already ended, so this returns at once — but without it the final
            // words of an error message can go missing, which is the half of stderr that
            // usually says what went wrong.
            _process.WaitForExit();
        }

        return exited;
    }

    /// <inheritdoc />
    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                // The command line went to a shell, so killing only the shell would leave the
                // archiver it started running with nothing attached to it.
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            // It finished between the check and the kill, which is the outcome that was wanted.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _process.Dispose();
    }

    private void Append(StringBuilder buffer, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_gate)
        {
            buffer.AppendLine(line);
        }
    }

    private string Read(StringBuilder buffer)
    {
        lock (_gate)
        {
            return buffer.ToString();
        }
    }
}
