// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Canger.Core.Native;

namespace Canger.Core.FileSystem;

/// <summary>
/// Resolves numeric user and group ids to names for display.
/// </summary>
/// <remarks>
/// Lookups go through the system name service, so users provided by LDAP, SSSD or any other NSS
/// backend resolve as well as local ones. Results are cached, since a directory listing asks for
/// the same few ids repeatedly. When an id has no name — a file owned by a deleted account, or a
/// container uid with no mapping — the number itself is shown, which is what <c>ls</c> does.
/// </remarks>
public static class UserDatabase
{
    /// <summary>Returns the user name for an id, falling back to the number.</summary>
    /// <param name="uid">The user id.</param>
    /// <returns>The user name, or the id rendered as a string.</returns>
    public static string UserName(uint uid) =>
        PasswdLookup.UserName(uid) ?? uid.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The user id this process is running as.
    /// </summary>
    /// <remarks>
    /// Used to decide whether a file belongs to the current user, which the status bar colours
    /// differently. Cached because it cannot change while the process runs.
    /// </remarks>
    public static uint CurrentUserId { get; } = PasswdLookup.GetUserId();

    /// <summary>The group id this process is running as.</summary>
    public static uint CurrentGroupId { get; } = PasswdLookup.GetGroupId();

    /// <summary>Whether this process is running as root.</summary>
    public static bool IsRoot => CurrentUserId == 0;

    /// <summary>Returns the group name for an id, falling back to the number.</summary>
    /// <param name="gid">The group id.</param>
    /// <returns>The group name, or the id rendered as a string.</returns>
    public static string GroupName(uint gid) =>
        PasswdLookup.GroupName(gid) ?? gid.ToString(CultureInfo.InvariantCulture);
}
