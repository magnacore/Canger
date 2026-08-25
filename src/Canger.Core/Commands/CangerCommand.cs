// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Commands;

/// <summary>
/// Names a command, so it can be typed at the prompt and bound to a key.
/// </summary>
/// <remarks>
/// Marking commands with an attribute rather than discovering them by naming convention means a
/// command's name is stated rather than derived, so it can differ from the class name where C#
/// would not allow a match — <c>quit!</c> and <c>set</c> being the obvious cases.
/// </remarks>
/// <param name="name">The name typed at the prompt.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute(string name) : Attribute
{
    /// <summary>The name typed at the prompt.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// Whether typing an unambiguous prefix is enough to run this command.
    /// </summary>
    /// <remarks>
    /// Turned off for commands where a mistake is expensive and a near-miss is plausible, so
    /// that <c>:q</c> can never resolve to <c>:quit!</c>.
    /// </remarks>
    public bool AllowAbbreviation { get; init; } = true;

    /// <summary>
    /// Whether macros such as <c>%f</c> are expanded before the command runs.
    /// </summary>
    /// <remarks>
    /// Off for commands that take a command line as an argument — <c>:map</c>, <c>:alias</c> —
    /// because their argument is expanded later, when the binding fires, and expanding it twice
    /// would resolve it against the wrong directory.
    /// </remarks>
    public bool ResolveMacros { get; init; } = true;

    /// <summary>
    /// Whether expanded macros are quoted for the shell.
    /// </summary>
    /// <remarks>
    /// On for commands that pass their argument to a shell, so that a filename containing a
    /// space or a quote cannot turn into extra arguments or, worse, extra commands.
    /// </remarks>
    public bool EscapeMacrosForShell { get; init; }

    /// <summary>A one-line description, shown by <c>:help</c>.</summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Something the user can run, from the prompt or from a key binding.
/// </summary>
/// <remarks>
/// <para>
/// A command is an object rather than a function so that it can do more than run: it can offer
/// completions, act as the user types, and undo itself if they change their mind. Those three
/// hooks are what make <c>:cd</c> complete directory names, <c>:filter</c> narrow the listing
/// live, and pressing Escape restore what was there before.
/// </para>
/// </remarks>
public abstract class CangerCommand
{
    /// <summary>The file manager being acted on.</summary>
    protected IFileManager FileManager { get; private set; } = null!;

    /// <summary>The line as typed, split the several ways commands need.</summary>
    protected CommandLine Line { get; private set; } = null!;

    /// <summary>A numeric prefix typed before the key that fired this, when there was one.</summary>
    protected int? Quantifier { get; private set; }

    /// <summary>The quantifier, or 1 when none was typed.</summary>
    protected int Repeat => Math.Max(Quantifier ?? 1, 1);

    /// <summary>Prepares the command to run.</summary>
    /// <param name="fileManager">What to act on.</param>
    /// <param name="line">The line as typed.</param>
    /// <param name="quantifier">A numeric prefix, when one was typed.</param>
    public virtual void Initialize(IFileManager fileManager, CommandLine line,
                                   int? quantifier)
    {
        FileManager = fileManager;
        Line = line;
        Quantifier = quantifier;
    }

    /// <summary>Runs the command.</summary>
    public abstract void Execute();

    /// <summary>
    /// Offers completions for what has been typed so far.
    /// </summary>
    /// <param name="direction">
    /// Which way the user is cycling: 1 for Tab, -1 for Shift-Tab.
    /// </param>
    /// <returns>
    /// The lines to cycle through, or an empty list when the command has no completions.
    /// </returns>
    public virtual IReadOnlyList<string> Complete(int direction) => [];

    /// <summary>
    /// Acts on each keystroke, before the user presses Enter.
    /// </summary>
    /// <remarks>
    /// This is what makes a filter narrow the listing as it is typed, rather than only once it
    /// is finished. Returning <see langword="true"/> runs the command and closes the prompt,
    /// which is how a search jumps as soon as the match is unambiguous.
    /// </remarks>
    /// <returns><see langword="true"/> to run the command and close the prompt.</returns>
    public virtual bool Quick() => false;

    /// <summary>
    /// Undoes anything <see cref="Quick"/> did, when the user abandons the prompt.
    /// </summary>
    public virtual void Cancel()
    {
    }

    /// <summary>One word of the line, with the command name at index 0.</summary>
    /// <param name="index">Which word.</param>
    /// <returns>The word, or an empty string.</returns>
    protected string Argument(int index) => Line.Word(index);

    /// <summary>Everything from a word onwards, with the original spacing intact.</summary>
    /// <param name="index">Which word to start from.</param>
    /// <returns>The rest of the line.</returns>
    protected string Rest(int index) => Line.Rest(index);

    /// <summary>
    /// Reads the <c>name=value</c> arguments a line carries.
    /// </summary>
    /// <returns>The names and their textual values; words without an <c>=</c> are ignored.</returns>
    /// <remarks>
    /// This is how a binding describes what it wants without needing a parser per command:
    /// <c>move down=1 pages=True</c>, <c>cut to=0</c>, <c>rename_append where=end</c>. Values are
    /// left as text because each command knows what shape its own arguments take.
    /// </remarks>
    protected Dictionary<string, string> NamedArguments()
    {
        Dictionary<string, string> named = new(StringComparer.Ordinal);

        for (int i = 1; i < Line.Count; i++)
        {
            string word = Argument(i);
            int equals = word.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                named[word[..equals]] = word[(equals + 1)..];
            }
        }

        return named;
    }

    /// <summary>Completes against the entries of the current directory.</summary>
    /// <returns>The candidate lines.</returns>
    protected IReadOnlyList<string> CompleteDirectoryContent()
    {
        string typed = Rest(1);
        string prefix = Line.Word(0) + " ";

        return
        [
            .. FileManager.CurrentDirectory.Entries
                .Select(e => e.RelativePath)
                .Where(name => name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(name => prefix + name),
        ];
    }

    /// <summary>Completes a directory path being typed.</summary>
    /// <param name="includeBookmarks">
    /// Whether bookmarks under a candidate are offered alongside it. The <c>cd_bookmarks</c>
    /// setting, which is on by default and which <c>:cd</c> asks for.
    /// </param>
    /// <returns>The candidate lines.</returns>
    /// <remarks>
    /// The typed text is split into the directory part and the partial name, and the directory
    /// part is what gets listed. Without that only names in the current directory completed, so
    /// <c>:cd /usr/lo</c> and <c>:cd ~/Doc</c> did nothing at all — which is most of the typing a
    /// <c>:cd</c> saves. Ranger splits it the same way (<c>config/commands.py:291-297</c>).
    /// </remarks>
    protected IReadOnlyList<string> CompleteDirectories(bool includeBookmarks = false)
    {
        string typed = Rest(1);
        string prefix = Line.Word(0) + " ";

        // What the user typed up to the last separator is echoed back unchanged, so a `~` stays a
        // `~` and an absolute path stays absolute rather than being rewritten under their feet.
        int separator = typed.LastIndexOf('/');
        string head = separator < 0 ? string.Empty : typed[..(separator + 1)];
        string tail = separator < 0 ? typed : typed[(separator + 1)..];

        string directory = FileManager.CurrentDirectory.Path;

        if (head.Length > 0)
        {
            // Resolved against the listing, not the process's working directory — which is what
            // `Expand` falls back to, and which is wherever Canger happened to be started from
            // rather than where the user is now.
            directory = FileSystem.UserPath.Expand(head, directory);
        }

        List<string> candidates = [];

        try
        {
            foreach (string entry in System.IO.Directory.EnumerateDirectories(directory))
            {
                string name = System.IO.Path.GetFileName(entry);

                if (name.StartsWith(tail, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(name);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A path being typed is unreadable more often than not; offering nothing is the
            // right answer and refusing to complete anything else would not be.
        }

        candidates.Sort(StringComparer.Ordinal);

        List<string> lines = [.. candidates.Select(name => prefix + head + name + "/")];

        if (!includeBookmarks)
        {
            return lines;
        }

        // A bookmark that lies under one of the candidates is offered beside it, so a place you
        // named once is reachable without typing the rest of the way to it. Ranger puts these
        // first (`config/commands.py:277-282`), where they are seen before the plain directories.
        List<string> bookmarks = [];

        foreach (string target in FileManager.Bookmarks.Entries.Values)
        {
            foreach (string name in candidates)
            {
                string under = System.IO.Path.Join(directory, name) + "/";

                if (target.StartsWith(under, StringComparison.Ordinal))
                {
                    bookmarks.Add(prefix + head + System.IO.Path.GetRelativePath(directory, target));
                    break;
                }
            }
        }

        bookmarks.Sort(StringComparer.Ordinal);
        lines.InsertRange(0, bookmarks);

        return lines;
    }
}
