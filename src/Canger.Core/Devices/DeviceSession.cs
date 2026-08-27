// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Processes;
using Canger.Core.Tasks;

namespace Canger.Core.Devices;

/// <summary>
/// The removable drives as the device view shows them: the list, the cursor, and the checks that
/// stand between a keystroke and a drive being unplugged mid-write.
/// </summary>
/// <remarks>
/// <para>
/// Kept here rather than in the widget, and for the same reason the task queue is: the widget
/// should draw and nothing else. Everything with a decision in it is testable without a terminal,
/// and — since it takes an <see cref="IProcessRunner"/> — without a drive.
/// </para>
/// <para>
/// The list is re-read rather than watched. <c>lsblk</c> reads sysfs and does not touch the disc,
/// so polling it costs nothing and cannot wake a drive that has spun down; watching would mean
/// either a D-Bus dependency or a udev helper process, for a list that is on screen for seconds
/// at a time.
/// </para>
/// </remarks>
public sealed class DeviceSession
{
    private readonly IProcessRunner _runner;
    private readonly TaskQueue _tasks;
    private int _cursor;
    private long _readAt;

    /// <summary>Creates a session.</summary>
    /// <param name="runner">Runs <c>lsblk</c>.</param>
    /// <param name="tasks">
    /// The work queue, consulted so that a drive Canger is itself writing to is not unmounted.
    /// </param>
    public DeviceSession(IProcessRunner runner, TaskQueue tasks)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
    }

    /// <summary>How old the list may get before the view re-reads it.</summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(2);

    /// <summary>The drives, as they were last read.</summary>
    public IReadOnlyList<BlockDevice> Devices { get; private set; } = [];

    /// <summary>
    /// Why the list is empty, when it is empty for a reason worth saying.
    /// </summary>
    /// <remarks>
    /// "No removable drives attached" and "lsblk is not installed" look identical on screen
    /// otherwise, and the second is the user's to fix.
    /// </remarks>
    public string? Problem { get; private set; }

    /// <summary>Which row the cursor is on.</summary>
    public int CursorIndex => Math.Clamp(_cursor, 0, Math.Max(Devices.Count - 1, 0));

    /// <summary>The drive under the cursor, or <see langword="null"/> when there are none.</summary>
    public BlockDevice? Selected => Devices.Count > 0 ? Devices[CursorIndex] : null;

    /// <summary>Moves the cursor.</summary>
    /// <param name="offset">How far, negative to move up.</param>
    public void MoveCursor(int offset) =>
        _cursor = Math.Clamp(_cursor + offset, 0, Math.Max(Devices.Count - 1, 0));

    /// <summary>Moves the cursor to the top or the bottom.</summary>
    /// <param name="toEnd">Whether to go to the bottom.</param>
    public void MoveCursorToEdge(bool toEnd) =>
        _cursor = toEnd ? Math.Max(Devices.Count - 1, 0) : 0;

    /// <summary>Re-reads the drives if the list has got old.</summary>
    /// <returns><see langword="true"/> when the list was re-read.</returns>
    public bool ReloadIfStale()
    {
        if (Environment.TickCount64 - _readAt < (long)MaximumAge.TotalMilliseconds)
        {
            return false;
        }

        Reload();
        return true;
    }

    /// <summary>
    /// Re-reads the drives now.
    /// </summary>
    /// <remarks>
    /// The cursor is kept on the drive it was pointing at, found again by its device path rather
    /// than by its position. A drive appearing above it would otherwise slide the cursor down onto
    /// its neighbour, and a key pressed at that moment would act on a drive the user was not
    /// looking at — which for <c>eject</c> is the whole problem.
    /// </remarks>
    public void Reload()
    {
        _readAt = Environment.TickCount64;

        string? was = Selected?.Path;

        if (!Executables.Exists("lsblk"))
        {
            Devices = [];
            Problem = "lsblk is not installed, so Canger cannot see the drives attached.";
            return;
        }

        ProcessResult result = _runner.Run(
            new ProcessRequest($"lsblk {DeviceLister.Arguments}", new ProcessFlags("p")));

        if (!result.Succeeded)
        {
            Devices = [];
            Problem = result.Error ?? "lsblk could not be run.";
            return;
        }

        Devices = DeviceLister.Parse(result.Output);
        Problem = null;

        Restore(was);
    }

    /// <summary>Puts the cursor back on the drive it was on, if that drive is still there.</summary>
    private void Restore(string? path)
    {
        if (path is null)
        {
            _cursor = Math.Clamp(_cursor, 0, Math.Max(Devices.Count - 1, 0));
            return;
        }

        for (int i = 0; i < Devices.Count; i++)
        {
            if (string.Equals(Devices[i].Path, path, StringComparison.Ordinal))
            {
                _cursor = i;
                return;
            }
        }

        _cursor = Math.Clamp(_cursor, 0, Math.Max(Devices.Count - 1, 0));
    }

    /// <summary>
    /// Says why an action on the selected drive must not go ahead.
    /// </summary>
    /// <param name="device">The drive the user asked about.</param>
    /// <param name="wholeDisk">Whether the action affects the drive rather than one volume.</param>
    /// <returns>The reason, or <see langword="null"/> when it is safe.</returns>
    /// <remarks>
    /// The list is read again first. What is on screen may be two seconds old, and two seconds is
    /// long enough to unplug something.
    /// </remarks>
    public string? Refuse(BlockDevice device, bool wholeDisk)
    {
        ArgumentNullException.ThrowIfNull(device);

        Reload();

        return DeviceActions.Refuse(device, Devices, BusyPaths(), wholeDisk);
    }

    /// <summary>
    /// Everywhere Canger's own outstanding work is reading from or writing to.
    /// </summary>
    /// <returns>The paths, which may be files or directories.</returns>
    /// <remarks>
    /// Both ends of every transfer, because unmounting the drive a copy is reading from breaks it
    /// exactly as thoroughly as unmounting the one it is writing to.
    /// </remarks>
    public IReadOnlyList<string> BusyPaths()
    {
        List<string> paths = [];

        foreach (QueuedTask task in _tasks.Tasks)
        {
            if (task.IsComplete || task.Work is not CopyJob job)
            {
                continue;
            }

            paths.Add(job.Destination);
            paths.AddRange(job.Sources);
        }

        return paths;
    }
}
