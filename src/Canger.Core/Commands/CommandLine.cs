// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Commands;

/// <summary>
/// A command line, split into a name and arguments in the several ways commands need to read it.
/// </summary>
/// <remarks>
/// Commands want different views of the same text. <c>:mkdir</c> wants everything after the name
/// exactly as typed, spaces included, because a directory name may contain them. <c>:set</c>
/// wants the first word and then the rest. <c>:shell</c> wants leading <c>-flags</c> pulled off
/// first. Rather than have each command re-split the line and get the edge cases subtly
/// different, all of those readings live here.
/// </remarks>
public sealed class CommandLine
{
    private readonly string[] _words;

    /// <summary>Splits a line.</summary>
    /// <param name="line">The line as typed, without the leading colon.</param>
    public CommandLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Line = line;

        // Any whitespace separates, not just a space. A configuration that lines its comments up
        // with tabs — `map edn directories_number_highlight<TAB><TAB># Number` — otherwise
        // leaves the tabs and the comment inside the first word, so the command name is
        // `directories_number_highlight\t\t\t#` and no such command exists. Thirty-six bindings
        // in one real configuration were dead that way, silently.
        //
        // Ranger splits with `str.split()`, which is whitespace-general, and keeps the trailing
        // comment in the line — `source` skips only lines that *start* with `#`
        // (`core/actions.py:378-381`). Keeping it is right: for a `shell` binding the comment
        // reaches `sh`, which ignores it, and for everything else it is documentation the hint
        // window can show.
        _words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The line as typed.</summary>
    public string Line { get; }

    /// <summary>The whitespace-separated words, including the command name at index 0.</summary>
    public IReadOnlyList<string> Words => _words;

    /// <summary>The command name.</summary>
    public string Name => _words.Length > 0 ? _words[0] : string.Empty;

    /// <summary>How many words there are, including the name.</summary>
    public int Count => _words.Length;

    /// <summary>One word, or an empty string when there is no such word.</summary>
    /// <param name="index">Which word, with the command name at 0.</param>
    /// <returns>The word.</returns>
    public string Word(int index) => index >= 0 && index < _words.Length ? _words[index] : string.Empty;

    /// <summary>
    /// Everything from a word onwards, with the original spacing intact.
    /// </summary>
    /// <remarks>
    /// This is what commands taking free text use, so that <c>:mkdir My Documents</c> creates one
    /// directory rather than two, and <c>:shell echo  a   b</c> passes the spacing through.
    /// </remarks>
    /// <param name="index">Which word to start from.</param>
    /// <returns>The rest of the line, trimmed of leading space.</returns>
    public string Rest(int index)
    {
        if (index <= 0)
        {
            return Line;
        }

        int position = 0;

        for (int word = 0; word < index; word++)
        {
            while (position < Line.Length && char.IsWhiteSpace(Line[position]))
            {
                position++;
            }

            if (position >= Line.Length)
            {
                return string.Empty;
            }

            while (position < Line.Length && !char.IsWhiteSpace(Line[position]))
            {
                position++;
            }
        }

        // Skip the separator before the remainder, but keep the spacing inside it.
        while (position < Line.Length && char.IsWhiteSpace(Line[position]))
        {
            position++;
        }

        return position >= Line.Length ? string.Empty : Line[position..];
    }

    /// <summary>
    /// Pulls leading <c>-abc</c> flag groups off the arguments.
    /// </summary>
    /// <remarks>
    /// Flags accumulate, so <c>-f -r</c> and <c>-fr</c> mean the same thing. A bare <c>--</c>
    /// stops the scan, which is how a command is given an argument that starts with a dash.
    /// This follows <c>api/commands.py:223-253</c>.
    /// </remarks>
    /// <returns>The letters found, and the arguments that remain.</returns>
    public (string Flags, string Arguments) ParseFlags()
    {
        System.Text.StringBuilder flags = new();
        int index = 1;

        while (index < _words.Length)
        {
            string word = _words[index];

            if (word == "--")
            {
                index++;
                break;
            }

            if (word.Length < 2 || word[0] != '-')
            {
                break;
            }

            flags.Append(word.AsSpan(1));
            index++;
        }

        return (flags.ToString(), Rest(index));
    }

    /// <inheritdoc />
    public override string ToString() => Line;
}
