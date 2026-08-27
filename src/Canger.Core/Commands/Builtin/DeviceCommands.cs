// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Devices;

namespace Canger.Core.Commands.Builtin;

/// <summary>Opens the list of removable drives.</summary>
[Command("devices_open", Summary = "Show the removable drives that can be mounted.")]
public sealed class DevicesOpenCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.OpenDevices();
}

/// <summary>Closes the list of removable drives.</summary>
[Command("devices_close", Summary = "Close the device list.")]
public sealed class DevicesCloseCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.CloseDevices();
}

/// <summary>Re-reads the attached drives.</summary>
[Command("devices_reload", Summary = "Look again at what is plugged in.")]
public sealed class DevicesReloadCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.Devices.Reload();
}

/// <summary>
/// What every command that acts on a drive has in common.
/// </summary>
/// <remarks>
/// The refusals live here rather than in each command, so that a new action cannot be added
/// without them. Every one of them re-reads the drives first: what is on screen may be two
/// seconds old, and two seconds is long enough to unplug something.
/// </remarks>
public abstract class DeviceActionCommand : CangerCommand
{
    /// <summary>Whether this action affects the whole drive rather than one volume.</summary>
    protected virtual bool AffectsWholeDisk => false;

    /// <inheritdoc />
    public override void Execute()
    {
        if (!Processes.Executables.Exists(DeviceActions.Tool))
        {
            FileManager.Notify(
                $"{DeviceActions.Tool} is not installed, so Canger cannot mount anything. "
                + "Install udisks2.", isError: true);
            return;
        }

        if (FileManager.Devices.Selected is not { } device)
        {
            FileManager.Notify("no drive selected", isError: true);
            return;
        }

        if (FileManager.Devices.Refuse(device, AffectsWholeDisk) is { } reason)
        {
            FileManager.Notify($"{device.DisplayName} {reason}", isError: true);
            return;
        }

        // Looked up again after the refusal check, which re-read the list: the row captured above
        // may name a mount point that has since changed.
        BlockDevice current = FileManager.Devices.Devices.FirstOrDefault(
            d => string.Equals(d.Path, device.Path, StringComparison.Ordinal)) ?? device;

        Act(current);
    }

    /// <summary>Does the thing, once it is known to be safe.</summary>
    /// <param name="device">The drive, freshly read.</param>
    protected abstract void Act(BlockDevice device);

    /// <summary>
    /// Runs one of the drive commands, on the queue or in front of the interface.
    /// </summary>
    /// <param name="command">What to run.</param>
    /// <param name="then">
    /// Run once it has ended and the drive list has been re-read, for an action that is one step
    /// of several.
    /// </param>
    /// <remarks>
    /// Anything that might stop and ask gets the terminal. Everything else goes on the task
    /// queue, where it turns the spinner instead of blanking the screen, and the drive list is
    /// re-read when it ends so the row shows what actually happened.
    /// </remarks>
    protected void Run(DeviceCommand command, Action? then = null)
    {
        if (command.NeedsTerminal)
        {
            FileManager.RunProgram(command.Command);
            FileManager.Devices.Reload();
            then?.Invoke();
            return;
        }

        FileManager.RunInBackground(command.Description, command.Command, finished: task =>
        {
            // udisks answers a request it cannot authorise by saying so rather than by prompting,
            // because it was told not to prompt. Asking again with the terminal lets pkttyagent
            // put the question — which is all a desktop file manager's password dialog is. Only
            // on this one cause, only once, and never invisibly: the user sees the prompt.
            if (DeviceActions.NeedsAuthorisation(task.Error))
            {
                FileManager.RunProgram(command.Command.Replace(
                    " --no-user-interaction", string.Empty, StringComparison.Ordinal));
            }

            FileManager.Devices.Reload();
            then?.Invoke();
        });
    }

    /// <summary>Finds a volume again by its device path, after the list has been re-read.</summary>
    /// <param name="path">The device node.</param>
    /// <returns>The volume, or <see langword="null"/> when it is no longer there.</returns>
    protected BlockDevice? Find(string path) =>
        FileManager.Devices.Devices.FirstOrDefault(
            d => string.Equals(d.Path, path, StringComparison.Ordinal));
}

/// <summary>Mounts the selected volume.</summary>
[Command("devices_mount", Summary = "Mount the selected drive.")]
public sealed class DevicesMountCommand : DeviceActionCommand
{
    /// <inheritdoc />
    protected override void Act(BlockDevice device)
    {
        if (device.IsMounted)
        {
            FileManager.Notify($"{device.DisplayName} is already at {device.MountPoint}");
            return;
        }

        // An encrypted drive has to be opened before there is a filesystem to mount, and asking
        // for the passphrase is the same thing the user would do next anyway.
        Run(device.Kind == VolumeKind.Encrypted && !device.IsUnlocked
                ? DeviceActions.Unlock(device)
                : DeviceActions.Mount(device));
    }
}

/// <summary>Unmounts the selected volume.</summary>
[Command("devices_unmount", Summary = "Unmount the selected drive.")]
public sealed class DevicesUnmountCommand : DeviceActionCommand
{
    /// <inheritdoc />
    protected override void Act(BlockDevice device)
    {
        if (!device.IsMounted)
        {
            FileManager.Notify($"{device.DisplayName} is not mounted");
            return;
        }

        Run(DeviceActions.Unmount(device));
    }
}

/// <summary>Opens an encrypted drive.</summary>
[Command("devices_unlock", Summary = "Unlock the selected encrypted drive.")]
public sealed class DevicesUnlockCommand : DeviceActionCommand
{
    /// <inheritdoc />
    protected override void Act(BlockDevice device)
    {
        if (device.Kind != VolumeKind.Encrypted)
        {
            FileManager.Notify($"{device.DisplayName} is not encrypted", isError: true);
            return;
        }

        if (device.IsUnlocked)
        {
            FileManager.Notify($"{device.DisplayName} is already unlocked");
            return;
        }

        Run(DeviceActions.Unlock(device));
    }
}

/// <summary>Closes an encrypted drive.</summary>
[Command("devices_lock", Summary = "Lock the selected encrypted drive.")]
public sealed class DevicesLockCommand : DeviceActionCommand
{
    /// <inheritdoc />
    protected override bool AffectsWholeDisk => true;

    /// <inheritdoc />
    protected override void Act(BlockDevice device)
    {
        if (device.Kind != VolumeKind.Encrypted || !device.IsUnlocked)
        {
            FileManager.Notify($"{device.DisplayName} is not unlocked", isError: true);
            return;
        }

        Run(DeviceActions.Lock(device));
    }
}

/// <summary>Makes the whole drive safe to unplug.</summary>
[Command("devices_eject", Summary = "Unmount, lock and power off the whole drive.")]
public sealed class DevicesEjectCommand : DeviceActionCommand
{
    /// <inheritdoc />
    protected override bool AffectsWholeDisk => true;

    /// <inheritdoc />
    protected override void Act(BlockDevice device)
    {
        Run(DeviceActions.SafelyRemove(device.DiskPath, device.DiskName,
                                       FileManager.Devices.Devices));

        FileManager.Notify($"removing {device.DiskName} — wait for it to finish before unplugging");
    }
}

/// <summary>
/// Mounts the selected drive if it needs it, and goes there.
/// </summary>
/// <remarks>
/// What clicking a drive does in a desktop file manager, and it can take more than one step to
/// get there: an encrypted drive has to be unlocked, and the filesystem that appears inside it
/// then has to be mounted, before there is anywhere to go. Each step finishes asynchronously —
/// a queued command, or a passphrase prompt — so the next one is decided when the last has
/// ended, by looking at what the drive has become rather than by assuming.
/// </remarks>
[Command("devices_enter", Summary = "Mount the selected drive and open it.")]
public sealed class DevicesEnterCommand : DeviceActionCommand
{
    /// <summary>
    /// How many things may be done before giving up.
    /// </summary>
    /// <remarks>
    /// Three is the longest real chain: unlock a container, step into what it holds, mount that.
    /// A budget rather than a flag because without one a mount that keeps failing would be
    /// retried forever — each failure re-entering the same code with the same drive in the same
    /// state.
    /// </remarks>
    private const int Steps = 3;

    /// <inheritdoc />
    protected override void Act(BlockDevice device) => Follow(device.Path, Steps);

    /// <summary>Takes the next step towards having the drive open, and goes there when it is.</summary>
    private void Follow(string path, int steps)
    {
        if (Find(path) is not { } device)
        {
            FileManager.Notify("that drive is no longer attached", isError: true);
            return;
        }

        if (device.MountPoint is { Length: > 0 } mountPoint
            && FileManager.FileSystem.DirectoryExists(mountPoint))
        {
            FileManager.CloseDevices();
            FileManager.CurrentTab.Enter(mountPoint);
            return;
        }

        if (steps <= 0)
        {
            FileManager.Notify($"could not open {device.DisplayName}", isError: true);
            return;
        }

        if (device.Kind == VolumeKind.Encrypted)
        {
            // Already open: what to mount is the filesystem inside, not the container.
            if (device.IsUnlocked && device.ClearTextPath is { Length: > 0 } inside)
            {
                Follow(inside, steps - 1);
                return;
            }

            Run(DeviceActions.Unlock(device), () => Follow(path, steps - 1));
            return;
        }

        Run(DeviceActions.Mount(device), () => Follow(path, steps - 1));
    }
}
