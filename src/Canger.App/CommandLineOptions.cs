// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.App;

/// <summary>
/// What the command line asked for.
/// </summary>
/// <remarks>
/// <para>
/// The names follow ranger's, so an existing shell function or desktop entry works against Canger
/// unchanged. That is what makes the chooser flags worth having at all: they exist so that
/// <em>other</em> programs can use Canger to pick a file, and those callers already know ranger's
/// spelling.
/// </para>
/// <para>
/// Unknown options are collected rather than ignored, because a mistyped flag that silently does
/// nothing is worse than one that says so.
/// </para>
/// </remarks>
public sealed class CommandLineOptions
{
    /// <summary>Directories to open, one per tab.</summary>
    public List<string> Paths { get; } = [];

    /// <summary>Print usage and exit.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Print the version and exit.</summary>
    public bool ShowVersion { get; private set; }

    /// <summary>
    /// Ignore every configuration file, plugin and piece of saved state.
    /// </summary>
    /// <remarks>
    /// For reproducing a problem without a personal configuration in the way, and for scripts
    /// that must behave the same on every machine. Nothing is written back either.
    /// </remarks>
    public bool Clean { get; private set; }

    /// <summary>Which shipped configuration files to copy into the user's directory.</summary>
    public string? CopyConfig { get; private set; }

    /// <summary>Write the opened file's path here and quit.</summary>
    public string? ChooseFile { get; private set; }

    /// <summary>Write every selected file's path here and quit.</summary>
    public string? ChooseFiles { get; private set; }

    /// <summary>Write the last visited directory here on quitting.</summary>
    public string? ChooseDir { get; private set; }

    /// <summary>Start with this file under the cursor.</summary>
    public string? SelectFile { get; private set; }

    /// <summary>Commands to run once the configuration has been read.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Override the configuration directory.</summary>
    public string? ConfigDirectory { get; private set; }

    /// <summary>Override the data directory.</summary>
    public string? DataDirectory { get; private set; }

    /// <summary>Override the cache directory.</summary>
    public string? CacheDirectory { get; private set; }

    /// <summary>Show only directories.</summary>
    public bool ShowOnlyDirectories { get; private set; }

    /// <summary>Print the files carrying a tag, then exit.</summary>
    public string? ListTaggedFiles { get; private set; }

    /// <summary>Report what the configuration produced, then exit.</summary>
    public bool ReportConfiguration { get; private set; }

    /// <summary>Print a listing without taking over the terminal, then exit.</summary>
    public bool ListOnly { get; private set; }

    /// <summary>Report what each key decodes to, then exit.</summary>
    public bool KeyProbe { get; private set; }

    /// <summary>Write the manual page to standard output, then exit.</summary>
    public bool ManPage { get; private set; }

    /// <summary>Options that were not recognised.</summary>
    public List<string> Unknown { get; } = [];

    /// <summary>Whether any chooser flag was given.</summary>
    public bool IsChooser => ChooseFile is not null || ChooseFiles is not null;

    /// <summary>
    /// Reads the command line.
    /// </summary>
    /// <param name="args">The arguments, without the program name.</param>
    /// <returns>What was asked for.</returns>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        CommandLineOptions options = new();

        for (int i = 0; i < args.Count; i++)
        {
            string argument = args[i];

            if (!argument.StartsWith('-') || argument == "-")
            {
                options.Paths.Add(argument);
                continue;
            }

            // Both spellings are accepted, since ranger's own documentation uses each in places.
            (string name, string? inline) = Split(argument);

            // Reads the value of an option, from either `--name=value` or the next argument.
            string? Value()
            {
                if (inline is not null)
                {
                    return inline;
                }

                return i + 1 < args.Count ? args[++i] : null;
            }

            switch (name)
            {
                case "-h" or "--help": options.ShowHelp = true; break;
                case "--version": options.ShowVersion = true; break;
                case "-c" or "--clean": options.Clean = true; break;
                case "--show-only-dirs": options.ShowOnlyDirectories = true; break;
                case "--config": options.ReportConfiguration = true; break;
                case "--list": options.ListOnly = true; break;
                case "--key-probe": options.KeyProbe = true; break;
                case "--man": options.ManPage = true; break;

                case "--copy-config": options.CopyConfig = Value() ?? "all"; break;
                case "--choosefile": options.ChooseFile = Value(); break;
                case "--choosefiles": options.ChooseFiles = Value(); break;
                case "--choosedir": options.ChooseDir = Value(); break;
                case "--selectfile": options.SelectFile = Value(); break;
                case "-r" or "--confdir": options.ConfigDirectory = Value(); break;
                case "--datadir": options.DataDirectory = Value(); break;
                case "--cachedir": options.CacheDirectory = Value(); break;
                case "--list-tagged-files": options.ListTaggedFiles = Value() ?? "*"; break;

                // Repeatable, so several commands can be run in the order they were given.
                case "--cmd":
                    if (Value() is { } command)
                    {
                        options.Commands.Add(command);
                    }

                    break;

                default:
                    options.Unknown.Add(argument);
                    break;
            }
        }

        return options;
    }

    /// <summary>Separates <c>--name=value</c> into its two halves.</summary>
    private static (string Name, string? Value) Split(string argument)
    {
        int equals = argument.IndexOf('=', StringComparison.Ordinal);

        return equals < 0
            ? (argument, null)
            : (argument[..equals], argument[(equals + 1)..]);
    }

    /// <summary>The usage text, in the order the options are most often wanted.</summary>
    public static string Usage =>
        """
        usage: canger [options] [path ...]

        A file manager for the terminal.

        Each path opens in a tab of its own. A path naming a file rather than a
        directory opens the directory holding it, with that file under the cursor.

        Options:
          -h, --help              show this message and exit
              --version           show the version and exit
          -c, --clean             ignore all configuration, plugins and saved state
          -r, --confdir=DIR       use a different configuration directory
              --datadir=DIR       use a different data directory
              --cachedir=DIR      use a different cache directory
              --copy-config=WHICH copy shipped configuration into the config directory;
                                  one of: all, cc, rifle, commands, scope
              --cmd=COMMAND       run COMMAND after reading the configuration (repeatable)
              --selectfile=FILE   start with FILE under the cursor
              --show-only-dirs    show only directories

        Acting as a chooser for another program:
              --choosefile=OUT    write the opened file's path to OUT and quit
              --choosefiles=OUT   write every selected file's path to OUT and quit
              --choosedir=OUT     write the last visited directory to OUT on quitting

        Inspecting things a full-screen interface would hide:
              --config            report what the configuration produced
              --list              print a listing without taking over the terminal
              --key-probe         report what each key decodes to
              --man               write the manual page to standard output
              --list-tagged-files[=TAG]
                                  print the files carrying a tag
        """;
}
