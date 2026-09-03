// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.Model;
using Canger.Core.Tasks;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// The list of background work, where it can be reordered, paused or cancelled.
/// </summary>
/// <remarks>
/// The status bar shows what is happening; this shows everything queued behind it and lets the
/// user do something about it. That matters once several transfers are outstanding: without it,
/// a large copy queued by accident can only be waited out.
/// </remarks>
public sealed class TaskView(IColorScheme colorScheme, TaskQueue queue) : Widget
{
    private int _cursor;

    /// <summary>Which task the cursor is on.</summary>
    public int CursorIndex => Math.Clamp(_cursor, 0, Math.Max(queue.Tasks.Count - 1, 0));

    /// <summary>The task under the cursor, or <see langword="null"/> when the queue is empty.</summary>
    public QueuedTask? Selected =>
        queue.Tasks.Count > 0 ? queue.Tasks[CursorIndex] : null;

    /// <summary>Moves the cursor.</summary>
    /// <param name="offset">How far, negative to move up.</param>
    public void MoveCursor(int offset) =>
        _cursor = Math.Clamp(_cursor + offset, 0, Math.Max(queue.Tasks.Count - 1, 0));

    /// <summary>Moves the cursor to the top or bottom.</summary>
    /// <param name="toEnd">Whether to go to the bottom.</param>
    public void MoveCursorToEdge(bool toEnd) =>
        _cursor = toEnd ? Math.Max(queue.Tasks.Count - 1, 0) : 0;

    /// <summary>Moves the selected task within the queue.</summary>
    /// <param name="offset">How far, negative to move it earlier.</param>
    public void MoveTask(int offset)
    {
        if (Selected is not { } task)
        {
            return;
        }

        queue.Move(task, offset);
        int moved = 0;
        for (int i = 0; i < queue.Tasks.Count; i++)
        {
            if (ReferenceEquals(queue.Tasks[i], task))
            {
                moved = i;
                break;
            }
        }

        _cursor = Math.Clamp(moved, 0, Math.Max(queue.Tasks.Count - 1, 0));
    }

    /// <summary>Stops the selected task.</summary>
    public void CancelSelected()
    {
        if (Selected is { } task)
        {
            queue.Cancel(task);
            _cursor = Math.Clamp(_cursor, 0, Math.Max(queue.Tasks.Count - 1, 0));
        }
    }

    /// <summary>Holds or releases the selected task.</summary>
    public void TogglePauseSelected()
    {
        if (Selected is not { } task)
        {
            return;
        }

        if (task.State == TaskState.Paused)
        {
            queue.Resume(task);
        }
        else
        {
            queue.Pause(task);
        }
    }

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        CellStyle baseStyle = colorScheme.Resolve(StyleContext.Of(ContextKey.InTaskview));
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, baseStyle);

        CellStyle titleStyle = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InTaskview, ContextKey.Title));

        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, 1, titleStyle);
        screen.Write(Bounds.X, Bounds.Y, "Task view", titleStyle);

        if (queue.Tasks.Count == 0)
        {
            screen.Write(Bounds.X, Bounds.Y + 2, "Nothing running.",
                         colorScheme.Resolve(StyleContext.Of(ContextKey.InTaskview,
                                                             ContextKey.Error)));
            return;
        }

        for (int i = 0; i < queue.Tasks.Count && i + 2 < Bounds.Height; i++)
        {
            DrawTask(screen, queue.Tasks[i], i, Bounds.Y + i + 2);
        }
    }

    /// <summary>Draws one task, with its progress shown as a tint across the row.</summary>
    private void DrawTask(ScreenBuffer screen, QueuedTask task, int index, int row)
    {
        bool isCursor = index == CursorIndex;

        CellStyle style = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InTaskview).With(isCursor, ContextKey.Selected));

        screen.Fill(Bounds.X, row, Bounds.Width, 1, style);

        string state = task.State switch
        {
            TaskState.Paused => "[held] ",
            TaskState.Failed => "[failed] ",
            TaskState.Cancelled => "[stopped] ",
            TaskState.Finished => "[done] ",
            _ => string.Empty,
        };

        // No figures of its own: the task's own line already carries them, laid out in the
        // columns every transfer uses. Drawing a percentage here as well put it on the row twice
        // for a copy, and indented every row by the width of a column that only some rows filled.
        screen.Write(Bounds.X, row,
                     new WideString(state + task.Description).Truncate(Bounds.Width), style);

        // The filled portion is tinted rather than drawn as a bar, so the description stays
        // readable underneath it.
        if (task.Progress is { } progress and > 0 and < 1)
        {
            screen.Recolor(
                Bounds.X, row, (int)(Bounds.Width * progress),
                colorScheme.Resolve(StyleContext.Of(ContextKey.InTaskview, ContextKey.Loaded)
                                                .With(isCursor, ContextKey.Selected)));
        }
    }
}
