// SPDX-License-Identifier: GPL-3.0-or-later
// The ranger zoxide plugin, ported.
//
// Two halves, as the original has: a `z` command that asks zoxide where you probably mean, and a
// hook that tells zoxide about every directory visited so it has something to go on. Without the
// hook the database never learns anything from Canger and `z` only knows what other shells taught
// it.

/// <summary>Jumps to a directory zoxide remembers.</summary>
/// <remarks>
/// <para>
/// <c>:z &lt;words&gt;</c> jumps to the best match. <c>:zi</c> — aliased by the plugin below —
/// passes <c>-i</c>, which makes zoxide open its own interactive picker.
/// </para>
/// <para>
/// A query that matches nothing but happens to name a real directory is treated as that
/// directory, so <c>:z /etc</c> works whether or not zoxide has heard of it.
/// </para>
/// </remarks>
[Command("z", Summary = "Jump to a directory with zoxide: z <words>")]
public sealed class ZoxideCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!Executables.Exists("zoxide"))
        {
            FileManager.Notify("Could not find zoxide in the PATH.", isError: true);
            return;
        }

        string query = Rest(1).Trim();
        bool interactive = query.Contains("-i", StringComparison.Ordinal);

        // --exclude "$PWD" so `z` never answers with where you already are, which would look
        // like the key doing nothing.
        string command = $"zoxide query --exclude {ShellQuote(FileManager.CurrentDirectory.Path)} "
                         + query;

        // The interactive form draws a picker and must be given the terminal; the plain form is a
        // one-line answer. Both want standard output, so both go the same way.
        ProcessResult result = FileManager.Runner.RunCapturingOutput(
            new ProcessRequest(command, ProcessFlags.None, FileManager.CurrentDirectory.Path));

        if (result.Error is { } error)
        {
            FileManager.Notify(error, isError: true);
            return;
        }

        string? answer = result.ExitCode == 0
            ? result.Output.Split('\n').Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0)
            : null;

        // Exit 1 is "nothing found" and 130 is the picker being abandoned; neither is an error
        // worth a message.
        if (answer is null)
        {
            string literal = query.Replace("-i", string.Empty, StringComparison.Ordinal).Trim();

            if (literal.Length > 0 && Directory.Exists(literal))
            {
                Enter(literal);
            }
            else if (result.ExitCode is not (0 or 1 or 130) && !interactive)
            {
                FileManager.Notify($"zoxide: exit {result.ExitCode}", isError: true);
            }

            return;
        }

        if (Directory.Exists(answer))
        {
            Enter(answer);
        }
        else
        {
            FileManager.Notify($"{answer}: gone", isError: true);
        }
    }

    /// <inheritdoc />
    /// <remarks>Completes from zoxide's own ranking, so Tab offers the likeliest places.</remarks>
    public override IReadOnlyList<string> Complete(int direction)
    {
        string query = Rest(1).Trim();

        if (!Executables.Exists("zoxide"))
        {
            return [];
        }

        ProcessResult result = FileManager.Runner.Run(new ProcessRequest(
            $"zoxide query --list {query}", new ProcessFlags("ps"),
            FileManager.CurrentDirectory.Path));

        return
        [
            .. result.Output.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .Take(20)
                    .Select(line => $"z {line}"),
        ];
    }

    private void Enter(string path)
    {
        FileManager.CurrentTab.Enter(path);
        FileManager.ReloadCurrentDirectory();
    }

    /// <summary>Quotes one word for the shell.</summary>
    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}

/// <summary>
/// Teaches zoxide where you go, and gives <c>zi</c> its meaning.
/// </summary>
/// <remarks>
/// The database is the whole point: a `z` that has never been told anything can only jump to
/// places other programs recorded. Ranger binds its <c>cd</c> signal for this; Canger raises
/// <see cref="IFileManager.DirectoryEntered"/>.
/// </remarks>
public sealed class ZoxidePlugin : ICangerPlugin
{
    /// <inheritdoc />
    public void OnInit(IFileManager fileManager)
    {
        // As in the ranger plugin: `zi` is `z -i`, zoxide's interactive picker.
        fileManager.Commands.Alias("zi", "z -i");

        fileManager.DirectoryEntered += (_, path) =>
        {
            if (!Executables.Exists("zoxide"))
            {
                return;
            }

            // Forked and silent: this happens on every movement between directories and must
            // never be something the user waits for or sees.
            fileManager.Runner.Run(new ProcessRequest(
                $"zoxide add '{path.Replace("'", @"'\''", StringComparison.Ordinal)}'",
                new ProcessFlags("fs")));
        };
    }
}
