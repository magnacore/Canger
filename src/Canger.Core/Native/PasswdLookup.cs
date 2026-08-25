// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Canger.Core.Native;

/// <summary>
/// Resolves numeric user and group ids to names through the system name service, so that
/// LDAP, SSSD and other NSS backends work rather than only local <c>/etc/passwd</c> entries.
/// </summary>
/// <remarks>
/// <para>
/// Only the first member of <c>struct passwd</c> and <c>struct group</c> is read — the name
/// pointer — so no full struct layout needs to be mirrored, which keeps this robust across
/// libc versions and architectures.
/// </para>
/// <para>
/// <c>getpwuid</c> and <c>getgrgid</c> return a pointer into a shared static buffer and are not
/// thread safe, so calls are serialised. Results are cached because a directory listing resolves
/// the same handful of ids over and over.
/// </para>
/// </remarks>
internal static partial class PasswdLookup
{
    private const string LibraryName = "libc";

    private static readonly ConcurrentDictionary<uint, string?> UserNames = new();
    private static readonly ConcurrentDictionary<uint, string?> GroupNames = new();
    private static readonly Lock NativeCallGate = new();

    [LibraryImport(LibraryName, EntryPoint = "getpwuid")]
    private static partial nint GetPasswordEntry(uint uid);

    [LibraryImport(LibraryName, EntryPoint = "getgrgid")]
    private static partial nint GetGroupEntry(uint gid);

    /// <summary>The real user id of this process. See <c>getuid(2)</c>.</summary>
    /// <returns>The user id.</returns>
    [LibraryImport(LibraryName, EntryPoint = "getuid")]
    internal static partial uint GetUserId();

    /// <summary>The real group id of this process. See <c>getgid(2)</c>.</summary>
    /// <returns>The group id.</returns>
    [LibraryImport(LibraryName, EntryPoint = "getgid")]
    internal static partial uint GetGroupId();

    /// <summary>Returns the login name for <paramref name="uid"/>, or <see langword="null"/>.</summary>
    /// <param name="uid">The user id to resolve.</param>
    /// <returns>The user name, or <see langword="null"/> when the id is unknown.</returns>
    internal static string? UserName(uint uid) =>
        UserNames.GetOrAdd(uid, static id => ReadName(() => GetPasswordEntry(id)));

    /// <summary>Returns the group name for <paramref name="gid"/>, or <see langword="null"/>.</summary>
    /// <param name="gid">The group id to resolve.</param>
    /// <returns>The group name, or <see langword="null"/> when the id is unknown.</returns>
    internal static string? GroupName(uint gid) =>
        GroupNames.GetOrAdd(gid, static id => ReadName(() => GetGroupEntry(id)));

    /// <summary>
    /// Invokes a name-service lookup and reads the name pointer, which is the first field of both
    /// <c>struct passwd</c> and <c>struct group</c>.
    /// </summary>
    private static string? ReadName(Func<nint> lookup)
    {
        lock (NativeCallGate)
        {
            nint entry = lookup();
            if (entry == 0)
            {
                return null;
            }

            nint namePointer = Marshal.ReadIntPtr(entry);
            return namePointer == 0 ? null : Marshal.PtrToStringUTF8(namePointer);
        }
    }
}
