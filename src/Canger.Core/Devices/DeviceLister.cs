// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace Canger.Core.Devices;

/// <summary>
/// Finds the removable drives attached to the machine, by reading <c>lsblk</c>.
/// </summary>
/// <remarks>
/// <para>
/// Split into a pure parse and a separate call to <c>lsblk</c> so that the part with all the
/// judgement in it — which drives count as removable, which must never be touched — is a function
/// of text and can be tested against captured output from real machines. None of it needs a disk.
/// </para>
/// <para>
/// Two things about identifying a removable drive are not obvious, and both were found by looking
/// at a real one rather than by reasoning:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>RM</c> is not the flag to use. It is the old removable-media bit and means floppies and
///     optical drives; a USB hard disk reports <c>RM=false</c>. The flag that matters is
///     <c>HOTPLUG</c>, backed up by the transport.
///   </description></item>
///   <item><description>
///     Removability cannot be read off the volume. The unlocked mapper inside an encrypted USB
///     disk reports <c>HOTPLUG=false</c> and no transport at all, because it is a device-mapper
///     node and not attached to anything. It has to be inherited from the physical drive at the
///     top of the tree.
///   </description></item>
/// </list>
/// </remarks>
public static class DeviceLister
{
    /// <summary>The columns asked of <c>lsblk</c>.</summary>
    /// <remarks>
    /// <c>--bytes</c> so that sizes arrive as numbers and Canger formats them the way it formats
    /// every other size, rather than showing lsblk's own spelling beside its own.
    /// </remarks>
    public const string Arguments =
        "--json --bytes --output "
        + "NAME,PATH,TYPE,SIZE,FSTYPE,LABEL,PARTLABEL,UUID,MOUNTPOINT,HOTPLUG,TRAN,"
        + "VENDOR,MODEL,RM,RO";

    /// <summary>Transports that only ever carry something the user can unplug.</summary>
    private static readonly HashSet<string> RemovableTransports =
        new(StringComparer.OrdinalIgnoreCase) { "usb", "ieee1394", "mmc", "sdio" };

    /// <summary>
    /// Mount points that must never appear in the list, whatever the drive claims to be.
    /// </summary>
    /// <remarks>
    /// A second line of defence rather than the main one. An internal SATA bay can report
    /// <c>HOTPLUG=true</c> — hot-swap backplanes and eSATA both do — and on such a machine the
    /// system disk would otherwise be offered for ejection. Finding any of these anywhere beneath
    /// a drive disqualifies the whole drive, not just the one volume.
    /// </remarks>
    private static readonly HashSet<string> ProtectedMountPoints =
        new(StringComparer.Ordinal)
        {
            "/", "/boot", "/boot/efi", "/efi", "/home", "/usr", "/var", "/etc", "/nix",
            "[SWAP]",
        };

    /// <summary>Filesystem types that are containers rather than something to mount.</summary>
    private static readonly HashSet<string> ContainerFileSystems =
        new(StringComparer.OrdinalIgnoreCase) { "crypto_LUKS", "BitLocker" };

    /// <summary>Filesystem types there is no point offering to mount.</summary>
    private static readonly HashSet<string> UnmountableFileSystems =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "swap", "linux_raid_member", "LVM2_member", "zfs_member", "isw_raid_member",
            "ddf_raid_member", "drbd", "mpath_member",
        };

    /// <summary>
    /// Reads the removable volumes out of <c>lsblk --json</c> output.
    /// </summary>
    /// <param name="json">What lsblk printed.</param>
    /// <returns>
    /// One entry per volume worth showing, drive by drive. Empty when nothing removable is
    /// attached, and empty rather than throwing when the output cannot be understood: a device
    /// list that fails to appear is a nuisance, and one that brings the file manager down is not.
    /// </returns>
    public static IReadOnlyList<BlockDevice> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }

        if (!root.TryGetProperty("blockdevices", out JsonElement disks)
            || disks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<BlockDevice> volumes = [];

        foreach (JsonElement disk in disks.EnumerateArray())
        {
            if (!IsRemovableDisk(disk) || HoldsSomethingProtected(disk))
            {
                continue;
            }

            Collect(disk, disk, volumes);
        }

        return volumes;
    }

    /// <summary>Whether a top-level entry is a drive the user could unplug.</summary>
    private static bool IsRemovableDisk(JsonElement disk)
    {
        // Only a whole drive. A partition or a mapper at the top level is something else's
        // business, and neither can be powered off.
        if (!string.Equals(Text(disk, "type"), "disk", StringComparison.Ordinal))
        {
            return false;
        }

        string? transport = Text(disk, "tran");

        // Zram, loop devices and the like report neither, and are not drives in any case.
        return (Flag(disk, "hotplug") && transport is not null)
               || (transport is not null && RemovableTransports.Contains(transport));
    }

    /// <summary>Whether anything beneath a drive is part of the running system.</summary>
    private static bool HoldsSomethingProtected(JsonElement node)
    {
        if (Text(node, "mountpoint") is { } mountPoint
            && ProtectedMountPoints.Contains(mountPoint))
        {
            return true;
        }

        foreach (JsonElement child in Children(node))
        {
            if (HoldsSomethingProtected(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Walks a drive, adding every volume worth listing.</summary>
    private static void Collect(JsonElement node, JsonElement disk, List<BlockDevice> volumes)
    {
        foreach (JsonElement child in Children(node))
        {
            string? fileSystem = Text(child, "fstype");

            if (fileSystem is not null && ContainerFileSystems.Contains(fileSystem))
            {
                // The cleartext device, when the container has been opened, is lsblk's child of
                // the container itself.
                JsonElement? opened = Children(child).Count > 0 ? Children(child)[0] : null;

                volumes.Add(Describe(child, disk, VolumeKind.Encrypted,
                                     opened is { } inside ? Text(inside, "path") : null));
            }
            else if (fileSystem is not null && !UnmountableFileSystems.Contains(fileSystem))
            {
                volumes.Add(Describe(child, disk, VolumeKind.Filesystem, null));
            }

            Collect(child, disk, volumes);
        }
    }

    /// <summary>Turns one lsblk node into a row.</summary>
    private static BlockDevice Describe(JsonElement node, JsonElement disk, VolumeKind kind,
                                        string? clearTextPath) =>
        new(Text(node, "path") ?? string.Empty,
            kind,
            Number(node, "size"),
            Text(node, "fstype"),
            Text(node, "label") ?? Text(node, "partlabel"),
            Text(node, "mountpoint"),
            Text(disk, "path") ?? string.Empty,
            DescribeDisk(disk),
            Text(disk, "tran"),
            Flag(node, "ro"),
            clearTextPath,
            Text(node, "uuid"));

    /// <summary>
    /// Names a drive the way it is printed on the case.
    /// </summary>
    /// <remarks>
    /// lsblk pads both fields with spaces to a fixed width — <c>"WD      "</c> — because that is
    /// how the SCSI inquiry response stores them.
    /// </remarks>
    private static string DescribeDisk(JsonElement disk)
    {
        string vendor = Text(disk, "vendor")?.Trim() ?? string.Empty;
        string model = Text(disk, "model")?.Trim() ?? string.Empty;

        // A model that already begins with the vendor — "WD" and "WDC WD40NMZW" — would read as
        // "WD WDC WD40NMZW".
        if (vendor.Length > 0 && model.StartsWith(vendor, StringComparison.OrdinalIgnoreCase))
        {
            vendor = string.Empty;
        }

        string name = string.Join(' ', new[] { vendor, model }.Where(p => p.Length > 0));

        return name.Length > 0 ? name : Text(disk, "path") ?? "disk";
    }

    /// <summary>A node's children, or nothing.</summary>
    private static IReadOnlyList<JsonElement> Children(JsonElement node) =>
        node.TryGetProperty("children", out JsonElement children)
        && children.ValueKind == JsonValueKind.Array
            ? [.. children.EnumerateArray()]
            : [];

    /// <summary>A string property, or <see langword="null"/> when absent, null or empty.</summary>
    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>A boolean property, false when absent.</summary>
    private static bool Flag(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// A numeric property, zero when absent.
    /// </summary>
    /// <remarks>
    /// Accepts a string as well as a number: without <c>--bytes</c> lsblk writes sizes as
    /// <c>"3.6T"</c>, and a caller that forgot the flag should get a list with the sizes missing
    /// rather than no list at all.
    /// </remarks>
    private static long Number(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
            ? number
            : 0;
}
