// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Devices;

namespace Canger.Core.Tests.Devices;

/// <summary>
/// What Canger actually runs against a drive, and what it refuses to run.
/// </summary>
/// <remarks>
/// These commands cannot be tried out: the cost of getting one wrong is a drive unplugged while
/// it is being written to. Building the command line is therefore separated from running it, so
/// that every decision in it is a function of its arguments and can be checked here, with nothing
/// attached.
/// </remarks>
public class DeviceActionsTests
{
    private static BlockDevice Volume(string path = "/dev/sdb1", string? mountPoint = null,
                                      VolumeKind kind = VolumeKind.Filesystem,
                                      string? clearText = null, string disk = "/dev/sdb") =>
        new(path, kind, 16_000_000_000, kind == VolumeKind.Encrypted ? "crypto_LUKS" : "vfat",
            "VERBATIM", mountPoint, disk, "Verbatim STORE N GO", "usb", ReadOnly: false,
            clearText);

    [Fact]
    public void EverythingGoesThroughUdisksctl()
    {
        // Never mount(8), never umount, never eject, never root. udisks mounts under
        // /media/$USER the way the desktop does, so a drive mounted here behaves exactly as one
        // mounted from Thunar.
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        foreach (string command in new[]
                 {
                     DeviceActions.Mount(device).Command,
                     DeviceActions.Unmount(device).Command,
                     DeviceActions.Lock(device).Command,
                     DeviceActions.Unlock(device).Command,
                 })
        {
            Assert.StartsWith("udisksctl ", command, StringComparison.Ordinal);
            Assert.DoesNotContain("sudo", command, StringComparison.Ordinal);
            Assert.DoesNotContain("pkexec", command, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NothingIsEverForced()
    {
        // A busy filesystem must fail. Forcing it, or unmounting lazily, pulls the floor out from
        // under whatever is writing.
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        string everything = string.Join(
            " ",
            DeviceActions.Unmount(device).Command,
            DeviceActions.SafelyRemove("/dev/sdb", "stick", [device]).Command);

        Assert.DoesNotContain(" -f", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("--lazy", everything, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyUnlockingAsksForTheTerminal()
    {
        // udisksctl prompts for the passphrase itself, with the echo off, so the passphrase goes
        // from the keyboard to udisks without passing through Canger at all. Everything else runs
        // on the task queue where it cannot block the interface.
        BlockDevice container = Volume(kind: VolumeKind.Encrypted);

        Assert.True(DeviceActions.Unlock(container).NeedsTerminal);
        Assert.False(DeviceActions.Mount(container).NeedsTerminal);
        Assert.False(DeviceActions.Unmount(container).NeedsTerminal);
        Assert.False(DeviceActions.Lock(container).NeedsTerminal);
    }

    [Fact]
    public void EverythingThatCouldStopAndAskIsToldNotTo()
    {
        // A backgrounded udisksctl that raised a polkit prompt would wait forever, with nothing on
        // screen to type at and a task in the queue that never ends.
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        Assert.Contains("--no-user-interaction", DeviceActions.Mount(device).Command,
                        StringComparison.Ordinal);
        Assert.Contains("--no-user-interaction", DeviceActions.Unmount(device).Command,
                        StringComparison.Ordinal);
        Assert.Contains("--no-user-interaction",
                        DeviceActions.SafelyRemove("/dev/sdb", "stick", [device]).Command,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingADriveUnmountsAndLocksBeforeCuttingThePower()
    {
        // The reverse of the order things were opened in. Locking a container whose filesystem is
        // still mounted fails, and so does powering off a drive with a container still open.
        BlockDevice inside = Volume("/dev/mapper/luks-1", mountPoint: "/media/manuj/BACKUP",
                                    disk: "/dev/sda");
        BlockDevice container = Volume("/dev/sda1", kind: VolumeKind.Encrypted,
                                       clearText: "/dev/mapper/luks-1", disk: "/dev/sda");

        string command = DeviceActions
            .SafelyRemove("/dev/sda", "My Passport", [container, inside]).Command;

        int unmount = command.IndexOf("unmount", StringComparison.Ordinal);
        int locked = command.IndexOf(" lock ", StringComparison.Ordinal);
        int power = command.IndexOf("power-off", StringComparison.Ordinal);

        Assert.InRange(unmount, 0, locked);
        Assert.InRange(locked, 0, power);
    }

    [Fact]
    public void ARemovalStopsAtTheFirstStepThatFails()
    {
        // The steps are joined with && precisely so that a filesystem which will not unmount
        // fails the whole line and the power never gets cut.
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        Assert.Contains(" && ", DeviceActions.SafelyRemove("/dev/sdb", "stick", [device]).Command,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingADriveIgnoresVolumesOnOtherDrives()
    {
        BlockDevice mine = Volume("/dev/sdb1", mountPoint: "/media/manuj/MINE", disk: "/dev/sdb");
        BlockDevice theirs = Volume("/dev/sdc1", mountPoint: "/media/manuj/THEIRS",
                                    disk: "/dev/sdc");

        string command = DeviceActions.SafelyRemove("/dev/sdb", "stick", [mine, theirs]).Command;

        Assert.DoesNotContain("/dev/sdc", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ADriveThatHasBeenUnpluggedIsRefused()
    {
        // The list on screen can be two seconds old.
        BlockDevice stale = Volume();

        Assert.Equal("is no longer attached",
                     DeviceActions.Refuse(stale, [], [], wholeDisk: false));
    }

    [Fact]
    public void ADriveCangerIsStillCopyingToIsRefused()
    {
        // Not redundant with the kernel. A transfer holds the file it is copying open, so the
        // kernel refuses to unmount underneath it — but between two files it holds nothing, and an
        // unmount landing in that gap succeeds and breaks the transfer.
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        Assert.Equal("is in use by a transfer that has not finished",
                     DeviceActions.Refuse(device, [device],
                                          ["/media/manuj/VERBATIM/films"], wholeDisk: false));
    }

    [Fact]
    public void TheDestinationItselfCountsAsInUse()
    {
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        Assert.NotNull(DeviceActions.Refuse(device, [device], ["/media/manuj/VERBATIM"],
                                            wholeDisk: false));
    }

    [Fact]
    public void RemovingADriveChecksEveryVolumeOnIt()
    {
        // Ejecting the drive stops the transfer on its other partition just as surely.
        BlockDevice asked = Volume("/dev/sdb1", mountPoint: "/media/manuj/ONE");
        BlockDevice busy = Volume("/dev/sdb2", mountPoint: "/media/manuj/TWO");

        Assert.NotNull(DeviceActions.Refuse(asked, [asked, busy], ["/media/manuj/TWO/x"],
                                            wholeDisk: true));
        Assert.Null(DeviceActions.Refuse(asked, [asked, busy], ["/media/manuj/TWO/x"],
                                         wholeDisk: false));
    }

    [Fact]
    public void AnIdleDriveIsAllowed()
    {
        BlockDevice device = Volume(mountPoint: "/media/manuj/VERBATIM");

        Assert.Null(DeviceActions.Refuse(device, [device], ["/home/manuj/films"],
                                         wholeDisk: true));
    }

    [Theory]
    [InlineData("Error mounting /dev/sdb1: GDBus.Error:org.freedesktop.UDisks2.Error.NotAuthorized")]
    [InlineData("Not authorized to perform operation")]
    public void AnAuthorisationFailureIsRecognisedSoItCanBeAskedAgainWithATerminal(string output)
    {
        Assert.True(DeviceActions.NeedsAuthorisation(output));
    }

    [Theory]
    [InlineData("Error unmounting: target is busy")]
    [InlineData(null)]
    [InlineData("")]
    public void OtherFailuresAreNotRetried(string? output)
    {
        // Retrying a busy unmount with a terminal would only fail again, more noisily.
        Assert.False(DeviceActions.NeedsAuthorisation(output));
    }


    [Fact]
    public void ABusyDriveIsExplainedInEnglish()
    {
        // What udisks actually says: one thing three times, none of them in English, and none of
        // them what to do about it.
        const string Raw =
            "Error unmounting /dev/dm-2: GDBus.Error:org.freedesktop.UDisks2.Error.DeviceBusy: "
            + "Error unmounting /dev/dm-2: target is busy";

        string? plain = DeviceActions.Explain(Raw);

        Assert.NotNull(plain);
        Assert.DoesNotContain("GDBus", plain, StringComparison.Ordinal);
        Assert.Contains("still in use", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongPassphraseIsExplained()
    {
        Assert.Equal("wrong passphrase",
                     DeviceActions.Explain(
                         "Error unlocking /dev/sda1: Failed to activate device: "
                         + "Operation not permitted"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Error mounting: some cause nobody has seen before")]
    public void AFailureWithNoPlainVersionIsLeftAlone(string? error)
    {
        // Passed through untouched rather than paraphrased into vagueness: the raw text is at
        // least the truth, and is what a search engine will match.
        Assert.Null(DeviceActions.Explain(error));
    }
}
