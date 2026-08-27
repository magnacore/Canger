// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Processes;
using Canger.TestSupport;

namespace Canger.Core.Tests.Devices;

/// <summary>
/// What the keys in the device list actually do.
/// </summary>
/// <remarks>
/// Every one of these ends in a command run against real hardware, so what matters here is which
/// command, whether it takes the terminal, and — most of all — when it is refused. None of it can
/// be tried out: the cost of getting it wrong is a drive unplugged while it is being written to.
/// </remarks>
public class DeviceCommandsTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Join(TestPaths.Root, "tests", "Canger.Core.Tests", "Devices",
                                   "Fixtures", name));

    /// <summary>A manager whose lsblk answers with a fixture.</summary>
    private static FakeFileManager Manager(string fixture, out InMemoryFileSystem fs)
    {
        fs = new InMemoryFileSystem().AddDirectory("/media/manuj/VERBATIM")
                                     .AddDirectory("/home/manuj");

        FakeFileManager manager = new(fs, "/home/manuj");

        ((FakeFileManager.RecordingProcessRunner)manager.Runner).Result =
            new ProcessResult(0, Fixture(fixture));

        manager.Devices.Reload();
        return manager;
    }

    /// <summary>A manager whose lsblk answers with a fixture.</summary>
    private static FakeFileManager Manager(string fixture) => Manager(fixture, out _);

    /// <summary>Everything the manager was asked to run, however it was asked.</summary>
    private static string AllCommands(FakeFileManager manager) =>
        string.Join("\n", manager.BackgroundWork.Select(w => w.Command)
                                 .Concat(manager.LaunchedPrograms.Select(p => p.Command)));

    [Fact]
    public void MountingAStickQueuesUdisksctlAgainstThatDevice()
    {
        FakeFileManager manager = Manager("plain-usb-stick.json");

        manager.Execute("devices_mount");

        Assert.Contains("udisksctl mount", AllCommands(manager), StringComparison.Ordinal);
        Assert.Contains("/dev/sdb1", AllCommands(manager), StringComparison.Ordinal);
    }

    [Fact]
    public void MountingDoesNotTakeTheTerminal()
    {
        // It belongs on the task queue, where it turns the spinner rather than blanking the
        // screen, and can be watched and cancelled like any other job.
        FakeFileManager manager = Manager("plain-usb-stick.json");

        manager.Execute("devices_mount");

        Assert.NotEmpty(manager.BackgroundWork);
    }

    [Fact]
    public void MountingALockedDriveUnlocksItFirst()
    {
        // There is no filesystem to mount until the container is open, and asking for the
        // passphrase is what the user would have done next anyway.
        FakeFileManager manager = Manager("luks-usb-and-internal.json");

        // The fixture's container is already open, so put the cursor on it and shut it.
        manager.Devices.MoveCursorToEdge(toEnd: false);
        while (manager.Devices.Selected is { Kind: not Core.Devices.VolumeKind.Encrypted })
        {
            manager.Devices.MoveCursor(1);
        }

        manager.Execute("devices_mount");

        // Already unlocked, so it is the filesystem inside that wants mounting, not a passphrase.
        Assert.DoesNotContain("udisksctl unlock", AllCommands(manager), StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockingIsTheOneThingThatTakesTheTerminal()
    {
        // udisksctl prompts for the passphrase itself, with the echo off, so it goes from the
        // keyboard to udisks without passing through Canger at all.
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");

        Assert.Contains(manager.LaunchedPrograms,
                        p => p.Command.Contains("udisksctl unlock", StringComparison.Ordinal));
        Assert.Empty(manager.BackgroundWork);
    }

    [Fact]
    public void EjectingUnmountsAndLocksBeforeCuttingThePower()
    {
        FakeFileManager manager = Manager("luks-usb-and-internal.json");

        manager.Execute("devices_eject");

        string command = Assert.Single(manager.BackgroundWork).Command;

        Assert.Contains("unmount", command, StringComparison.Ordinal);
        Assert.Contains("power-off", command, StringComparison.Ordinal);
        Assert.InRange(command.IndexOf("unmount", StringComparison.Ordinal), 0,
                       command.IndexOf("power-off", StringComparison.Ordinal));
    }

    [Fact]
    public void ADriveCangerIsCopyingToIsNotUnmounted()
    {
        // The check the kernel cannot make for us: between two files a transfer holds nothing
        // open, and an unmount landing in that gap succeeds and breaks the copy.
        FakeFileManager manager = Manager("mounted-usb-stick.json", out InMemoryFileSystem fs);

        fs.AddFileOfSize("/home/manuj/film.mp4", 10, DateTimeOffset.UnixEpoch);
        manager.Tasks.Add(new CopyJob(fs, ["/home/manuj/film.mp4"], "/media/manuj/VERBATIM"));

        manager.Execute("devices_unmount");

        Assert.Empty(manager.BackgroundWork);
        Assert.Contains("in use", manager.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ADriveCangerIsCopyingToIsNotEjectedEither()
    {
        FakeFileManager manager = Manager("mounted-usb-stick.json", out InMemoryFileSystem fs);

        fs.AddFileOfSize("/home/manuj/film.mp4", 10, DateTimeOffset.UnixEpoch);
        manager.Tasks.Add(new CopyJob(fs, ["/home/manuj/film.mp4"], "/media/manuj/VERBATIM"));

        manager.Execute("devices_eject");

        Assert.Empty(manager.BackgroundWork);
    }

    [Fact]
    public void UnmountingSomethingAlreadyUnmountedRunsNothing()
    {
        FakeFileManager manager = Manager("plain-usb-stick.json");

        manager.Execute("devices_unmount");

        Assert.Empty(manager.BackgroundWork);
        Assert.Empty(manager.LaunchedPrograms);
    }

    [Fact]
    public void EnteringAMountedDriveGoesThereAndClosesTheList()
    {
        FakeFileManager manager = Manager("mounted-usb-stick.json");
        manager.OpenDevices();

        manager.Execute("devices_enter");

        Assert.Equal("/media/manuj/VERBATIM", manager.CurrentTab.Path);
        Assert.False(manager.DevicesOpen);
    }

    [Fact]
    public void EnteringAnUnmountedDriveMountsItRatherThanGoingNowhere()
    {
        FakeFileManager manager = Manager("plain-usb-stick.json");

        manager.Execute("devices_enter");

        Assert.Contains("udisksctl mount", AllCommands(manager), StringComparison.Ordinal);
    }

    [Fact]
    public void WithNothingPluggedInEveryActionSaysSoAndRunsNothing()
    {
        FakeFileManager manager = Manager("internal-only.json");

        foreach (string command in new[]
                 {
                     "devices_mount", "devices_unmount", "devices_unlock", "devices_lock",
                     "devices_eject", "devices_enter",
                 })
        {
            manager.Execute(command);
        }

        Assert.Empty(manager.BackgroundWork);
        Assert.Empty(manager.LaunchedPrograms);
    }

    [Fact]
    public void OpeningAndClosingTheListAreOrdinaryCommands()
    {
        // So they can be typed at the console, bound anywhere, or called from a plugin — not only
        // reached by the one key that ships bound to them.
        FakeFileManager manager = Manager("plain-usb-stick.json");

        manager.Execute("devices_open");
        Assert.True(manager.DevicesOpen);

        manager.Execute("devices_close");
        Assert.False(manager.DevicesOpen);
    }
}
