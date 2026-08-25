// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Configuration;

/// <summary>
/// One problem found while reading a configuration file.
/// </summary>
/// <remarks>
/// A bad line never aborts the load. Ranger reports the line and carries on
/// (<c>ranger/core/actions.py:369-389</c>), and so does Canger — one typo in cc.conf should
/// not leave the user with an unconfigured file manager.
/// </remarks>
/// <param name="File">The file the problem was found in.</param>
/// <param name="LineNumber">The one-based line number.</param>
/// <param name="Line">The line as written.</param>
/// <param name="Message">What went wrong.</param>
public readonly record struct ConfigurationError(
    string File, int LineNumber, string Line, string Message)
{
    /// <summary>Renders the error the way it is shown to the user.</summary>
    /// <returns>A single-line description.</returns>
    public override string ToString() => $"{File}:{LineNumber}: {Message}  ({Line})";
}

/// <summary>
/// Reads cc.conf files and dispatches each line to the directive that handles it.
/// </summary>
/// <remarks>
/// <para>
/// Files are read in order and applied additively, so the shipped defaults, then
/// <c>/etc/canger/cc.conf</c>, then the user's own file each build on the last. That is why a
/// personal cc.conf can contain only the handful of lines the user wants to change.
/// </para>
/// <para>
/// Order within a file matters and is preserved strictly. The keybinding trie stores leaves and
/// interior nodes in the same structure, so binding <c>gg</c> after <c>g</c> discards the
/// <c>g</c> binding, and <c>copymap</c> duplicates whatever is at the source path at the moment
/// it runs. Reordering lines would silently change which keys work.
/// </para>
/// </remarks>
public sealed class ConfigurationReader
{
    private readonly Dictionary<string, IConfigurationDirective> _directives =
        new(StringComparer.Ordinal);

    private readonly List<ConfigurationError> _errors = [];

    /// <summary>
    /// Tried for any line no directive claims, and expected to return whether it handled it.
    /// </summary>
    /// <remarks>
    /// cc.conf is not a distinct file format: every line is a command, the same ones the prompt
    /// accepts. A handful — <c>set</c>, <c>map</c>, <c>alias</c> — need to reach subsystems the
    /// command layer knows nothing about, so they are registered as directives; everything else
    /// is simply a command. Without this, a command that is perfectly usable at the prompt is
    /// rejected in a configuration file, which is how <c>default_linemode</c> came to do nothing.
    /// </remarks>
    public Func<string, bool>? Fallback { get; set; }

    /// <summary>Problems found so far, in the order they were encountered.</summary>
    public IReadOnlyList<ConfigurationError> Errors => _errors;

    /// <summary>
    /// Registers a directive handler. Subsystems call this to contribute their own directives.
    /// </summary>
    /// <param name="directive">The handler.</param>
    /// <returns>This reader, so registrations can be chained.</returns>
    public ConfigurationReader Register(IConfigurationDirective directive)
    {
        ArgumentNullException.ThrowIfNull(directive);

        foreach (string name in directive.Names)
        {
            _directives[name] = directive;
        }

        return this;
    }

    /// <summary>Whether a directive name has a handler registered.</summary>
    /// <param name="name">The directive name.</param>
    /// <returns><see langword="true"/> when the name is handled.</returns>
    public bool Handles(string name) => _directives.ContainsKey(name);

    /// <summary>
    /// Reads every existing file in a sequence, applying them in order.
    /// </summary>
    /// <param name="paths">Candidate paths. Missing files are skipped without complaint.</param>
    public void ReadFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                ReadFile(path);
            }
        }
    }

    /// <summary>Reads a single configuration file.</summary>
    /// <param name="path">Path to the file.</param>
    public void ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            ReadLines(File.ReadLines(path), path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _errors.Add(new ConfigurationError(path, 0, string.Empty, e.Message));
        }
    }

    /// <summary>
    /// Reads configuration from a sequence of lines. Exposed so tests and the <c>--cmd</c>
    /// command-line option can feed configuration in without a file.
    /// </summary>
    /// <param name="lines">The lines to read.</param>
    /// <param name="origin">A name for the source, used when reporting errors.</param>
    public void ReadLines(IEnumerable<string> lines, string origin = "<input>")
    {
        ArgumentNullException.ThrowIfNull(lines);

        int lineNumber = 0;
        foreach (string line in lines)
        {
            lineNumber++;
            ExecuteLine(line, origin, lineNumber);
        }
    }

    /// <summary>
    /// Executes one configuration line, recording rather than throwing on failure.
    /// </summary>
    /// <param name="line">The line as written.</param>
    /// <param name="origin">A name for the source, used when reporting errors.</param>
    /// <param name="lineNumber">The one-based line number, used when reporting errors.</param>
    /// <returns><see langword="true"/> when the line was executed.</returns>
    public bool ExecuteLine(string line, string origin = "<input>", int lineNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(line);

        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return true;
        }

        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        string name = space < 0 ? trimmed : trimmed[..space];
        string arguments = space < 0 ? string.Empty : trimmed[(space + 1)..];

        if (!_directives.TryGetValue(name, out IConfigurationDirective? directive))
        {
            if (Fallback is { } fallback)
            {
                try
                {
                    if (fallback(trimmed))
                    {
                        return true;
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException
                                              and not StackOverflowException)
                {
                    _errors.Add(new ConfigurationError(origin, lineNumber, trimmed, e.Message));
                    return false;
                }
            }

            _errors.Add(new ConfigurationError(
                origin, lineNumber, trimmed, $"unknown directive '{name}'"));
            return false;
        }

        try
        {
            directive.Execute(name, arguments);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            _errors.Add(new ConfigurationError(origin, lineNumber, trimmed, e.Message));
            return false;
        }
    }

    /// <summary>Discards recorded errors, so a subsequent load reports only its own problems.</summary>
    public void ClearErrors() => _errors.Clear();
}
