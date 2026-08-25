// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Canger.Core.Model;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// Adds a rule deciding what listings show beside each name.
/// </summary>
/// <remarks>
/// Belongs in the configuration rather than at the prompt: it says how a kind of file should
/// always look, whereas <see cref="LinemodeCommand"/> changes one listing for as long as it is
/// open.
/// </remarks>
[Command("default_linemode",
         Summary = "Choose the linemode for matching entries: default_linemode "
                 + "[path=<regex> | tag=<tags>] <linemode>")]
public sealed class DefaultLinemodeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string first = Argument(1);
        LinemodeScope scope = LinemodeScope.Always;
        Regex? pattern = null;
        string? tags = null;
        int modeIndex = 1;

        if (first.StartsWith("path=", StringComparison.Ordinal))
        {
            scope = LinemodeScope.Path;
            modeIndex = 2;

            if (!TryCompile(first["path=".Length..], out pattern))
            {
                return;
            }
        }
        else if (first.StartsWith("tag=", StringComparison.Ordinal))
        {
            scope = LinemodeScope.Tag;
            tags = first["tag=".Length..];
            modeIndex = 2;
        }

        string mode = Rest(modeIndex);

        if (mode.Length == 0)
        {
            FileManager.Notify(
                "Usage: default_linemode [path=<regex> | tag=<tags>] <linemode>", isError: true);
            return;
        }

        if (FileManager.Linemodes.Registry.Find(mode) is null)
        {
            FileManager.Notify(
                $"Invalid linemode: {mode}; should be "
                + string.Join('/', FileManager.Linemodes.Registry.Names),
                isError: true);
            return;
        }

        FileManager.Linemodes.Add(new LinemodeRule(scope, pattern, tags, mode));
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) =>
        LinemodeCompletion.For(FileManager, Line);

    /// <summary>
    /// Compiles a path pattern, reporting a bad one rather than letting it escape.
    /// </summary>
    /// <remarks>
    /// A configuration file is read before the user can see anything, so an unhandled regular
    /// expression error here would take the whole startup down over one mistyped line.
    /// </remarks>
    private bool TryCompile(string source, out Regex? pattern)
    {
        try
        {
            pattern = new Regex(source, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException error)
        {
            FileManager.Notify($"Invalid path pattern: {error.Message}", isError: true);
            pattern = null;
            return false;
        }
    }
}

/// <summary>
/// Changes what the current listing shows beside each name.
/// </summary>
/// <remarks>
/// The change lives on the entries themselves and so lasts until the directory is reloaded,
/// matching ranger. To make it stick, use <c>default_linemode</c> in the configuration.
/// </remarks>
[Command("linemode", Summary = "Change what this listing shows beside each name.")]
public sealed class LinemodeCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string mode = Rest(1);

        // "normal" is what ranger's own bindings ask for, and it means the default.
        if (mode is "normal")
        {
            mode = LinemodeRegistry.DefaultName;
        }

        if (FileManager.Linemodes.Registry.Find(mode) is null)
        {
            FileManager.Notify($"Unhandled linemode: `{mode}'", isError: true);
            return;
        }

        foreach (Model.FsNode entry in FileManager.CurrentDirectory.Entries)
        {
            entry.LinemodeOverride = mode;
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) =>
        LinemodeCompletion.For(FileManager, Line);
}

/// <summary>Completion shared by the two linemode commands.</summary>
internal static class LinemodeCompletion
{
    /// <summary>Offers the registered mode names.</summary>
    /// <param name="fileManager">Where the registry lives.</param>
    /// <param name="line">The line being completed.</param>
    /// <returns>The candidate lines.</returns>
    public static IReadOnlyList<string> For(IFileManager fileManager, CommandLine line)
    {
        // The scope prefix, if any, is kept so completing the mode does not discard it.
        string typed = line.Rest(1);
        string prefix = line.Word(0);
        string scope = string.Empty;

        if (typed.StartsWith("path=", StringComparison.Ordinal)
            || typed.StartsWith("tag=", StringComparison.Ordinal))
        {
            scope = line.Word(1) + " ";
            typed = line.Rest(2);
        }

        return
        [
            .. fileManager.Linemodes.Registry.Names
                .Where(name => name.StartsWith(typed, StringComparison.Ordinal))
                .Select(name => $"{prefix} {scope}{name}")
        ];
    }
}
