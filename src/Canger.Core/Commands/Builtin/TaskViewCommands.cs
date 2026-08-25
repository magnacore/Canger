// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Commands.Builtin;

/// <summary>Opens the list of background work.</summary>
[Command("taskview_open", Summary = "Show the queued background work.")]
public sealed class TaskViewOpenCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.OpenTaskView();
}

/// <summary>Closes the list of background work.</summary>
[Command("taskview_close", Summary = "Close the task view.")]
public sealed class TaskViewCloseCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.CloseTaskView();
}

/// <summary>Holds or releases everything queued.</summary>
[Command("pause_tasks", Summary = "Hold or release all background work.")]
public sealed class PauseTasksCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        bool anyRunning = FileManager.Tasks.Tasks.Any(
            t => !t.IsComplete && t.State != Tasks.TaskState.Paused);

        foreach (Tasks.QueuedTask task in FileManager.Tasks.Tasks)
        {
            if (anyRunning)
            {
                FileManager.Tasks.Pause(task);
            }
            else
            {
                FileManager.Tasks.Resume(task);
            }
        }

        FileManager.Notify(anyRunning ? "work held" : "work resumed");
    }
}
