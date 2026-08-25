// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Canger.Core.Processes;

namespace Canger.Preview.Images;

/// <summary>
/// Draws images through a long-running <c>ueberzug</c> helper.
/// </summary>
/// <remarks>
/// <para>
/// Ueberzug draws images in a separate X11 window positioned over the terminal, which is how it
/// works in terminals with no image protocol of their own. It is told what to do through a stream
/// of JSON commands on its standard input.
/// </para>
/// <para>
/// One helper is started and kept, rather than one per image: starting a process for every
/// keypress as the cursor moves through a directory of photographs would be unusably slow.
/// </para>
/// </remarks>
public sealed class UeberzugImageDisplay : IImageDisplay
{
    private const string Identifier = "canger-preview";

    private Process? _process;
    private bool _hasImage;
    private bool _disposed;

    /// <inheritdoc />
    public string Name => "ueberzug";

    /// <inheritdoc />
    public bool IsAvailable =>
        Executable is not null &&
        Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 };

    /// <summary>Which helper to run, preferring the maintained fork.</summary>
    private static string? Executable
    {
        get
        {
            foreach (string candidate in (string[])["ueberzugpp", "ueberzug"])
            {
                if (Executables.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public void Draw(string path, int x, int y, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!EnsureRunning())
        {
            return;
        }

        Send(new Dictionary<string, object>
        {
            ["action"] = "add",
            ["identifier"] = Identifier,
            ["x"] = x,
            ["y"] = y,
            ["max_width"] = Math.Max(width, 1),
            ["max_height"] = Math.Max(height, 1),
            ["path"] = path,
        });

        _hasImage = true;
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (!_hasImage || _process is null)
        {
            return;
        }

        Send(new Dictionary<string, object>
        {
            ["action"] = "remove",
            ["identifier"] = Identifier,
        });

        _hasImage = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_process is null)
        {
            return;
        }

        try
        {
            Clear();
            _process.StandardInput.Close();

            // Give it a moment to leave on its own; an image left drawn would outlive Canger and
            // sit over whatever the user does next.
            if (!_process.WaitForExit(1000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception)
        {
            // It has already gone.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private bool EnsureRunning()
    {
        if (_process is { HasExited: false })
        {
            return true;
        }

        if (Executable is not { } executable)
        {
            return false;
        }

        ProcessStartInfo start = new()
        {
            FileName = executable,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("layer");
        start.ArgumentList.Add("--silent");

        try
        {
            _process = Process.Start(start);
            return _process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _process = null;
            return false;
        }
    }

    private void Send(Dictionary<string, object> command)
    {
        try
        {
            _process!.StandardInput.WriteLine(
                JsonSerializer.Serialize(command, ImageJson.Options));
            _process.StandardInput.Flush();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException
                                      or InvalidOperationException)
        {
            // The helper has gone; the next draw restarts it rather than failing here.
            _process = null;
            _hasImage = false;
        }
    }
}

/// <summary>Shared JSON settings for talking to image helpers.</summary>
internal static class ImageJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };
}
