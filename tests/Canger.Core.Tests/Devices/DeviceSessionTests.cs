// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Devices;
using Canger.Core.FileOperations;
using Canger.Core.Processes;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Devices;

/// <summary>
/// The list the device view shows, its cursor, and the checks that stand between a keystroke and
/// a drive being pulled out from under a running copy.
/// </summary>
public class DeviceSessionTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Join(TestPaths.Root, "tests", "Canger.Core.Tests", "Devices",
                                   "Fixtures", name));

    private static (DeviceSession Session, FakeFileManager.RecordingProcessRunner Runner,
                    TaskQueue Tasks) Build(string fixture)
    {
        FakeFileManager.RecordingProcessRunner runner = new()
        {
            Result = new ProcessResult(0, Fixture(fixture)),
        };

        TaskQueue tasks = new();
        return (new DeviceSession(runner, tasks), runner, tasks);
    }

    [Fact]
    public void ReadingTheDrivesAsksLsblkForWhatTheParserNeeds()
    {
        (DeviceSession session, FakeFileManager.RecordingProcessRunner runner, _) =
            Build("plain-usb-stick.json");

        session.Reload();

        ProcessRequest request = Assert.Single(runner.Requests);
        Assert.StartsWith("lsblk ", request.Command, StringComparison.Ordinal);
        Assert.Contains("--json", request.Command, StringComparison.Ordinal);

        // --bytes, so sizes arrive as numbers and Canger formats them the way it formats every
        // other size rather than showing lsblk's spelling beside its own.
        Assert.Contains("--bytes", request.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingTheDrivesDoesNotTakeTheTerminal()
    {
        // It happens every two seconds while the list is on screen. Handing over the terminal for
        // it would make the whole interface flicker twice a second.
        (DeviceSession session, FakeFileManager.RecordingProcessRunner runner, _) =
            Build("plain-usb-stick.json");

        session.Reload();

        Assert.True(Assert.Single(runner.Requests).Flags.Pipe);
    }

    [Fact]
    public void TheCursorStaysOnTheDriveItWasOnWhenAnotherAppears()
    {
        // The list is re-read every couple of seconds. A drive appearing above the cursor would
        // otherwise slide it down onto the neighbour — and a key pressed at that moment would act
        // on a drive the user was not looking at, which for eject is the whole problem.
        FakeFileManager.RecordingProcessRunner runner = new()
        {
            Result = new ProcessResult(0, Fixture("plain-usb-stick.json")),
        };

        DeviceSession session = new(runner, new TaskQueue());
        session.Reload();

        string was = session.Selected!.Path;

        // The same stick, now with an encrypted drive listed ahead of it.
        runner.Result = new ProcessResult(0, TwoDrives);
        session.Reload();

        Assert.Equal(was, session.Selected!.Path);
        Assert.True(session.CursorIndex > 0, "the drive should have moved down the list");
    }

    [Fact]
    public void ADriveUnpluggedFromUnderTheCursorLeavesItSomewhereValid()
    {
        FakeFileManager.RecordingProcessRunner runner = new()
        {
            Result = new ProcessResult(0, Fixture("plain-usb-stick.json")),
        };

        DeviceSession session = new(runner, new TaskQueue());
        session.Reload();

        runner.Result = new ProcessResult(0, Fixture("internal-only.json"));
        session.Reload();

        Assert.Empty(session.Devices);
        Assert.Null(session.Selected);
    }

    [Fact]
    public void LsblkFailingIsSaidOutLoudRatherThanLookingLikeAnEmptyList()
    {
        // "No removable drives attached" and "lsblk could not be run" look identical on screen
        // otherwise, and only one of them is the user's to fix.
        FakeFileManager.RecordingProcessRunner runner = new()
        {
            Result = new ProcessResult(error: "no such file"),
        };

        DeviceSession session = new(runner, new TaskQueue());
        session.Reload();

        Assert.Empty(session.Devices);
        Assert.NotNull(session.Problem);
    }

    [Fact]
    public void ATransferIsCountedAtBothEnds()
    {
        // Unmounting the drive a copy is reading from breaks it exactly as thoroughly as
        // unmounting the one it is writing to.
        (DeviceSession session, _, TaskQueue tasks) = Build("plain-usb-stick.json");

        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/media/manuj/VERBATIM");
        fs.AddFileOfSize("/home/manuj/film.mp4", 10, DateTimeOffset.UnixEpoch);

        tasks.Add(new CopyJob(fs, ["/home/manuj/film.mp4"], "/media/manuj/VERBATIM"));

        IReadOnlyList<string> busy = session.BusyPaths();

        Assert.Contains("/media/manuj/VERBATIM", busy);
        Assert.Contains("/home/manuj/film.mp4", busy);
    }

    [Fact]
    public void ADriveBeingCopiedToIsRefused()
    {
        (DeviceSession session, _, TaskQueue tasks) = Build("plain-usb-stick.json");
        session.Reload();

        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/media/manuj/VERBATIM");
        fs.AddFileOfSize("/home/manuj/film.mp4", 10, DateTimeOffset.UnixEpoch);
        tasks.Add(new CopyJob(fs, ["/home/manuj/film.mp4"], "/media/manuj/VERBATIM"));

        // The fixture's stick is not mounted, so give it a mount point to be busy at.
        BlockDevice mounted = session.Devices[0] with { MountPoint = "/media/manuj/VERBATIM" };

        Assert.Equal("is in use by a transfer that has not finished",
                     DeviceActions.Refuse(mounted, [mounted], session.BusyPaths(),
                                          wholeDisk: false));
    }

    [Fact]
    public void AFinishedTransferNoLongerHoldsADrive()
    {
        (DeviceSession session, _, TaskQueue tasks) = Build("plain-usb-stick.json");

        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/media/manuj/VERBATIM");
        fs.AddFileOfSize("/home/manuj/film.mp4", 10, DateTimeOffset.UnixEpoch);
        QueuedTask task = tasks.Add(new CopyJob(fs, ["/home/manuj/film.mp4"],
                                                "/media/manuj/VERBATIM"));

        for (int i = 0; i < 1000 && tasks.HasWork; i++)
        {
            tasks.Work(TimeSpan.Zero);
        }

        Assert.True(task.IsComplete);
        Assert.Empty(session.BusyPaths());
    }

    /// <summary>Two drives, with the stick from the fixture listed second.</summary>
    private const string TwoDrives = """
        {
           "blockdevices": [
              {
                 "name": "sda", "path": "/dev/sda", "type": "disk", "size": 500000000000,
                 "fstype": null, "label": null, "mountpoint": null, "hotplug": true,
                 "tran": "usb", "vendor": "Seagate", "model": "Expansion",
                 "rm": false, "ro": false,
                 "children": [
                    {
                       "name": "sda1", "path": "/dev/sda1", "type": "part",
                       "size": 500000000000, "fstype": "ext4", "label": "EXPANSION",
                       "mountpoint": null, "hotplug": true, "tran": null,
                       "vendor": null, "model": null, "rm": false, "ro": false
                    }
                 ]
              },
              {
                 "name": "sdb", "path": "/dev/sdb", "type": "disk", "size": 16008609792,
                 "fstype": null, "label": null, "mountpoint": null, "hotplug": true,
                 "tran": "usb", "vendor": "Verbatim", "model": "STORE N GO",
                 "rm": true, "ro": false,
                 "children": [
                    {
                       "name": "sdb1", "path": "/dev/sdb1", "type": "part",
                       "size": 16007561216, "fstype": "vfat", "label": "VERBATIM",
                       "mountpoint": null, "hotplug": true, "tran": null,
                       "vendor": null, "model": null, "rm": true, "ro": false
                    }
                 ]
              }
           ]
        }
        """;
}
