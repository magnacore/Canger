// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Processes;

/// <summary>One way a file can be opened.</summary>
/// <param name="Number">Its position among the alternatives, as <c>:open_with</c> counts them.</param>
/// <param name="Label">The name it goes by, when it has one.</param>
/// <param name="Command">The command that would run.</param>
public readonly record struct OpenAlternative(int Number, string? Label, string Command);

/// <summary>How opening a file turned out.</summary>
/// <param name="Succeeded">Whether a program was started.</param>
/// <param name="NeedsUserChoice">Whether the configuration says to ask which program to use.</param>
/// <param name="Message">What to tell the user, when there is something to say.</param>
public readonly record struct OpenResult(
    bool Succeeded,
    bool NeedsUserChoice = false,
    string? Message = null);

/// <summary>
/// Decides which program opens a file, and opens it.
/// </summary>
/// <remarks>
/// Declared here, in the domain, and implemented by the rifle engine. That keeps the dependency
/// pointing inwards: commands can open files without the domain knowing anything about rifle's
/// configuration format, and a different opener could be substituted without touching them.
/// </remarks>
public interface IFileOpener
{
    /// <summary>Opens files with whichever rule applies.</summary>
    /// <param name="paths">The files.</param>
    /// <param name="number">Which alternative to use, counting from zero.</param>
    /// <param name="label">Use the alternative with this name instead of counting.</param>
    /// <param name="flags">Extra flags to add to whatever the rule asked for.</param>
    /// <returns>How it turned out.</returns>
    OpenResult Open(IReadOnlyList<string> paths, int number = 0, string? label = null,
                    string flags = "");

    /// <summary>Lists the ways a file could be opened, most preferred first.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The alternatives.</returns>
    IReadOnlyList<OpenAlternative> Alternatives(string path);
}
