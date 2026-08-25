// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Preview.Images;

/// <summary>
/// Draws an image into a region of the terminal.
/// </summary>
/// <remarks>
/// <para>
/// Images cannot go through the screen buffer. A terminal that can show one does so through its
/// own protocol — an escape sequence carrying the image data, or a helper process drawing over
/// the window — and the result occupies cells the buffer knows nothing about. So the buffer
/// leaves those cells blank and the image is written past it.
/// </para>
/// <para>
/// That also means the image must be cleared explicitly when the selection changes, since
/// nothing about redrawing the buffer will remove it.
/// </para>
/// </remarks>
public interface IImageDisplay : IDisposable
{
    /// <summary>The name this protocol goes by in the configuration.</summary>
    string Name { get; }

    /// <summary>Whether this protocol can be used in the current terminal.</summary>
    bool IsAvailable { get; }

    /// <summary>Draws an image, replacing anything previously drawn.</summary>
    /// <param name="path">The image file.</param>
    /// <param name="x">Left column, counting from zero.</param>
    /// <param name="y">Top row, counting from zero.</param>
    /// <param name="width">Columns available.</param>
    /// <param name="height">Rows available.</param>
    void Draw(string path, int x, int y, int width, int height);

    /// <summary>Removes whatever was drawn.</summary>
    void Clear();
}

/// <summary>An image display that does nothing, for terminals that cannot show images.</summary>
public sealed class NoImageDisplay : IImageDisplay
{
    /// <inheritdoc />
    public string Name => "none";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public void Draw(string path, int x, int y, int width, int height)
    {
    }

    /// <inheritdoc />
    public void Clear()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
