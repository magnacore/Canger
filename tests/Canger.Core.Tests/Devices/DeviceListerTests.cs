// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Devices;

namespace Canger.Core.Tests.Devices;

/// <summary>
/// Which drives the device list offers, and which it must never offer.
/// </summary>
/// <remarks>
/// Every fixture here is real <c>lsblk</c> output rather than something written to suit the
/// parser — one of them captured from the machine Canger is developed on, serial numbers and
/// UUIDs replaced. That matters because the two things this code has to get right are exactly the
/// two that reasoning about lsblk gets wrong.
/// </remarks>
public class DeviceListerTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Join(TestPaths.Root, "tests", "Canger.Core.Tests", "Devices",
                                   "Fixtures", name));

    private static IReadOnlyList<BlockDevice> Parse(string fixture) =>
        DeviceLister.Parse(Fixture(fixture));

    [Fact]
    public void AUsbHardDiskIsListedEvenThoughItSaysItIsNotRemovable()
    {
        // The first surprise. `RM` is the old removable-media bit — floppies and optical drives —
        // and a USB hard disk reports it false. Filtering on it finds nothing at all, which is
        // exactly what the obvious implementation does.
        IReadOnlyList<BlockDevice> volumes = Parse("luks-usb-and-internal.json");

        Assert.NotEmpty(volumes);
        Assert.All(volumes, v => Assert.Equal("/dev/sda", v.DiskPath));
    }

    [Fact]
    public void TheFilesystemInsideAnEncryptedDriveIsReachable()
    {
        // The second surprise. The unlocked mapper reports HOTPLUG=false and no transport at all,
        // because it is a device-mapper node attached to nothing. Removability has to come from
        // the drive at the top of the tree, not from the volume.
        IReadOnlyList<BlockDevice> volumes = Parse("luks-usb-and-internal.json");

        BlockDevice inside = Assert.Single(volumes, v => v.Kind == VolumeKind.Filesystem);

        Assert.Equal("ext4", inside.FileSystem);
        Assert.Equal("BACKUP_01_A", inside.Label);
        Assert.Equal("/media/manuj/BACKUP_01_A", inside.MountPoint);
        Assert.True(inside.IsMounted);
    }

    [Fact]
    public void TheEncryptedContainerIsListedAndKnowsWhatIsInsideIt()
    {
        BlockDevice container = Assert.Single(Parse("luks-usb-and-internal.json"),
                                              v => v.Kind == VolumeKind.Encrypted);

        Assert.Equal("/dev/sda1", container.Path);
        Assert.Equal("crypto_LUKS", container.FileSystem);
        Assert.True(container.IsUnlocked);
        Assert.StartsWith("/dev/mapper/luks-", container.ClearTextPath!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInternalDiskIsNotListed()
    {
        // The machine this was captured from has whole-disk encryption on an NVMe drive, so its
        // internal volumes look structurally identical to the external one — same crypto_LUKS,
        // same mapper. Only the transport tells them apart.
        Assert.DoesNotContain(Parse("luks-usb-and-internal.json"),
                              v => v.DiskPath.Contains("nvme", StringComparison.Ordinal));
    }

    [Fact]
    public void AMachineWithNothingPluggedInOffersNothing()
    {
        Assert.Empty(Parse("internal-only.json"));
    }

    [Fact]
    public void APlainStickIsListedWithItsLabelAndSize()
    {
        BlockDevice stick = Assert.Single(Parse("plain-usb-stick.json"));

        Assert.Equal("/dev/sdb1", stick.Path);
        Assert.Equal("VERBATIM", stick.Label);
        Assert.Equal("vfat", stick.FileSystem);
        Assert.Equal(16_007_561_216, stick.SizeBytes);
        Assert.False(stick.IsMounted);
        Assert.Equal("usb", stick.Transport);
    }

    [Fact]
    public void TheSystemDiskIsRefusedEvenWhenItCallsItselfHotPluggable()
    {
        // Hot-swap backplanes and eSATA report HOTPLUG=true on internal drives. On such a machine
        // the flag alone would offer the running system for ejection.
        Assert.Empty(Parse("hotplug-internal-bay.json"));
    }

    [Fact]
    public void ADriveIsNamedTheWayItIsPrintedOnTheCase()
    {
        // lsblk pads both fields to a fixed width because that is how the SCSI inquiry response
        // stores them, and the model here already starts with the vendor.
        Assert.Equal("WDC WD40NMZW-59GX6S1", Parse("luks-usb-and-internal.json")[0].DiskName);
    }

    [Fact]
    public void SwapOnARemovableDriveIsNotOfferedForMounting()
    {
        Assert.DoesNotContain(Parse("luks-usb-and-internal.json"), v => v.FileSystem == "swap");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"blockdevices\": null}")]
    [InlineData("{\"blockdevices\": [{\"type\": \"disk\"}]}")]
    public void RubbishInMeansAnEmptyListRatherThanACrash(string json)
    {
        // A device list that fails to appear is a nuisance. One that brings the file manager down
        // while a copy is running is not.
        Assert.Empty(DeviceLister.Parse(json));
    }
}
