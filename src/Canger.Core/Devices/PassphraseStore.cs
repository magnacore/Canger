// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;

namespace Canger.Core.Devices;

/// <summary>
/// Remembers the passphrase for an encrypted drive, in the same place the desktop keeps it.
/// </summary>
/// <remarks>
/// <para>
/// The schema is not Canger's own. Thunar, Nautilus and GNOME Disks all store a LUKS passphrase
/// through libsecret under <c>org.gnome.GVfs.Luks.Password</c>, keyed by the volume's LUKS UUID,
/// and this uses exactly that. The consequence is the point: a passphrase saved in Thunar unlocks
/// the drive in Canger without being typed again, and one saved here works in Thunar. There is
/// one entry per drive, visible and removable in Seahorse, and it belongs to the desktop rather
/// than to this program.
/// </para>
/// <para>
/// Read from a real entry to be sure of it:
/// <code>
/// gvfs-luks-uuid : 61858679-035e-4001-94c3-0e6946fc85df
/// xdg:schema     : org.gnome.GVfs.Luks.Password
/// label          : Encryption passphrase for WDC WD40NMZW-59GX6S1 (4.0 TB Hard Disk)
/// </code>
/// Only the two attributes are looked up. The label is for the human reading Seahorse.
/// </para>
/// <para>
/// Everything goes through <c>secret-tool</c>, libsecret's own command. Storing writes the
/// passphrase to its standard input and looking it up reads it from a pipe, so it is never an
/// argument to anything and never reaches a file. Writing a Secret Service client would mean
/// several hundred lines of session negotiation and prompt handling, and it would be the newest
/// code in the repository handling the most sensitive thing in it.
/// </para>
/// </remarks>
/// <param name="runner">Runs <c>secret-tool</c>.</param>
/// <param name="available">
/// Whether the tool can be found. Injectable because otherwise every test of the prompt would
/// depend on whether the machine running it happens to have libsecret installed — which is how
/// this was written, and the tests passed only until it was.
/// </param>
public sealed class PassphraseStore(IProcessRunner runner, Func<bool>? available = null)
{
    /// <summary>The program everything goes through.</summary>
    public const string Tool = "secret-tool";

    /// <summary>The schema the desktop files these under.</summary>
    public const string Schema = "org.gnome.GVfs.Luks.Password";

    /// <summary>The attribute holding the volume's LUKS UUID.</summary>
    public const string UuidAttribute = "gvfs-luks-uuid";

    private readonly IProcessRunner _runner =
        runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>
    /// Whether saved passphrases can be reached at all.
    /// </summary>
    /// <remarks>
    /// False on a machine without libsecret's tools installed, where the feature simply is not
    /// offered rather than failing when it is used.
    /// </remarks>
    public bool IsAvailable => (available ?? (() => Executables.Exists(Tool)))();

    /// <summary>
    /// Finds the saved passphrase for a volume.
    /// </summary>
    /// <param name="luksUuid">The volume's LUKS UUID, as <c>lsblk</c> reports it.</param>
    /// <returns>The passphrase, or <see langword="null"/> when none is saved.</returns>
    /// <remarks>
    /// A missing entry and a locked keyring are both reported as "none", because the answer to
    /// either is the same: ask the user. <c>secret-tool</c> exits non-zero for the first and
    /// prompts for the second, which it cannot do here — so the absence of an answer is taken at
    /// face value and the passphrase is typed instead.
    /// </remarks>
    public string? Lookup(string luksUuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(luksUuid);

        if (!IsAvailable)
        {
            return null;
        }

        ProcessResult result = _runner.RunWithInput(
            new ProcessRequest($"{Tool} lookup {Quote(UuidAttribute)} {Quote(luksUuid)} "
                               + $"xdg:schema {Quote(Schema)}"),
            string.Empty);

        if (!result.Succeeded || result.Output.Length == 0)
        {
            return null;
        }

        // Exactly what came back, with nothing trimmed. This did trim a trailing newline, on the
        // assumption that one could only have got there by accident — and that was wrong in both
        // directions. Measured against libsecret itself: `lookup` adds no terminator of its own
        // (a 63-byte passphrase arrives as 63 bytes, no newline), and `store` keeps a newline
        // that was piped into it (4 bytes in, 4 bytes out). So a passphrase whose last character
        // is a newline is a passphrase that can be stored, and trimming it would hand cryptsetup
        // the wrong key while looking like the right one.
        return result.Output;
    }

    /// <summary>
    /// Saves a passphrase, replacing whatever was there for that volume.
    /// </summary>
    /// <param name="luksUuid">The volume's LUKS UUID.</param>
    /// <param name="label">What Seahorse should call it.</param>
    /// <param name="passphrase">The passphrase.</param>
    /// <returns>Why it could not be saved, or <see langword="null"/> when it was.</returns>
    public string? Save(string luksUuid, string label, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(luksUuid);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        if (!IsAvailable)
        {
            return $"{Tool} is not installed, so there is nowhere to keep it. "
                   + "Install libsecret-tools.";
        }

        ProcessResult result = _runner.RunWithInput(
            new ProcessRequest($"{Tool} store --label={Quote(label)} "
                               + $"{Quote(UuidAttribute)} {Quote(luksUuid)} "
                               + $"xdg:schema {Quote(Schema)}"),
            passphrase);

        return result.Succeeded
            ? null
            : result.Error?.Trim() is { Length: > 0 } why ? why : "the keyring refused it";
    }

    /// <summary>
    /// Names a drive the way the desktop names it, so one entry is recognisable beside the rest.
    /// </summary>
    /// <param name="device">The encrypted volume.</param>
    /// <returns>The label.</returns>
    /// <remarks>
    /// Deliberately the same shape gvfs uses — <c>Encryption passphrase for WDC WD40NMZW-59GX6S1
    /// (4.0 TB Hard Disk)</c> — so a list of them in Seahorse reads as one list rather than as
    /// two programs' worth. Only the attributes are used to find an entry, so a label that
    /// differs in a space costs nothing; a label that looks foreign costs the user a moment of
    /// doubt about what wrote it.
    /// </remarks>
    public static string LabelFor(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Decimal, one place, as udisks describes a drive: "4.0 TB", not Canger's "4 T". Down to
        // megabytes because stopping at gigabytes called a 64 MB volume "0.1 GB", which is both
        // ugly and wrong-looking beside the real ones.
        string size = device.SizeBytes switch
        {
            >= 1_000_000_000_000L =>
                $"{device.SizeBytes / 1_000_000_000_000d:0.0} TB",
            >= 1_000_000_000L =>
                $"{device.SizeBytes / 1_000_000_000d:0.0} GB",
            _ =>
                $"{device.SizeBytes / 1_000_000d:0} MB",
        };

        // "Hard Disk" for spinning platters and plain "Disk" for anything else, which is the
        // distinction the existing entries make: a TOSHIBA MQ01ABD100 is a "1.0 TB Hard Disk"
        // and a SanDisk Extreme is a "1.0 TB Disk".
        string kind = device.Rotational ? "Hard Disk" : "Disk";

        return $"Encryption passphrase for {device.DiskName} ({size} {kind})";
    }

    /// <summary>Quotes an argument for the shell.</summary>
    private static string Quote(string value) => Commands.MacroExpander.ShellQuote(value);
}
