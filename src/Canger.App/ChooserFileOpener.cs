// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;

namespace Canger.App;

/// <summary>
/// Makes Canger a file chooser for another program.
/// </summary>
/// <remarks>
/// <para>
/// With <c>--choosefile</c> or <c>--choosefiles</c>, opening a file writes its path to a file and
/// quits instead of launching anything. That is what lets a script use Canger as its file picker:
/// run it, let the user browse, read the answer.
/// </para>
/// <para>
/// Standing in for the real opener rather than checking a flag inside the open command means
/// every route to opening a file is covered — Enter, the right arrow, <c>:open_with</c>, a mouse
/// click — without any of them knowing this mode exists.
/// </para>
/// </remarks>
/// <param name="outputPath">Where the chosen paths are written.</param>
/// <param name="multiple">
/// Whether every selected file is written, one per line, rather than just the first.
/// </param>
/// <param name="quit">Asks the interface to exit once the answer has been written.</param>
public sealed class ChooserFileOpener(string outputPath, bool multiple, Action quit) : IFileOpener
{
    /// <inheritdoc />
    public OpenResult Open(IReadOnlyList<string> paths, int number = 0, string? label = null,
                           string flags = "")
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return new OpenResult(Succeeded: false, Message: "nothing selected");
        }

        IEnumerable<string> chosen = multiple ? paths : paths.Take(1);

        try
        {
            // A trailing newline, so the file is a well-formed list of lines whether it holds
            // one path or twenty — `read` in a shell script depends on it.
            File.WriteAllLines(outputPath, chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new OpenResult(Succeeded: false, Message: e.Message);
        }

        quit();
        return new OpenResult(Succeeded: true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A chooser has nothing to offer here: there is only one thing it can do with a file, so
    /// <c>:open_with</c> has no alternatives to list.
    /// </remarks>
    public IReadOnlyList<OpenAlternative> Alternatives(string path) => [];
}
