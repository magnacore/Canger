// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.FileSystem;
using Canger.Core.Native;

namespace Canger.Core.FileOperations;

/// <summary>
/// Copies a file's metadata alongside its contents.
/// </summary>
/// <remarks>
/// <para>
/// A copy that loses the executable bit produces a script that will not run, and one that loses
/// the modification time makes every backup tool think everything changed. Preserving metadata is
/// not a nicety.
/// </para>
/// <para>
/// Each attribute is copied independently and a failure is ignored rather than propagated.
/// Ownership usually cannot be set without privilege, and extended attributes are not supported
/// everywhere; neither should abandon a copy whose data arrived intact.
/// </para>
/// </remarks>
public static class MetadataCopier
{
    /// <summary>
    /// Copies permissions, timestamps, ownership and extended attributes.
    /// </summary>
    /// <param name="fileSystem">Used to read the source's metadata.</param>
    /// <param name="source">The file to copy from.</param>
    /// <param name="destination">The file to copy to.</param>
    /// <param name="followSymbolicLinks">
    /// Whether to describe the target of a link rather than the link itself.
    /// </param>
    public static void Copy(IFileSystem fileSystem, string source, string destination,
                            bool followSymbolicLinks = true)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        if (fileSystem.GetStatus(source, followSymbolicLinks) is not { } status)
        {
            return;
        }

        // Permissions and times cannot be set through a symbolic link, and doing so would change
        // the target rather than the link.
        if (!status.IsSymbolicLink)
        {
            TryCopyPermissions(destination, status);
            TryCopyTimes(destination, status);
        }

        TryCopyOwnership(destination, status);
        TryCopyExtendedAttributes(source, destination);
    }

    private static void TryCopyPermissions(string destination, FileStatus status)
    {
        try
        {
            File.SetUnixFileMode(destination, status.Permissions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
        {
            // The data is copied; the mode is a best effort.
        }
    }

    private static void TryCopyTimes(string destination, FileStatus status)
    {
        try
        {
            File.SetLastWriteTimeUtc(destination, status.ModifyTime.UtcDateTime);
            File.SetLastAccessTimeUtc(destination, status.AccessTime.UtcDateTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or ArgumentOutOfRangeException)
        {
            // Some filesystems refuse timestamps outside their range.
        }
    }

    /// <summary>
    /// Copies ownership, which ordinarily requires privilege.
    /// </summary>
    /// <remarks>
    /// Attempted only when it could succeed — running as root, or the ids already match — so the
    /// ordinary case does not make a syscall that is certain to fail.
    /// </remarks>
    private static void TryCopyOwnership(string destination, FileStatus status)
    {
        if (!UserDatabase.IsRoot &&
            (status.Uid != UserDatabase.CurrentUserId || status.Gid != UserDatabase.CurrentGroupId))
        {
            return;
        }

        _ = status.IsSymbolicLink
            ? Libc.Lchown(destination, status.Uid, status.Gid)
            : Libc.Chown(destination, status.Uid, status.Gid);
    }

    /// <summary>
    /// Copies extended attributes, which carry things like SELinux labels and file capabilities.
    /// </summary>
    private static void TryCopyExtendedAttributes(string source, string destination)
    {
        nint listLength = Libc.ListExtendedAttributes(source, null, 0);
        if (listLength <= 0)
        {
            return;
        }

        byte[] names = new byte[listLength];
        if (Libc.ListExtendedAttributes(source, names, (nuint)listLength) != listLength)
        {
            return;
        }

        // The names arrive as one NUL-separated block.
        foreach (string name in Encoding.UTF8.GetString(names)
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            nint valueLength = Libc.GetExtendedAttribute(source, name, null, 0);
            if (valueLength < 0)
            {
                continue;
            }

            byte[] value = new byte[valueLength];
            if (Libc.GetExtendedAttribute(source, name, value, (nuint)valueLength) == valueLength)
            {
                Libc.SetExtendedAttribute(destination, name, value, (nuint)valueLength, 0);
            }
        }
    }
}
