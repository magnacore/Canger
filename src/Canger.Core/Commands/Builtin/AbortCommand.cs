// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Commands.Builtin;

/// <summary>
/// Cancels whatever is running in the background.
/// </summary>
/// <remarks>
/// <para>
/// This is what <c>Ctrl-C</c> is bound to, and what it means in a file manager: stop the copy,
/// do not stop the program. With nothing running it says how to quit instead, because Ctrl-C is
/// what someone reaches for when they want out and it is worth answering that rather than doing
/// nothing.
/// </para>
/// <para>
/// Worth knowing that in ranger this binding never actually fires: Ctrl-C reaches it as a signal
/// and kills ranger first, which is why pressing it there closes the terminal. Canger delivers
/// Ctrl-C as a keystroke, so the binding does what the configuration always said it would.
/// </para>
/// </remarks>
[Command("abort", Summary = "Cancel the running background task.")]
public sealed class AbortCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.Tasks.Current is not { IsComplete: false } running)
        {
            FileManager.Notify("Type Q or :quit to exit canger");
            return;
        }

        FileManager.Tasks.Cancel(running);
        FileManager.Notify($"aborting: {running.Description}");
    }
}
