// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using Canger.Core.FileSystem;
using Canger.Core.Model;
using Canger.Core.Processes;

namespace Canger.Rifle;

/// <summary>Why a file could not be opened.</summary>
public enum RifleOutcome
{
    /// <summary>A program was chosen and run.</summary>
    Opened,

    /// <summary>The configuration says to ask which program to use.</summary>
    AskUser,

    /// <summary>No rule applies to the file.</summary>
    NoRuleMatched,

    /// <summary>The requested alternative does not exist.</summary>
    NoSuchAlternative,

    /// <summary>The program could not be started.</summary>
    Failed,
}

/// <summary>What happened when rifle tried to open a file.</summary>
/// <param name="Outcome">Whether it worked, and if not why.</param>
/// <param name="Command">The command line that was run, or would have been.</param>
/// <param name="Label">The name of the way the file was opened.</param>
/// <param name="Error">The reason it failed, when it did.</param>
public readonly record struct RifleResult(
    RifleOutcome Outcome,
    string Command = "",
    string? Label = null,
    string? Error = null)
{
    /// <summary>Whether the file was opened.</summary>
    public bool Succeeded => Outcome == RifleOutcome.Opened;
}

/// <summary>
/// Decides which program opens a file, and runs it.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece that makes a file manager useful: pressing Enter on a file should do the
/// obvious thing, whatever the file is. The rules encode what "obvious" means, and this walks
/// them in order and runs the first that applies.
/// </para>
/// <para>
/// The command is handed to a shell as <c>set -- 'file1' 'file2'; command</c>, which is what lets
/// a rule refer to the files as <c>"$@"</c> or <c>"$1"</c> and have quoting handled once, here,
/// rather than in every rule.
/// </para>
/// </remarks>
public sealed class RifleLauncher(RifleConfiguration configuration, IProcessRunner runner,
                                  IFileSystem fileSystem) : IFileOpener
{
    private readonly RifleConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    private readonly IProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>The rules in use.</summary>
    public RifleConfiguration Configuration => _configuration;

    /// <summary>
    /// Lists the ways a file could be opened, most preferred first.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The alternatives, numbered as <c>:open_with</c> counts them.</returns>
    public IReadOnlyList<NumberedMatch> Alternatives(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return _configuration.Candidates(BuildContext(path, label: null));
    }

    /// <summary>
    /// Opens files with whichever rule applies.
    /// </summary>
    /// <param name="paths">The files, which are passed to the command together.</param>
    /// <param name="number">Which alternative to use, counting from zero.</param>
    /// <param name="label">Use the alternative with this name instead of counting.</param>
    /// <param name="extraFlags">Flags to add to whatever the rule asked for.</param>
    /// <returns>What happened.</returns>
    public RifleResult Open(IReadOnlyList<string> paths, int number = 0, string? label = null,
                            string extraFlags = "")
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return new RifleResult(RifleOutcome.NoRuleMatched, Error: "no files given");
        }

        // Rules are matched against the first file; a selection is assumed to be homogeneous,
        // as ranger assumes.
        RifleContext context = BuildContext(paths[0], label);
        IReadOnlyList<NumberedMatch> candidates = _configuration.Candidates(context);

        if (candidates.Count == 0)
        {
            return new RifleResult(RifleOutcome.NoRuleMatched,
                                   Error: $"no rule matches {Path.GetFileName(paths[0])}");
        }

        NumberedMatch? chosen = label is not null
            ? candidates.Cast<NumberedMatch?>().FirstOrDefault(
                c => string.Equals(c!.Value.Match.Label, label, StringComparison.Ordinal))
            : candidates.Cast<NumberedMatch?>().FirstOrDefault(c => c!.Value.Number == number);

        if (chosen is not { } selected)
        {
            return new RifleResult(
                RifleOutcome.NoSuchAlternative,
                Error: label is not null
                    ? $"no way to open this called '{label}'"
                    : $"there is no alternative {number}");
        }

        if (selected.Match.IsAsk)
        {
            return new RifleResult(RifleOutcome.AskUser, Label: selected.Match.Label);
        }

        string command = BuildCommand(paths, selected.Match.Command);
        ProcessFlags flags = new(selected.Match.Flags + extraFlags);

        ProcessResult result = _runner.Run(new ProcessRequest(
            command, flags, Path.GetDirectoryName(Path.GetFullPath(paths[0]))));

        return result.Error is null
            ? new RifleResult(RifleOutcome.Opened, command, selected.Match.Label)
            : new RifleResult(RifleOutcome.Failed, command, selected.Match.Label, result.Error);
    }

    /// <summary>
    /// Builds the shell command for a rule.
    /// </summary>
    /// <remarks>
    /// The files are placed in the shell's positional parameters rather than substituted into the
    /// command text, so a rule writes <c>"$@"</c> and never has to think about quoting. Names
    /// containing a NUL byte are dropped, because they cannot survive the shell at all.
    /// </remarks>
    /// <param name="paths">The files.</param>
    /// <param name="action">The command from the rule.</param>
    /// <returns>The shell command line.</returns>
    public static string BuildCommand(IReadOnlyList<string> paths, string action)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(action);

        StringBuilder command = new("set --");

        foreach (string path in paths)
        {
            if (path.Contains('\0', StringComparison.Ordinal))
            {
                continue;
            }

            command.Append(' ').Append(Quote(path));
        }

        return command.Append("; ").Append(action).ToString();
    }

    /// <summary>Wraps a value so a shell treats it as one argument whatever it contains.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted value.</returns>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
    }

    /// <inheritdoc />
    IReadOnlyList<OpenAlternative> IFileOpener.Alternatives(string path) =>
        [.. Alternatives(path).Select(
            a => new OpenAlternative(a.Number, a.Match.Label, a.Match.Command))];

    /// <inheritdoc />
    OpenResult IFileOpener.Open(IReadOnlyList<string> paths, int number, string? label,
                                string flags)
    {
        RifleResult result = Open(paths, number, label, flags);

        return new OpenResult(
            result.Succeeded,
            result.Outcome == RifleOutcome.AskUser,
            result.Error ?? (result.Succeeded ? null : result.Outcome.ToString()));
    }

    /// <summary>Gathers what the conditions need to know about a file.</summary>
    private RifleContext BuildContext(string path, string? label) => new(
        path,
        DetectMimeType(path),
        label,
        HasGraphicalDisplay(),
        HasTerminal: !Console.IsInputRedirected,
        _fileSystem);

    /// <summary>Whether a graphical session is available, for the <c>X</c> condition.</summary>
    private static bool HasGraphicalDisplay() =>
        Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 } ||
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 };

    /// <summary>
    /// Works out what kind of file this is, the way ranger does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three steps, in this order, because each covers the previous one's blind spot:
    /// </para>
    /// <list type="number">
    /// <item><description>The filename's extension. Cheap, and right far more often than
    /// content inspection — an MP3 with an unusual tag reads as
    /// <c>application/octet-stream</c> from <c>file(1)</c>, matches no audio rule, and gets
    /// handed to whatever the desktop calls a default handler.</description></item>
    /// <item><description><c>file --mime-type</c>, for names that say nothing.</description></item>
    /// <item><description><c>mimetype(1)</c>, but only when <c>file</c> gave up — it consults
    /// the shared MIME database, which knows formats <c>file</c> does not.</description></item>
    /// </list>
    /// </remarks>
    private static string? DetectMimeType(string path)
    {
        if (MimeTypes.FromExtension(path) is { } byName)
        {
            return byName;
        }

        string? detected = RunDetector("file", "--mime-type", "-Lb", path);

        // "octet-stream" is file(1) saying it does not know, not an answer.
        return detected is null or "application/octet-stream"
            ? RunDetector("mimetype", "--output-format", "%m", path) ?? detected
            : detected;
    }

    /// <summary>Runs a detection program and returns the single line it prints.</summary>
    private static string? RunDetector(string program, params string[] arguments)
    {
        try
        {
            ProcessStartInfo start = new()
            {
                FileName = program,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output.Length > 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The program is not installed, which is not an error: the next step in the
            // cascade gets its turn, and failing all of them degrades to extension rules.
            return null;
        }
    }
}
