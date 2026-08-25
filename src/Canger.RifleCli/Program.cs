// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;
using Canger.Core.FileSystem;
using Canger.Rifle;
using Canger.Tui;

namespace Canger.RifleCli;

/// <summary>
/// The file launcher as a program in its own right.
/// </summary>
/// <remarks>
/// Rifle is useful outside Canger: it answers "what should open this?" for a shell, a script or
/// another program, using the same rules the browser uses. Ranger ships it separately for the
/// same reason.
/// </remarks>
internal static class Program
{
    /// <summary>Runs rifle.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A process exit code.</returns>
    internal static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"rifle: {e.Message}");
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (options.Files.Count == 0)
        {
            PrintUsage();
            return 2;
        }

        RifleConfiguration configuration = LoadRules(options.ConfigPath);

        foreach (RifleConfigurationError error in configuration.Errors)
        {
            Console.Error.WriteLine($"rifle: line {error.LineNumber}: {error.Message}");
        }

        RifleLauncher launcher = new(configuration, new TerminalProcessRunner(), LocalFileSystem.Instance);

        if (options.List)
        {
            return ListAlternatives(launcher, options.Files[0]);
        }

        RifleResult result = launcher.Open(
            options.Files, options.Number, options.Label, options.Flags);

        switch (result.Outcome)
        {
            case RifleOutcome.Opened:
                return 0;

            case RifleOutcome.AskUser:
                Console.Error.WriteLine(
                    "rifle: the rules say to ask; choose one with -p or -l");
                return ListAlternatives(launcher, options.Files[0]);

            default:
                Console.Error.WriteLine($"rifle: {result.Error ?? result.Outcome.ToString()}");
                return 1;
        }
    }

    /// <summary>Prints the ways a file could be opened, in ranger's format.</summary>
    private static int ListAlternatives(RifleLauncher launcher, string path)
    {
        foreach (NumberedMatch alternative in launcher.Alternatives(path))
        {
            Console.WriteLine(
                $"{alternative.Number}:{alternative.Match.Label}:" +
                $"{alternative.Match.Flags}:{alternative.Match.Command}");
        }

        return 0;
    }

    /// <summary>
    /// Finds the rules, preferring an explicit path, then the user's, then the shipped ones.
    /// </summary>
    private static RifleConfiguration LoadRules(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            return File.Exists(explicitPath)
                ? RifleConfiguration.FromFile(explicitPath)
                : throw new FileNotFoundException($"no such file: {explicitPath}", explicitPath);
        }

        CangerPaths paths = new();

        foreach (string candidate in (string[])
                 [
                     paths.Config("rifle.conf"),
                     Path.Join(CangerPaths.SystemConfigDirectory, "rifle.conf"),
                     Path.Join(CangerPaths.InstallDirectory, "config", "rifle.conf"),
                 ])
        {
            if (File.Exists(candidate))
            {
                return RifleConfiguration.FromFile(candidate);
            }
        }

        return RifleConfiguration.FromLines([]);
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            usage: rifle [options] file [file ...]

            Opens files with the program the rules choose.

              -f FLAGS   run with these flags: s silent, f fork, p pipe to a pager,
                         w wait for a key, r as root, t in a new terminal window.
                         A capital letter cancels its lowercase counterpart.
              -l         list the ways the file could be opened, one per line, as
                         number:label:flags:command
              -p KEYWORD use this alternative: a number counts from zero, anything
                         else is treated as a label
              -c FILE    read rules from FILE instead of the usual places
              -h         show this message

            Rules are read from ~/.config/canger/rifle.conf, then /etc/canger/rifle.conf,
            then the copy shipped with Canger.
            """);

    /// <summary>What was asked for on the command line.</summary>
    private sealed record Options
    {
        public List<string> Files { get; } = [];

        public string Flags { get; private set; } = string.Empty;

        public bool List { get; private set; }

        public int Number { get; private set; }

        public string? Label { get; private set; }

        public string? ConfigPath { get; private set; }

        public bool ShowHelp { get; private set; }

        public static Options Parse(string[] args)
        {
            Options options = new();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h" or "--help":
                        options.ShowHelp = true;
                        break;

                    case "-l":
                        options.List = true;
                        break;

                    case "-f":
                        options.Flags = Next(args, ref i, "-f needs the flags to use");
                        break;

                    case "-c":
                        options.ConfigPath = Next(args, ref i, "-c needs a path");
                        break;

                    case "-p":
                        {
                            string keyword = Next(args, ref i, "-p needs a number or a label");

                            // A number counts alternatives; anything else names one.
                            if (int.TryParse(keyword, out int number))
                            {
                                options.Number = number;
                            }
                            else
                            {
                                options.Label = keyword;
                            }

                            break;
                        }

                    default:
                        options.Files.Add(args[i]);
                        break;
                }
            }

            return options;
        }

        private static string Next(string[] args, ref int index, string message) =>
            index + 1 < args.Length ? args[++index] : throw new ArgumentException(message);
    }
}
