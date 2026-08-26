// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Previews;

/// <summary>What kind of preview a file has.</summary>
public enum PreviewKind
{
    /// <summary>There is nothing to show.</summary>
    None,

    /// <summary>
    /// The answer is not known yet, because it is being prepared on a worker.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="None"/> on purpose. "Nothing to show" and "not yet" look the same
    /// to a drawing routine and are opposites to a layout one: a column that collapses because the
    /// answer has not arrived will expand again a moment later, which reads as a flicker. Ranger
    /// keeps the distinction by whether the file has a cache entry at all, and reuses the previous
    /// frame's decision when it does not (<c>gui/widgets/view_miller.py:196-201</c>).
    /// </remarks>
    Pending,

    /// <summary>Text, possibly with colour.</summary>
    Text,

    /// <summary>An image, at a path the preview script produced.</summary>
    Image,

    /// <summary>An image, the file itself.</summary>
    DirectImage,
}

/// <summary>
/// How a preview depends on the space available.
/// </summary>
/// <remarks>
/// A preview script says this through its exit code, and it is what makes previews affordable.
/// Syntax-highlighted source does not change when the column gets taller, and a file's metadata
/// does not change at all — so regenerating either on every resize would be pure waste.
/// </remarks>
[Flags]
public enum PreviewFit
{
    /// <summary>The preview depends on both dimensions.</summary>
    Exact = 0,

    /// <summary>The preview is the same at any width.</summary>
    AnyWidth = 1,

    /// <summary>The preview is the same at any height.</summary>
    AnyHeight = 2,

    /// <summary>The preview is the same whatever the size.</summary>
    AnySize = AnyWidth | AnyHeight,
}

/// <summary>What a preview script produced.</summary>
/// <param name="Kind">What kind of preview it is.</param>
/// <param name="Fit">Which dimensions it depends on.</param>
/// <param name="Text">The text to show, for a text preview.</param>
/// <param name="ImagePath">Where the image is, for an image preview.</param>
public readonly record struct PreviewResult(
    PreviewKind Kind,
    PreviewFit Fit = PreviewFit.Exact,
    string Text = "",
    string? ImagePath = null)
{
    /// <summary>There is nothing to show.</summary>
    public static PreviewResult None => new(PreviewKind.None, PreviewFit.AnySize);

    /// <summary>An answer that is still being prepared.</summary>
    public static PreviewResult Pending => new(PreviewKind.Pending, PreviewFit.AnySize);

    /// <summary>Whether there is anything to show.</summary>
    public bool HasContent => Kind != PreviewKind.None;
}

/// <summary>
/// The exit codes a preview script uses to say what it produced.
/// </summary>
/// <remarks>
/// This small protocol is the whole interface between Canger and the preview script, and it is
/// deliberately identical to ranger's (<c>core/actions.py:1145-1170</c>) so that an existing
/// <c>scope.sh</c> works unchanged.
/// </remarks>
public static class PreviewExitCodes
{
    /// <summary>Show what the script printed. It depends on both dimensions.</summary>
    public const int Success = 0;

    /// <summary>There is no preview for this file.</summary>
    public const int NoPreview = 1;

    /// <summary>Canger should read the file as text itself.</summary>
    public const int ReadFileAsText = 2;

    /// <summary>Show what the script printed; it is the same at any width.</summary>
    public const int FixWidth = 3;

    /// <summary>Show what the script printed; it is the same at any height.</summary>
    public const int FixHeight = 4;

    /// <summary>Show what the script printed; it is the same whatever the size.</summary>
    public const int FixBoth = 5;

    /// <summary>Show the image the script wrote to the cache path.</summary>
    public const int ImageAtCachePath = 6;

    /// <summary>Show the file itself as an image.</summary>
    public const int ImageIsTheFile = 7;

    /// <summary>Turns an exit code into what it says about the preview.</summary>
    /// <param name="exitCode">What the script returned.</param>
    /// <returns>The kind and the dimensions it depends on.</returns>
    public static (PreviewKind Kind, PreviewFit Fit) Interpret(int exitCode) => exitCode switch
    {
        Success => (PreviewKind.Text, PreviewFit.Exact),
        ReadFileAsText => (PreviewKind.Text, PreviewFit.AnySize),
        FixWidth => (PreviewKind.Text, PreviewFit.AnyWidth),
        FixHeight => (PreviewKind.Text, PreviewFit.AnyHeight),
        FixBoth => (PreviewKind.Text, PreviewFit.AnySize),
        ImageAtCachePath => (PreviewKind.Image, PreviewFit.AnySize),
        ImageIsTheFile => (PreviewKind.DirectImage, PreviewFit.AnySize),

        // Anything else, including the documented "no preview", shows nothing. An unrecognised
        // code is treated the same way rather than guessed at.
        _ => (PreviewKind.None, PreviewFit.AnySize),
    };
}
