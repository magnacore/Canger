// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Devices;

/// <summary>What a listed volume is, which decides what can be done to it.</summary>
public enum VolumeKind
{
    /// <summary>A filesystem, mounted or not.</summary>
    Filesystem,

    /// <summary>An encrypted container, which must be unlocked before it holds a filesystem.</summary>
    Encrypted,
}

/// <summary>
/// A volume on a removable drive, as the device list shows it.
/// </summary>
/// <remarks>
/// <para>
/// One row of the list. A plain USB stick contributes one of these; an encrypted drive contributes
/// the locked container, and — once unlocked — the filesystem inside it as a second.
/// </para>
/// <para>
/// <see cref="DiskPath"/> is the whole physical drive rather than this volume, because that is what
/// gets powered off when the user asks to remove it safely. A partition cannot be ejected on its
/// own.
/// </para>
/// </remarks>
/// <param name="Path">The device node, such as <c>/dev/sdb1</c>.</param>
/// <param name="Kind">Whether this is a filesystem or a locked container.</param>
/// <param name="SizeBytes">How big it is.</param>
/// <param name="FileSystem">The filesystem type, or <c>crypto_LUKS</c> for a container.</param>
/// <param name="Label">Its label, if it has one.</param>
/// <param name="MountPoint">Where it is mounted, or <see langword="null"/> when it is not.</param>
/// <param name="DiskPath">The whole drive this sits on, which is what can be powered off.</param>
/// <param name="DiskName">The drive's vendor and model, for the user to recognise it by.</param>
/// <param name="Transport">How the drive is attached — <c>usb</c>, <c>mmc</c> and so on.</param>
/// <param name="ReadOnly">Whether the kernel considers it read-only.</param>
/// <param name="ClearTextPath">
/// For a container that has been unlocked, the device node of what is inside it. This is what has
/// to be unmounted before the container can be locked again.
/// </param>
public sealed record BlockDevice(
    string Path,
    VolumeKind Kind,
    long SizeBytes,
    string? FileSystem,
    string? Label,
    string? MountPoint,
    string DiskPath,
    string DiskName,
    string? Transport,
    bool ReadOnly,
    string? ClearTextPath = null)
{
    /// <summary>Whether it is mounted somewhere.</summary>
    public bool IsMounted => MountPoint is { Length: > 0 };

    /// <summary>Whether it is a container that has been opened.</summary>
    public bool IsUnlocked => Kind == VolumeKind.Encrypted && ClearTextPath is { Length: > 0 };

    /// <summary>
    /// What to call it in the list.
    /// </summary>
    /// <remarks>
    /// The label if it has one, since that is what is written on the drive and what the mount
    /// point is named after. Failing that the device node, which is at least unambiguous.
    /// </remarks>
    public string DisplayName => Label is { Length: > 0 } label ? label : Path;
}
