// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Canger.Core.Processes;

/// <summary>
/// How a program should be run: silently, in the background, through a pager, and so on.
/// </summary>
/// <remarks>
/// <para>
/// Flags are written as letters because that is how they appear in <c>rifle.conf</c> and after
/// <c>:shell</c>, where brevity matters. A capital letter cancels its lowercase counterpart,
/// which is what lets a general rule set a flag and a specific one override it without either
/// needing to know about the other.
/// </para>
/// </remarks>
public readonly record struct ProcessFlags(string Letters)
{
    /// <summary>No flags.</summary>
    public static ProcessFlags None => new(string.Empty);

    /// <summary>Discard the program's output rather than showing it.</summary>
    public bool Silent => Has('s');

    /// <summary>Start the program and carry on, rather than waiting for it.</summary>
    public bool Fork => Has('f');

    /// <summary>Send the program's output to a pager.</summary>
    public bool Pipe => Has('p');

    /// <summary>Wait for a key press after the program finishes, so its output can be read.</summary>
    public bool WaitForKey => Has('w');

    /// <summary>Run the program as root.</summary>
    public bool AsRoot => Has('r');

    /// <summary>Run the program in a new terminal window.</summary>
    public bool NewTerminal => Has('t');

    /// <summary>Act on the file under the cursor rather than the whole selection.</summary>
    public bool CurrentFileOnly => Has('c');

    /// <summary>Whether a flag is set.</summary>
    /// <param name="flag">The lowercase letter.</param>
    /// <returns><see langword="true"/> when it survives cancellation.</returns>
    public bool Has(char flag) => Squash(Letters).Contains(flag, StringComparison.Ordinal);

    /// <summary>Combines two sets of flags, with the later one able to cancel the earlier.</summary>
    /// <param name="other">The flags to add.</param>
    /// <returns>The combined flags.</returns>
    public ProcessFlags Add(ProcessFlags other) => new(Letters + other.Letters);

    /// <summary>
    /// Removes flags cancelled by an uppercase counterpart.
    /// </summary>
    /// <remarks>
    /// An uppercase letter removes <em>both</em> itself and its lowercase form, so <c>fF</c>
    /// leaves nothing rather than leaving <c>F</c> behind. Ranger documents this behaviour with
    /// the examples <c>abcC</c> becoming <c>ab</c> and <c>CabcAd</c> becoming <c>bd</c>
    /// (<c>ext/rifle.py:167-178</c>).
    /// </remarks>
    /// <param name="letters">The flags as written.</param>
    /// <returns>The flags that survive.</returns>
    public static string Squash(string letters)
    {
        ArgumentNullException.ThrowIfNull(letters);

        HashSet<char> cancelled = [];
        foreach (char letter in letters)
        {
            if (char.IsAsciiLetterUpper(letter))
            {
                cancelled.Add(letter);
                cancelled.Add(char.ToLowerInvariant(letter));
            }
        }

        if (cancelled.Count == 0)
        {
            return letters;
        }

        StringBuilder result = new(letters.Length);
        foreach (char letter in letters)
        {
            if (!cancelled.Contains(letter))
            {
                result.Append(letter);
            }
        }

        return result.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => Squash(Letters);
}
