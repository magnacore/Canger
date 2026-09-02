// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.State;

/// <summary>What one Canger copied or cut, as another Canger sees it.</summary>
/// <param name="Paths">The absolute paths, in the order they were chosen.</param>
/// <param name="Cut">Whether pasting them moves rather than copies.</param>
public readonly record struct SharedCopy(IReadOnlyList<string> Paths, bool Cut);

/// <summary>
/// A copy buffer shared between every running Canger.
/// </summary>
/// <remarks>
/// <para>
/// Copy in one window and paste in another. Ranger cannot do this — its <c>copy_buffer</c> is an
/// in-memory set on the file manager — and neither could Canger, so this is a deliberate addition
/// rather than a parity fix. It is off unless <c>shared_copy_buffer</c> says otherwise, because
/// two Cangers open on unrelated work should not have <c>dd</c> in one arm <c>pp</c> in the other.
/// </para>
/// <para>
/// The buffer outlives the windows, like a clipboard: a cut that is never pasted is still a cut
/// tomorrow, and the dimmed rows say so. There is no expiry and no tracking of which instances are
/// alive — both were considered and neither earns the machinery it costs.
/// </para>
/// <para>
/// Unlike <see cref="Tags"/> this does not read before writing. A tag set is merged, so losing a
/// concurrent edit loses information; a copy buffer is replaced whole, so two instances copying at
/// once should end with whichever copied last — which is what a clipboard does.
/// </para>
/// </remarks>
public sealed class SharedCopyBuffer(string path)
{
    private const string CutMarker = "cut";
    private const string CopyMarker = "copy";

    /// <summary>Where the buffer is kept.</summary>
    public string Path { get; } = path;

    /// <summary>Whether changes are written at all. False under <c>--clean</c>.</summary>
    public bool Persistent { get; set; } = true;

    /// <summary>The exact text of the last contents this instance saw or wrote.</summary>
    /// <remarks>
    /// The text itself rather than a timestamp. A modification time looked like the cheap answer
    /// and is the wrong one: measured on this machine, five rewrites in quick succession left
    /// <c>mtime_ns</c> **identical** every time, because the filesystem keeps a coarse clock. The
    /// check would have failed in precisely the case it exists for — two windows acting within
    /// the same instant. Comparing content cannot be fooled, and the file is a few hundred bytes.
    /// </remarks>
    private string? _seen;

    /// <summary>Reads the buffer, but only when it differs from what this instance last saw.</summary>
    /// <returns>
    /// The new contents, or <see langword="null"/> when there is nothing new — which covers both
    /// "unchanged" and "could not be read". Both mean the same thing to a caller: keep what you
    /// have. Answering "empty" on a transient error would quietly disarm a pending cut in every
    /// window, which is the mistake <see cref="Tags.CouldNotBeRead"/> exists to prevent.
    /// </returns>
    /// <remarks>
    /// A missing file is an empty buffer rather than a failure: that is what a completed move
    /// leaves behind.
    /// </remarks>
    public SharedCopy? ReadIfChanged()
    {
        string text;

        try
        {
            text = File.Exists(Path) ? File.ReadAllText(Path) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (string.Equals(text, _seen, StringComparison.Ordinal))
        {
            return null;
        }

        _seen = text;
        return Parse(text);
    }

    /// <summary>Reads the buffer whatever this instance last saw.</summary>
    /// <returns>The contents, or <see langword="null"/> when it could not be read.</returns>
    public SharedCopy? Read()
    {
        try
        {
            string text = File.Exists(Path) ? File.ReadAllText(Path) : string.Empty;
            _seen = text;
            return Parse(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Turns the file's text into paths and a mode.</summary>
    private static SharedCopy Parse(string text)
    {
        string[] lines = text.Split('\n');

        return new SharedCopy(
            [.. lines.Skip(1).Select(line => line.Trim()).Where(line => line.Length > 0)],
            Cut: lines.Length > 0 &&
                 lines[0].Trim().Equals(CutMarker, StringComparison.Ordinal));
    }

    /// <summary>Replaces the buffer with a new set of paths.</summary>
    /// <param name="paths">The absolute paths.</param>
    /// <param name="cut">Whether pasting them moves rather than copies.</param>
    public void Write(IEnumerable<string> paths, bool cut)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!Persistent)
        {
            return;
        }

        try
        {
            StateFile.Replace(Path, [cut ? CutMarker : CopyMarker, .. paths]);

            // Marked as seen, so the instance that wrote it does not read it straight back and
            // rebuild everything it already has. Read rather than reconstructed, because what
            // matters is the bytes a later comparison will see.
            _seen = File.Exists(Path) ? File.ReadAllText(Path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Failing to share a copy buffer is not worth failing a copy over.
        }
    }

    /// <summary>Empties the buffer, as a completed move does.</summary>
    public void Clear() => Write([], cut: false);

}
