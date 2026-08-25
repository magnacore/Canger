// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Previews;

/// <summary>How much room a preview has to fill.</summary>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
public readonly record struct PreviewSize(int Width, int Height);

/// <summary>
/// Produces a preview of a file.
/// </summary>
/// <remarks>
/// Declared in the domain and implemented by the preview layer, so the browser can show previews
/// without knowing whether they come from an external script, a built-in reader, or nothing at
/// all. That also means a session with previews turned off costs nothing rather than running a
/// script and discarding the answer.
/// </remarks>
public interface IPreviewProvider
{
    /// <summary>Produces a preview, or reports that there is none.</summary>
    /// <param name="path">The file.</param>
    /// <param name="size">How much room there is.</param>
    /// <param name="cancellationToken">Abandons a slow preview.</param>
    /// <returns>What to show.</returns>
    PreviewResult Preview(string path, PreviewSize size, CancellationToken cancellationToken = default);

    /// <summary>Forgets anything remembered about a file, after it has changed.</summary>
    /// <param name="path">The file.</param>
    void Invalidate(string path);
}
