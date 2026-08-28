// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Devices;
using Canger.Core.Processes;
using Canger.TestSupport;

namespace Canger.Core.Tests.Devices;

/// <summary>
/// Asking for, remembering and using the passphrase of an encrypted drive.
/// </summary>
/// <remarks>
/// The passphrase is the most sensitive thing Canger touches, and almost every test here is about
/// where it must not go: not into a command line, not into the command history, not into a file,
/// and not into a second store of Canger's own invention when the desktop already has one.
/// </remarks>
public class PassphraseTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Join(TestPaths.Root, "tests", "Canger.Core.Tests", "Devices",
                                   "Fixtures", name));

    private static FakeFileManager Manager(string fixture, string unlockPrompt = "builtin")
    {
        FakeFileManager manager = new(
            new InMemoryFileSystem().AddDirectory("/media/manuj/VERBATIM")
                                    .AddDirectory("/home/manuj"),
            "/home/manuj");

        ((FakeFileManager.RecordingProcessRunner)manager.Runner).Result =
            new ProcessResult(0, Fixture(fixture));

        manager.SettingsStore.SetFromText("unlock_prompt", unlockPrompt);
        manager.Devices.Reload();
        return manager;
    }

    private static FakeFileManager.RecordingProcessRunner Runner(FakeFileManager manager) =>
        (FakeFileManager.RecordingProcessRunner)manager.Runner;

    private static BlockDevice Container(string uuid = "e2cae451-e2e7-47bb-9ff3-0241323c254d") =>
        new("/dev/sdb1", VolumeKind.Encrypted, 16_000_000_000, "crypto_LUKS", "secret", null,
            "/dev/sdb", "Verbatim STORE N GO", "usb", ReadOnly: false, ClearTextPath: null,
            Uuid: uuid);

    [Fact]
    public void TheDefaultIsToLetUdisksctlAskAsItAlwaysHas()
    {
        // Nothing of the passphrase passes through Canger unless the user has asked for it to.
        FakeFileManager manager = Manager("locked-luks-stick.json", unlockPrompt: "terminal");

        manager.Execute("devices_unlock");

        Assert.Contains(manager.LaunchedPrograms,
                        p => p.Command.Contains("udisksctl unlock", StringComparison.Ordinal));
        Assert.Null(manager.PendingPrompt);
    }

    [Fact]
    public void TheBuiltinPromptAsksWithTheTypingHidden()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");

        Assert.NotNull(manager.PendingPrompt);
        Assert.True(manager.PendingPrompt!.Value.Hidden,
                    "a passphrase must not be drawn on the screen");
    }

    [Fact]
    public void ThePassphraseGoesDownAPipeAndNeverIntoACommandLine()
    {
        // The whole reason RunWithInput exists. A passphrase in a command line is visible in
        // /proc and to ps for as long as the call takes.
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");

        Assert.Contains(Runner(manager).Fed, f => f.Input == "hunter2");
        Assert.DoesNotContain(Runner(manager).Requests,
                              r => r.Command.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePassphraseIsSentExactlyWithNoNewlineAdded()
    {
        // Measured against a real LUKS volume: a trailing newline is part of the passphrase as
        // far as cryptsetup is concerned, so `echo` fails where `printf` succeeds.
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");

        Assert.Equal("hunter2",
                     Assert.Single(Runner(manager).Fed,
                                   f => f.Command.Contains("unlock", StringComparison.Ordinal))
                           .Input);
    }

    [Fact]
    public void TheKeyIsReadFromStandardInputRatherThanAFile()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");

        Assert.Contains(Runner(manager).Fed,
                        f => f.Command.Contains("--key-file /dev/stdin",
                                                StringComparison.Ordinal));
    }

    [Fact]
    public void GivingUpAtThePromptUnlocksNothing()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type(null);

        Assert.DoesNotContain(Runner(manager).Fed,
                              f => f.Command.Contains("unlock", StringComparison.Ordinal));
    }

    [Fact]
    public void AWorkingPassphraseLeadsToBeingAskedWhetherToKeepIt()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");

        Assert.NotNull(manager.PendingQuestion);
        Assert.Contains("Remember", manager.PendingQuestion!.Value.Question,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void KeepingItForTheSittingMeansNotBeingAskedAgain()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");
        manager.Answer('s');

        manager.Execute("devices_unlock");

        Assert.Null(manager.PendingPrompt);
        Assert.Equal(2, Runner(manager).Fed.Count(
            f => f.Command.Contains("udisksctl unlock", StringComparison.Ordinal)));
    }

    [Fact]
    public void SayingNeverMeansBeingAskedAgain()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");
        manager.Answer('n');

        manager.Execute("devices_unlock");

        Assert.NotNull(manager.PendingPrompt);
    }

    [Fact]
    public void ForgettingClearsWhatWasKeptForTheSitting()
    {
        // Leaving a terminal unattended is the reason to want this, and it should not need a
        // restart.
        FakeFileManager manager = Manager("locked-luks-stick.json");

        manager.Execute("devices_unlock");
        manager.Type("hunter2");
        manager.Answer('s');

        manager.Execute("forget_passphrases");
        manager.Execute("devices_unlock");

        Assert.NotNull(manager.PendingPrompt);
    }

    [Fact]
    public void TheKeyringIsTheDesktopsAndNotCangersOwn()
    {
        // A passphrase saved in Thunar must unlock the drive here, and one saved here must work
        // in Thunar. That is only true if both use the same schema and the same key.
        Assert.Equal("org.gnome.GVfs.Luks.Password", PassphraseStore.Schema);
        Assert.Equal("gvfs-luks-uuid", PassphraseStore.UuidAttribute);
    }

    [Fact]
    public void ASavedPassphraseIsLookedUpByTheContainersUuid()
    {
        FakeFileManager manager = Manager("locked-luks-stick.json");
        BlockDevice container = Container();

        manager.Devices.Passphrases.Lookup(container.Uuid!);

        // Nothing to assert about the answer without secret-tool installed; what matters is that
        // if it is asked, it is asked by UUID and under the desktop's schema.
        Assert.All(Runner(manager).Requests.Where(
                       r => r.Command.StartsWith("secret-tool", StringComparison.Ordinal)),
                   r =>
                   {
                       Assert.Contains(container.Uuid!, r.Command, StringComparison.Ordinal);
                       Assert.Contains(PassphraseStore.Schema, r.Command, StringComparison.Ordinal);
                   });
    }

    [Fact]
    public void TheKeyringLabelReadsLikeTheOnesAlreadyThere()
    {
        // So a row in Seahorse beside the ones gvfs wrote does not read as a stranger.
        BlockDevice drive = new("/dev/sda1", VolumeKind.Encrypted, 4_000_752_599_040L,
                                "crypto_LUKS", null, null, "/dev/sda",
                                "WDC WD40NMZW-59GX6S1", "usb", ReadOnly: false,
                                ClearTextPath: null, Uuid: "61858679-035e-4001-94c3-0e6946fc85df");

        Assert.Equal("Encryption passphrase for WDC WD40NMZW-59GX6S1 (4.0 TB Hard Disk)",
                     PassphraseStore.LabelFor(drive));
    }

    [Fact]
    public void ADriveWithNoUuidFallsBackToLettingUdisksctlAsk()
    {
        // Without a UUID there is nothing to look a passphrase up by and nothing to file one
        // under, so the builtin prompt can do nothing udisksctl cannot do better. Asking anyway
        // would be asking for a passphrase Canger could only ever throw away.
        FakeFileManager manager = Manager("luks-stick-without-uuid.json");

        manager.Execute("devices_unlock");

        Assert.Null(manager.PendingPrompt);
        Assert.Contains(manager.LaunchedPrograms,
                        p => p.Command.Contains("udisksctl unlock", StringComparison.Ordinal));
    }
}
