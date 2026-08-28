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

/// <summary>Forgets every passphrase held in memory for this sitting.</summary>
[Command("forget_passphrases",
         Summary = "Forget passphrases kept in memory; the keyring is untouched.")]
public sealed class ForgetPassphrasesCommand : CangerCommand
{
    /// <inheritdoc />
    /// <remarks>
    /// So that keeping a passphrase for a sitting is a decision that can be undone without
    /// quitting — leaving a terminal unattended being the reason anyone would want to. It does
    /// not touch the keyring: what was deliberately saved is removed in Seahorse, deliberately.
    /// </remarks>
    public override void Execute()
    {
        int held = FileManager.Devices.Forget();

        FileManager.Notify(held == 1
            ? "forgot 1 passphrase"
            : $"forgot {held} passphrases");
    }
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
            else if (DeviceActions.Explain(task.Error) is { } plain)
            {
                // After the task's own report rather than instead of it: this replaces the raw
                // GDBus line, which says one thing three times and none of them in English.
                FileManager.Notify($"{command.Description}: {plain}", isError: true);
            }

            FileManager.Devices.Reload();
            then?.Invoke();
        });
    }

    /// <summary>
    /// Moves out of the way of a drive that is about to be unmounted.
    /// </summary>
    /// <param name="mountPoints">Where the affected volumes are mounted.</param>
    /// <returns>Whether anything had to move.</returns>
    /// <remarks>
    /// <para>
    /// A file manager showing a directory is a reason that directory cannot be unmounted, and
    /// Canger being the one thing standing in the way of its own eject is no use to anybody. So
    /// every tab looking at the drive is sent home first, its cached listings are dropped, and
    /// any preview taken from it is thrown away — the last because a preview is a file that was
    /// read, and on some paths one that is still being held.
    /// </para>
    /// <para>
    /// This is what a desktop file manager does, and why ejecting from one leaves you in your
    /// home directory rather than refusing. Anything <em>else</em> still holding the drive — a
    /// video playing, an editor with a file open — is beyond Canger's reach, and the unmount will
    /// fail and say so, which is the right answer.
    /// </para>
    /// </remarks>
    protected bool LeaveDrive(IReadOnlyList<string> mountPoints)
    {
        ArgumentNullException.ThrowIfNull(mountPoints);

        if (mountPoints.Count == 0)
        {
            return false;
        }

        bool moved = false;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (Model.Tab tab in FileManager.Tabs.Values)
        {
            if (IsOn(tab.Path, mountPoints))
            {
                tab.Enter(home);
                moved = true;
            }
        }

        foreach (string cached in FileManager.Directories.Paths.ToList())
        {
            if (IsOn(cached, mountPoints))
            {
                FileManager.Directories.Evict(cached);
                moved = true;
            }
        }

        if (moved)
        {
            FileManager.InvalidatePreviews();
        }

        return moved;
    }

    /// <summary>Whether a path is on one of the given mount points.</summary>
    private static bool IsOn(string path, IReadOnlyList<string> mountPoints) =>
        mountPoints.Any(m => string.Equals(path, m, StringComparison.Ordinal)
                             || FileOperations.PathRelation.IsInside(path, m));

    /// <summary>Where the volumes about to be unmounted are mounted.</summary>
    /// <param name="device">The drive the user asked about.</param>
    /// <param name="wholeDisk">Whether the action affects the drive rather than one volume.</param>
    /// <returns>The mount points, which may be none.</returns>
    protected IReadOnlyList<string> MountPointsOf(BlockDevice device, bool wholeDisk)
    {
        ArgumentNullException.ThrowIfNull(device);

        IEnumerable<BlockDevice> affected = wholeDisk
            ? FileManager.Devices.Devices.Where(
                  d => string.Equals(d.DiskPath, device.DiskPath, StringComparison.Ordinal))
            : [device];

        return [.. affected.Select(d => d.MountPoint).OfType<string>()];
    }

    /// <summary>
    /// Opens an encrypted container, however the user has asked to be asked.
    /// </summary>
    /// <param name="device">The container.</param>
    /// <param name="then">What to do once it is open.</param>
    /// <remarks>
    /// <para>
    /// With <c>unlock_prompt</c> at its default, this hands the screen to <c>udisksctl</c> and
    /// lets it ask, as it always has: no passphrase passes through Canger and none is kept.
    /// </para>
    /// <para>
    /// Set to <c>builtin</c>, three places are tried in turn — what was kept for this sitting,
    /// what the desktop's keyring holds, and finally the user. Only the last of those asks, which
    /// is the point: a drive whose passphrase Thunar saved simply opens.
    /// </para>
    /// </remarks>
    protected void Unlock(BlockDevice device, Action? then = null)
    {
        // No UUID means nothing to look a passphrase up by, so there is nothing the builtin
        // prompt can do that udisksctl cannot do better.
        if (FileManager.Settings.UnlockPrompt is not "builtin"
            || device.Uuid is not { Length: > 0 } uuid)
        {
            Run(DeviceActions.Unlock(device), then);
            return;
        }

        if (FileManager.Devices.Remembered(uuid) is { } kept)
        {
            Open(device, uuid, kept, mayOfferToSave: false, then);
            return;
        }

        if (FileManager.Devices.Passphrases.Lookup(uuid) is { } saved)
        {
            Open(device, uuid, saved, mayOfferToSave: false, then);
            return;
        }

        FileManager.Prompt($"Passphrase for {device.DisplayName}:", typed =>
        {
            if (typed is null)
            {
                FileManager.Notify($"{device.DisplayName} was left locked");
                return;
            }

            Open(device, uuid, typed, mayOfferToSave: true, then);
        }, hidden: true);
    }

    /// <summary>Tries a passphrase, and offers to keep one that worked.</summary>
    private void Open(BlockDevice device, string uuid, string passphrase,
                      bool mayOfferToSave, Action? then)
    {
        Processes.ProcessResult result = FileManager.Devices.UnlockWith(device, passphrase);
        string trouble = result.Error ?? result.Output;

        if (!result.Succeeded || DeviceActions.Explain(trouble) is not null)
        {
            FileManager.Notify(
                $"{device.DisplayName}: {DeviceActions.Explain(trouble) ?? trouble.Trim()}",
                isError: true);

            // A saved passphrase that no longer works is worse than none: it fails silently on
            // every attempt and the user cannot see why. Said plainly so it can be fixed.
            if (!mayOfferToSave)
            {
                FileManager.Notify(
                    $"{device.DisplayName}: the remembered passphrase did not work. "
                    + "Remove it in Seahorse, or run :forget_passphrases.", isError: true);
            }

            return;
        }

        FileManager.Devices.Reload();

        if (mayOfferToSave)
        {
            OfferToSave(device, uuid, passphrase, then);
            return;
        }

        then?.Invoke();
    }

    /// <summary>Asks whether to keep a passphrase that worked, and where.</summary>
    private void OfferToSave(BlockDevice device, string uuid, string passphrase, Action? then)
    {
        // Only when there is somewhere to put it. Offering the keyring on a machine without
        // libsecret would be offering something that cannot happen.
        string question = PassphraseStore.IsAvailable
            ? "Remember this passphrase? (n)ever, this (s)ession, in the (k)eyring"
            : "Remember this passphrase for this session?";

        IReadOnlyList<char> choices = PassphraseStore.IsAvailable
            ? ['n', 's', 'k']
            : ['n', 's'];

        FileManager.Ask(question, answer =>
        {
            switch (answer)
            {
                case 's':
                    FileManager.Devices.Remember(uuid, passphrase);
                    FileManager.Notify("kept until Canger closes");
                    break;

                case 'k':
                    FileManager.Devices.Remember(uuid, passphrase);

                    if (FileManager.Devices.Passphrases.Save(
                            uuid, PassphraseStore.LabelFor(device), passphrase) is { } why)
                    {
                        FileManager.Notify($"could not save it: {why}", isError: true);
                    }
                    else
                    {
                        FileManager.Notify("saved in the keyring");
                    }

                    break;

                default:
                    break;
            }

            then?.Invoke();
        }, choices);
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
        if (device.Kind == VolumeKind.Encrypted && !device.IsUnlocked)
        {
            Unlock(device);
            return;
        }

        Run(DeviceActions.Mount(device));
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

        LeaveDrive(MountPointsOf(device, wholeDisk: false));
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

        Unlock(device);
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
        bool moved = LeaveDrive(MountPointsOf(device, wholeDisk: true));

        Run(DeviceActions.SafelyRemove(device.DiskPath, device.DiskName,
                                       FileManager.Devices.Devices));

        FileManager.Notify(moved
            ? $"removing {device.DiskName} — left the drive first; "
              + "wait for it to finish before unplugging"
            : $"removing {device.DiskName} — wait for it to finish before unplugging");
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

            Unlock(device, () => Follow(path, steps - 1));
            return;
        }

        Run(DeviceActions.Mount(device), () => Follow(path, steps - 1));
    }
}
