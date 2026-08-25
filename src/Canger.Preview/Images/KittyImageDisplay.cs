// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using Canger.Tui.Rendering;

namespace Canger.Preview.Images;

/// <summary>
/// Draws images with the kitty graphics protocol.
/// </summary>
/// <remarks>
/// <para>
/// The terminal itself decodes and draws the image, so this needs no helper process and no
/// window system. The image is sent as an escape sequence carrying the file's path, which the
/// terminal reads directly — far cheaper than sending the pixels.
/// </para>
/// <para>
/// Under tmux the sequence has to be wrapped so it reaches the outer terminal rather than being
/// swallowed, and every escape inside it doubled. Without that the image simply never appears.
/// </para>
/// </remarks>
public sealed class KittyImageDisplay(TextWriter output) : IImageDisplay
{
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));

    private int _lastImageId;
    private bool _disposed;

    /// <inheritdoc />
    public string Name => "kitty";

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            string? term = Environment.GetEnvironmentVariable("TERM");
            string? program = Environment.GetEnvironmentVariable("TERM_PROGRAM");

            return term?.Contains("kitty", StringComparison.OrdinalIgnoreCase) == true ||
                   program?.Contains("ghostty", StringComparison.OrdinalIgnoreCase) == true ||
                   Environment.GetEnvironmentVariable("KITTY_WINDOW_ID") is { Length: > 0 };
        }
    }

    /// <summary>Whether the sequence must be wrapped to escape tmux.</summary>
    private static bool InsideTmux =>
        Environment.GetEnvironmentVariable("TMUX") is { Length: > 0 };

    /// <inheritdoc />
    public void Draw(string path, int x, int y, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Clear();

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _lastImageId++;

        // The path is sent base64-encoded, and t=t tells the terminal to read the file itself
        // rather than expecting the pixels inline.
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(path)));

        string command = string.Create(
            CultureInfo.InvariantCulture,
            $"a=T,f=100,t=t,i={_lastImageId},c={width},r={height},C=1");

        Write($"\e_G{command};{encoded}\e\\", moveTo: (x, y));
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (_lastImageId == 0)
        {
            return;
        }

        Write(string.Create(CultureInfo.InvariantCulture, $"\e_Ga=d,d=i,i={_lastImageId}\e\\"));
        _lastImageId = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
    }

    /// <summary>Writes a sequence, positioning the cursor first and wrapping it for tmux.</summary>
    private void Write(string sequence, (int X, int Y)? moveTo = null)
    {
        StringBuilder text = new();

        if (moveTo is { } position)
        {
            text.Append(Ansi.MoveCursor(position.Y + 1, position.X + 1));
        }

        // tmux only passes a sequence through when it is wrapped this way, with every escape
        // inside doubled so tmux does not read them as its own.
        text.Append(InsideTmux
            ? "\ePtmux;" + sequence.Replace("\e", "\e\e", StringComparison.Ordinal) + "\e\\"
            : sequence);

        _output.Write(text.ToString());
        _output.Flush();
    }
}
