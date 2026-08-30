// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Commands;
using Canger.Core.FileOperations;

namespace Canger.Core.Devices;

/// <summary>A command to run against a drive, and how it needs to be run.</summary>
/// <param name="Command">The command line, for a shell.</param>
/// <param name="Description">What the task view calls it while it runs.</param>
/// <param name="NeedsTerminal">
/// Whether the program must be given the terminal because it will ask the user something.
/// </param>
public readonly record struct DeviceCommand(string Command, string Description,
                                            bool NeedsTerminal);

/// <summary>
/// Builds the commands that mount, unmount, unlock and remove a drive, and refuses the ones that
/// would lose data.
/// </summary>
/// <remarks>
/// <para>
/// Everything goes through <c>udisksctl</c> and nothing else: no <c>mount</c>, no <c>umount</c>,
/// no <c>eject</c>, and nothing as root. udisks knows about polkit, mounts under
/// <c>/media/$USER</c> the way the desktop does, and is the same machinery Thunar drives — so a
/// drive mounted here and a drive mounted there behave identically.
/// </para>
/// <para>
/// Nothing here is ever forced. There is no <c>-f</c> and no lazy unmount anywhere: a filesystem
/// that is busy must fail and say so, because the alternative is pulling the floor out from under
/// whatever is writing to it.
/// </para>
/// <para>
/// Building the command is separated from running it so that every one of these decisions is a
/// function of its arguments, and can be tested without a drive attached — which is the only way
/// this could be tested at all.
/// </para>
/// </remarks>
public static class DeviceActions
{
    /// <summary>The program everything runs through.</summary>
    public const string Tool = "udisksctl";

    /// <summary>
    /// Passed to everything that must not stop and ask.
    /// </summary>
    /// <remarks>
    /// A backgrounded udisksctl that decided to raise a polkit prompt would wait forever, with
    /// nothing on screen to type at and a task in the queue that never ends. With this it fails
    /// instead, and the caller can re-run it with the terminal — see
    /// <see cref="NeedsAuthorisation"/>.
    /// </remarks>
    private const string Quiet = "--no-user-interaction";

    /// <summary>Mounts a filesystem.</summary>
    /// <param name="device">The volume to mount.</param>
    /// <returns>The command.</returns>
    public static DeviceCommand Mount(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceCommand($"{Tool} mount {Quiet} -b {Quote(device.Path)}",
                                 $"mounting {device.DisplayName}", NeedsTerminal: false);
    }

    /// <summary>Unmounts a filesystem.</summary>
    /// <param name="device">The volume to unmount.</param>
    /// <returns>The command.</returns>
    public static DeviceCommand Unmount(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceCommand($"{Tool} unmount {Quiet} -b {Quote(device.Path)}",
                                 $"unmounting {device.DisplayName}", NeedsTerminal: false);
    }

    /// <summary>
    /// Opens an encrypted container.
    /// </summary>
    /// <param name="device">The container to unlock.</param>
    /// <returns>The command, which needs the terminal.</returns>
    /// <remarks>
    /// The one thing here that must have the terminal, because udisksctl prompts for the
    /// passphrase itself with the echo turned off. That is also why it is left to do so: the
    /// passphrase goes from the keyboard to udisks and never passes through Canger — not through
    /// a pipe, not through a buffer, not through anything of ours that could be swapped to disc.
    /// </remarks>
    public static DeviceCommand Unlock(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceCommand($"{Tool} unlock -b {Quote(device.Path)}",
                                 $"unlocking {device.DisplayName}", NeedsTerminal: true);
    }

    /// <summary>
    /// Opens an encrypted container with a passphrase Canger already has.
    /// </summary>
    /// <param name="device">The container to unlock.</param>
    /// <returns>The command, which expects the passphrase on its standard input.</returns>
    /// <remarks>
    /// <c>--key-file /dev/stdin</c>, so the passphrase arrives down a pipe and is never written
    /// to a file nor placed in a command line where <c>ps</c> would show it. Verified against a
    /// real LUKS volume; the trap is that the bytes must be exact, since a trailing newline is
    /// part of the passphrase as far as cryptsetup is concerned and makes it the wrong key.
    /// </remarks>
    public static DeviceCommand UnlockWithKey(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceCommand(
            $"{Tool} unlock {Quiet} -b {Quote(device.Path)} --key-file /dev/stdin",
            $"unlocking {device.DisplayName}", NeedsTerminal: false);
    }

    /// <summary>Closes an encrypted container.</summary>
    /// <param name="device">The container to lock.</param>
    /// <returns>The command.</returns>
    public static DeviceCommand Lock(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceCommand($"{Tool} lock {Quiet} -b {Quote(device.Path)}",
                                 $"locking {device.DisplayName}", NeedsTerminal: false);
    }

    /// <summary>
    /// Makes a whole drive safe to unplug: everything unmounted, everything locked, power cut.
    /// </summary>
    /// <param name="diskPath">The drive.</param>
    /// <param name="diskName">What to call it.</param>
    /// <param name="volumes">Every volume currently known, of which those on this drive are used.</param>
    /// <returns>The command.</returns>
    /// <remarks>
    /// One shell command with the steps joined by <c>&amp;&amp;</c>, which buys stop-on-failure
    /// for nothing: a filesystem that will not unmount fails the whole line, and the power-off
    /// never happens. Written as separate calls it would have needed that logic — and the tests
    /// for it — written out by hand.
    ///
    /// The order is the reverse of the order things were opened in. Filesystems first, then the
    /// containers holding them, then the drive: locking a container whose filesystem is still
    /// mounted fails, and so does powering off a drive with a container still open.
    /// </remarks>
    public static DeviceCommand SafelyRemove(string diskPath, string diskName,
                                             IReadOnlyList<BlockDevice> volumes)
    {
        ArgumentException.ThrowIfNullOrEmpty(diskPath);
        ArgumentNullException.ThrowIfNull(volumes);

        IReadOnlyList<BlockDevice> onThisDisk =
            [.. volumes.Where(v => string.Equals(v.DiskPath, diskPath, StringComparison.Ordinal))];

        List<string> steps =
        [
            .. onThisDisk.Where(v => v.IsMounted).Select(v => Unmount(v).Command),
            .. onThisDisk.Where(v => v.IsUnlocked).Select(v => Lock(v).Command),
            $"{Tool} power-off {Quiet} -b {Quote(diskPath)}",
        ];

        return new DeviceCommand(string.Join(" && ", steps),
                                 $"removing {diskName}", NeedsTerminal: false);
    }

    /// <summary>
    /// Whether a failure was udisks refusing on authorisation grounds.
    /// </summary>
    /// <param name="output">What the program printed.</param>
    /// <returns><see langword="true"/> when it should be retried with the terminal.</returns>
    /// <remarks>
    /// With <see cref="Quiet"/> set, udisks answers a request it cannot authorise by saying so
    /// rather than by prompting. Running the same command again with the terminal lets
    /// <c>pkttyagent</c> ask — which is what a desktop file manager's password dialog amounts to.
    /// One escalation, only on this one cause, and never silently: the user sees the prompt.
    /// </remarks>
    public static bool NeedsAuthorisation(string? output) =>
        output is not null
        && (output.Contains("Not authorized", StringComparison.OrdinalIgnoreCase)
            || output.Contains("NotAuthorized", StringComparison.Ordinal));

    /// <summary>
    /// Turns a udisks failure into something a person can act on.
    /// </summary>
    /// <param name="error">What the program printed.</param>
    /// <returns>The plain version, or <see langword="null"/> when there is nothing to translate.</returns>
    /// <remarks>
    /// <c>Error unmounting /dev/dm-2: GDBus.Error:org.freedesktop.UDisks2.Error.DeviceBusy:
    /// Error unmounting /dev/dm-2: target is busy</c> says one thing three times, none of them in
    /// English, and none of them what to do about it. Only the failures a person can actually do
    /// something about are translated; anything else is passed through untouched rather than
    /// paraphrased into vagueness.
    /// </remarks>
    public static string? Explain(string? error)
    {
        if (error is not { Length: > 0 })
        {
            return null;
        }

        if (error.Contains("DeviceBusy", StringComparison.Ordinal)
            || error.Contains("target is busy", StringComparison.OrdinalIgnoreCase))
        {
            return "still in use — something outside Canger has a file open on it. "
                   + "Close it and try again.";
        }

        // Measured against a real LUKS volume: udisks says this for a passphrase that is simply
        // wrong, and something different for one that is empty. They were treated as the same
        // thing, which would have told a user who typed nothing that what they typed was wrong.
        if (error.Contains("Incorrect passphrase", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Failed to activate", StringComparison.OrdinalIgnoreCase)
            || error.Contains("wrong passphrase", StringComparison.OrdinalIgnoreCase))
        {
            return "wrong passphrase";
        }

        if (error.Contains("No key available", StringComparison.OrdinalIgnoreCase))
        {
            return "no passphrase was given";
        }

        if (error.Contains("AlreadyMounted", StringComparison.Ordinal))
        {
            return "already mounted";
        }

        if (error.Contains("NotMounted", StringComparison.Ordinal))
        {
            return "not mounted";
        }

        return null;
    }

    /// <summary>
    /// Says why an action must not go ahead.
    /// </summary>
    /// <param name="device">The volume the user asked about.</param>
    /// <param name="current">The drives as they are now, freshly read.</param>
    /// <param name="busyPaths">
    /// Paths Canger's own outstanding work is reading from or writing to.
    /// </param>
    /// <param name="wholeDisk">Whether the action affects the drive rather than this volume.</param>
    /// <returns>The reason, or <see langword="null"/> when it is safe to proceed.</returns>
    /// <remarks>
    /// <para>
    /// The list on screen can be two seconds old, so the device is looked up again in
    /// <paramref name="current"/> rather than trusted. A drive unplugged in the meantime, or one
    /// that has stopped qualifying as removable, is refused.
    /// </para>
    /// <para>
    /// The check against Canger's own work is not redundant with the kernel's. A transfer holds
    /// the file it is copying open, so the kernel refuses to unmount underneath it — but between
    /// two files it holds nothing at all, and an unmount landing in that gap succeeds. The next
    /// file then fails to be created. Nothing already written is lost, but the transfer breaks
    /// for a reason the user did not intend and cannot see, which is close enough to data loss to
    /// be worth one comparison.
    /// </para>
    /// </remarks>
    public static string? Refuse(BlockDevice device, IReadOnlyList<BlockDevice> current,
                                 IReadOnlyList<string> busyPaths, bool wholeDisk)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(busyPaths);

        bool stillThere = current.Any(
            v => string.Equals(v.Path, device.Path, StringComparison.Ordinal));

        if (!stillThere)
        {
            return "is no longer attached";
        }

        IEnumerable<BlockDevice> affected = wholeDisk
            ? current.Where(v => string.Equals(v.DiskPath, device.DiskPath, StringComparison.Ordinal))
            : current.Where(v => string.Equals(v.Path, device.Path, StringComparison.Ordinal));

        foreach (BlockDevice volume in affected)
        {
            if (volume.MountPoint is not { Length: > 0 } mountPoint)
            {
                continue;
            }

            foreach (string busy in busyPaths)
            {
                if (string.Equals(busy, mountPoint, StringComparison.Ordinal)
                    || PathRelation.IsInside(busy, mountPoint))
                {
                    return "is in use by a transfer that has not finished";
                }
            }
        }

        return null;
    }

    /// <summary>Quotes a device path for the shell.</summary>
    /// <remarks>
    /// Device nodes have no characters that need it, which is exactly why it is done: the day one
    /// of these paths comes from somewhere less predictable than <c>lsblk</c>, the quoting is
    /// already there.
    /// </remarks>
    private static string Quote(string value) => MacroExpander.ShellQuote(value);
}
