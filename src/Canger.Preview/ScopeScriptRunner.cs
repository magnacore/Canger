// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Canger.Core.Previews;

namespace Canger.Preview;

/// <summary>
/// Runs the external preview script and interprets what it says.
/// </summary>
/// <remarks>
/// <para>
/// Previews are delegated to a shell script rather than built in, because what makes a good
/// preview of a PDF, an archive or a video is a question with a hundred answers and they all
/// involve programs the user already has. The script decides; Canger only has to display the
/// result.
/// </para>
/// <para>
/// The contract is deliberately identical to ranger's, so an existing <c>scope.sh</c> works
/// unchanged: five positional arguments, and an exit code saying what was produced and whether
/// it depends on the size.
/// </para>
/// </remarks>
public sealed class ScopeScriptRunner(string scriptPath, string cacheDirectory)
{
    /// <summary>How long a preview may take before it is abandoned.</summary>
    /// <remarks>
    /// A preview is a convenience. If a script hangs on a network filesystem or a corrupt
    /// archive, the browser must carry on rather than freeze with it.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether images may be produced, from the <c>preview_images</c> setting.</summary>
    public bool ImagesEnabled { get; set; }

    /// <summary>The script being run.</summary>
    public string ScriptPath { get; } = scriptPath;

    /// <summary>Whether the script exists and can be executed.</summary>
    public bool IsUsable
    {
        get
        {
            try
            {
                return File.Exists(ScriptPath) &&
                       (File.GetUnixFileMode(ScriptPath) &
                        (UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                         UnixFileMode.OtherExecute)) != 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Runs the script for a file.
    /// </summary>
    /// <param name="path">The file to preview.</param>
    /// <param name="size">How much room there is.</param>
    /// <param name="cancellationToken">Abandons the preview.</param>
    /// <returns>What the script produced.</returns>
    public PreviewResult Run(string path, PreviewSize size,
                             CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!IsUsable)
        {
            return PreviewResult.None;
        }

        string imagePath = CachePathFor(path);

        ProcessStartInfo start = new()
        {
            FileName = ScriptPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // The script runs beside the file so relative commands behave sensibly, but only
            // when that directory actually exists — otherwise starting the process fails and
            // the preview is lost for a reason that has nothing to do with the file.
            WorkingDirectory = WorkingDirectoryFor(path),
        };

        // The five arguments ranger passes, in the same order, so an existing script works.
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(size.Width.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(size.Height.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(imagePath);
        start.ArgumentList.Add(ImagesEnabled ? "True" : "False");

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return PreviewResult.None;
            }

            // Read before waiting, or a script producing more output than the pipe holds would
            // block forever waiting for someone to drain it.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);

            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                TryKill(process);
                return PreviewResult.None;
            }

            return Interpret(process.ExitCode, output.GetAwaiter().GetResult(), path, imagePath);
        }
        catch (OperationCanceledException)
        {
            return PreviewResult.None;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                      or IOException)
        {
            return PreviewResult.None;
        }
    }

    /// <summary>Turns an exit code and some output into a preview.</summary>
    private static PreviewResult Interpret(int exitCode, string output, string path,
                                           string imagePath)
    {
        (PreviewKind kind, PreviewFit fit) = PreviewExitCodes.Interpret(exitCode);

        switch (kind)
        {
            case PreviewKind.Text:
                // Exit code 2 means the script declined and Canger should read the file itself,
                // which is how plain text avoids a pointless round trip through a highlighter.
                string text = exitCode == PreviewExitCodes.ReadFileAsText
                    ? ReadTextFile(path)
                    : output;

                return text.Length == 0
                    ? PreviewResult.None
                    : new PreviewResult(PreviewKind.Text, fit, text);

            case PreviewKind.Image:
                // Emptiness counts as absence. A thumbnailer that creates its output file and
                // then fails — ffmpegthumbnailer on a Matroska file with no video stream, which
                // is every .mka — leaves a nought-byte file behind, and handing that to an image
                // protocol achieves nothing but a corrupted screen.
                return HasContent(imagePath)
                    ? new PreviewResult(PreviewKind.Image, fit, ImagePath: imagePath)
                    : PreviewResult.None;

            case PreviewKind.DirectImage:
                return new PreviewResult(PreviewKind.DirectImage, fit, ImagePath: path);

            default:
                return PreviewResult.None;
        }
    }

    /// <summary>
    /// Reads the start of a text file.
    /// </summary>
    /// <remarks>
    /// Bounded, because a preview only ever shows a screenful and reading a multi-gigabyte log
    /// into memory to display forty lines of it would be absurd.
    /// </remarks>
    private static string ReadTextFile(string path)
    {
        const int limit = 32 * 1024;

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[limit];
            int read = stream.ReadAtLeast(buffer, limit, throwOnEndOfStream: false);

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Where the script should write an image for a file.
    /// </summary>
    /// <remarks>
    /// Named from a hash of the path so that two files with the same name in different
    /// directories do not collide, and so the name is safe whatever the original contained.
    /// </remarks>
    public string CachePathFor(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
        return Path.Join(cacheDirectory, Convert.ToHexStringLower(hash));
    }

    /// <summary>Whether a file exists and has something in it.</summary>
    private static bool HasContent(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true, Length: > 0 };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Where to run the script, falling back when the file's directory is unusable.</summary>
    private static string WorkingDirectoryFor(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        return directory is { Length: > 0 } && Directory.Exists(directory)
            ? directory
            : Environment.CurrentDirectory;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException
                                      or System.ComponentModel.Win32Exception)
        {
            // It exited between the timeout and the kill.
        }
    }
}
