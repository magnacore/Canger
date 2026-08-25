// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// Opens the prompt showing a command that was run before.
/// </summary>
/// <remarks>
/// This is what <c>&lt;C-p&gt;</c> is for: reaching the last command without opening the prompt
/// and then pressing Up. Ranger writes it as two chained actions, the second an <c>eval</c> that
/// reaches into the console widget; one command that says what it wants is clearer and does not
/// depend on the prompt already being open.
/// </remarks>
[Command("console_history", Summary = "Open the prompt at an earlier command: "
                                    + "console_history [<offset>]")]
public sealed class ConsoleHistoryCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        IReadOnlyList<string> history = FileManager.CommandHistory;

        if (history.Count == 0)
        {
            // Opening an empty prompt is better than doing nothing: the key still means
            // "start typing a command".
            FileManager.OpenConsole();
            return;
        }

        // -1 is the most recent, -2 the one before it. A positive offset is accepted and read
        // the same way, since there is nothing forward of the newest entry to mean.
        int offset = int.TryParse(Argument(1), CultureInfo.InvariantCulture, out int parsed)
            ? Math.Abs(parsed)
            : 1;

        int index = Math.Clamp(history.Count - Math.Max(offset, 1), 0, history.Count - 1);
        string line = history[index];

        FileManager.OpenConsole(line, line.Length);
    }
}
