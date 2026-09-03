// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;

namespace Canger.Core.FileSystem;

/// <summary>
/// Reads what a zip archive holds, from the index it keeps at its end.
/// </summary>
/// <remarks>
/// <para>
/// A zip file ends with a central directory listing every entry and its uncompressed size, so what
/// an archive will produce can be known exactly without decompressing any of it. That is what lets
/// unpacking show a true percentage: <c>unzip</c> names each entry as it writes it, and this says
/// what each of those names is worth.
/// </para>
/// <para>
/// Best effort, like <see cref="CompressedStreamSize"/>. A file that cannot be read this way — a
/// zip64 archive of more than 65 535 entries, a truncated download, something that is not a zip at
/// all — yields nothing, and the caller falls back to showing no total rather than a wrong one.
/// </para>
/// </remarks>
public static class ZipDirectory
{
    /// <summary>The fixed part of an end-of-central-directory record.</summary>
    private const int EndRecordLength = 22;

    /// <summary>The comment that may follow it, at most.</summary>
    private const int MaximumComment = 0xFFFF;

    /// <summary>The fixed part of one central directory entry.</summary>
    private const int EntryLength = 46;

    /// <summary>What an archive holds.</summary>
    /// <param name="path">The archive.</param>
    /// <returns>
    /// Each entry's name as stored, with the size it will occupy once written, or an empty list
    /// when the index cannot be read. Directory entries are included at zero, because
    /// <c>unzip</c> announces those too.
    /// </returns>
    public static IReadOnlyList<(string Name, long Size)> Entries(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (Locate(file) is not { } end)
            {
                return [];
            }

            return Read(file, end.Offset, end.Count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>The total the entries come to.</summary>
    /// <param name="path">The archive.</param>
    /// <returns>The uncompressed size, or <see langword="null"/> when the index cannot be read.</returns>
    public static long? UncompressedSize(string path)
    {
        IReadOnlyList<(string Name, long Size)> entries = Entries(path);

        return entries.Count == 0 ? null : entries.Sum(entry => entry.Size);
    }

    /// <summary>Finds the end-of-central-directory record and reads where the index begins.</summary>
    /// <param name="file">The archive.</param>
    /// <returns>The index's offset and entry count, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Searched backwards from the end because a trailing comment of arbitrary length may follow
    /// the record, so it is not at a fixed position. The comment can be 64 KB, which bounds how
    /// far back there is any point looking.
    /// </remarks>
    private static (long Offset, int Count)? Locate(FileStream file)
    {
        int window = (int)Math.Min(file.Length, EndRecordLength + MaximumComment);

        if (window < EndRecordLength)
        {
            return null;
        }

        byte[] tail = new byte[window];
        file.Seek(file.Length - window, SeekOrigin.Begin);

        if (file.ReadAtLeast(tail, window, throwOnEndOfStream: false) != window)
        {
            return null;
        }

        for (int at = window - EndRecordLength; at >= 0; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at)) != 0x06054B50)
            {
                continue;
            }

            int count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(at + 10));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at + 16));

            // 0xFFFFFFFF is zip64's "look in the other record", which is not read here; an
            // offset past the file is a damaged archive. Either way there is nothing to list.
            return offset >= file.Length ? null : (offset, count);
        }

        return null;
    }

    /// <summary>Walks the central directory, one entry at a time.</summary>
    private static IReadOnlyList<(string Name, long Size)> Read(FileStream file, long offset,
                                                               int count)
    {
        long length = file.Length - offset;

        // The index is names and numbers; anything enormous here means the offset was wrong.
        if (length <= 0 || length > 64 << 20)
        {
            return [];
        }

        byte[] index = new byte[length];
        file.Seek(offset, SeekOrigin.Begin);

        if (file.ReadAtLeast(index, index.Length, throwOnEndOfStream: false) != index.Length)
        {
            return [];
        }

        List<(string Name, long Size)> entries = new(count);
        int at = 0;

        for (int entry = 0; entry < count; entry++)
        {
            if (at + EntryLength > index.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(at)) != 0x02014B50)
            {
                break;
            }

            long size = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(at + 24));
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(at + 28));
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(at + 30));
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(at + 32));

            if (at + EntryLength + nameLength > index.Length)
            {
                break;
            }

            // Names are stored as bytes with a flag for UTF-8; reading them as UTF-8 either way
            // is what every other reader does, and a name that does not decode is still matched
            // by its own bytes' worth of characters.
            entries.Add((Encoding.UTF8.GetString(index, at + EntryLength, nameLength), size));

            at += EntryLength + nameLength + extraLength + commentLength;
        }

        return entries;
    }
}
