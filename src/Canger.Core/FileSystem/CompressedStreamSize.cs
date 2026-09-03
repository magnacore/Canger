// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;

namespace Canger.Core.FileSystem;

/// <summary>
/// Reads how much data a compressed file will produce, from the file itself.
/// </summary>
/// <remarks>
/// <para>
/// gzip, lzip and xz each record the uncompressed size in the stream, so it can be had exactly
/// from a few bytes at the end without decompressing anything and without starting a program —
/// which matters because starting one would mean suspending the interface. It is what makes a
/// true percentage possible while unpacking: <c>tar</c> reports the bytes it has read out of the
/// <em>uncompressed</em> stream, and this is that stream's length.
/// </para>
/// <para>
/// Best effort by design. An unknown format, a truncated file or a shape not handled here returns
/// <see langword="null"/>, and the caller shows a byte count instead. A figure that comes back too
/// small — a concatenated gzip, whose trailer describes only the last member — is caught later by
/// <see cref="Tasks.MarkerProgress"/>, which drops a total the command's own output overshoots. So
/// a wrong answer here degrades to no answer rather than to a bar that lies.
/// </para>
/// </remarks>
public static class CompressedStreamSize
{
    /// <summary>A member header plus its trailer: the smallest possible lzip member.</summary>
    private const int LzipOverhead = 26;

    /// <summary>CRC, backward size, stream flags and the trailing magic.</summary>
    private const int XzFooter = 12;

    /// <summary>
    /// A ceiling on members walked, so a corrupt file cannot spin.
    /// </summary>
    /// <remarks>
    /// Members are megabytes in practice; a million of them is far past anything real and still
    /// costs only a bounded walk.
    /// </remarks>
    private const int MemberLimit = 1 << 20;

    /// <summary>How much data this file will produce when decompressed.</summary>
    /// <param name="path">The compressed file.</param>
    /// <returns>The uncompressed size, or <see langword="null"/> when it cannot be read.</returns>
    public static long? Of(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using FileStream file =
                new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Span<byte> magic = stackalloc byte[6];

            if (file.Length < magic.Length || !Fill(file, 0, magic))
            {
                return null;
            }

            // Sniffed rather than taken from the extension: the name is the user's, the bytes are
            // the format's, and an archive named `.tar.gz` that is really xz is a real thing.
            if (magic is [0x4C, 0x5A, 0x49, 0x50, ..])
            {
                return Lzip(file);
            }

            if (magic is [0x1F, 0x8B, ..])
            {
                return Gzip(file);
            }

            return magic is [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00] ? Xz(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            // Unreadable, vanished, or on something that will not seek. No total, which the
            // caller already knows how to show.
            return null;
        }
    }

    /// <summary>Sums the members' declared sizes, walking back from the end.</summary>
    /// <param name="file">The lzip file.</param>
    /// <returns>The total, or <see langword="null"/> when the chain does not reach the start.</returns>
    /// <remarks>
    /// Each member ends with its own uncompressed size and its own length on disk, and the second
    /// is what makes the previous member findable. Walking the whole chain rather than reading the
    /// last trailer is what makes a concatenated archive come out right — measured: two members of
    /// 17 448 960 report 34 897 920, which is what <c>lzip -l</c> says. Landing anywhere but
    /// exactly the start means the chain was misread, and a partial sum would be worse than none.
    /// </remarks>
    private static long? Lzip(FileStream file)
    {
        Span<byte> trailer = stackalloc byte[20];
        long total = 0;
        long end = file.Length;

        for (int member = 0; member < MemberLimit; member++)
        {
            if (end == 0)
            {
                return total;
            }

            if (end < LzipOverhead || !Fill(file, end - trailer.Length, trailer))
            {
                return null;
            }

            long data = BinaryPrimitives.ReadInt64LittleEndian(trailer[4..]);
            long size = BinaryPrimitives.ReadInt64LittleEndian(trailer[12..]);

            if (data < 0 || size < LzipOverhead || size > end || total > long.MaxValue - data)
            {
                return null;
            }

            total += data;
            end -= size;
        }

        return null;
    }

    /// <summary>Reads the trailing size field.</summary>
    /// <param name="file">The gzip file.</param>
    /// <returns>The size the last member declares.</returns>
    /// <remarks>
    /// gzip stores the size modulo 4 GiB and describes only the final member, so this is short for
    /// anything larger and for a concatenated file. Both cases end the same way: the count runs
    /// past the total, and the total is dropped in favour of the byte figure.
    /// </remarks>
    private static long? Gzip(FileStream file)
    {
        Span<byte> tail = stackalloc byte[4];

        return file.Length >= 18 && Fill(file, file.Length - tail.Length, tail)
            ? BinaryPrimitives.ReadUInt32LittleEndian(tail)
            : null;
    }

    /// <summary>Sums the uncompressed sizes the stream index lists.</summary>
    /// <param name="file">The xz file.</param>
    /// <returns>The total, or <see langword="null"/> when the index cannot be read.</returns>
    /// <remarks>
    /// The footer names the index that precedes it, and the index carries one record per block —
    /// so a file compressed across several threads, which is many blocks in one stream, still adds
    /// up exactly. Only the last stream is read; concatenated streams come out short, and are
    /// caught the same way gzip's are.
    /// </remarks>
    private static long? Xz(FileStream file)
    {
        Span<byte> footer = stackalloc byte[XzFooter];

        if (file.Length < XzFooter || !Fill(file, file.Length - XzFooter, footer) ||
            footer[10] != 0x59 || footer[11] != 0x5A)
        {
            return null;
        }

        // Stored as "one less, in units of four", the encoding the format defines.
        long indexSize = ((long)BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]) + 1) * 4;
        long start = file.Length - XzFooter - indexSize;

        if (start < 0 || indexSize > 1 << 20)
        {
            return null;
        }

        byte[] index = new byte[indexSize];

        if (!Fill(file, start, index) || index[0] != 0x00)
        {
            return null;
        }

        int at = 1;

        if (!ReadVarint(index, ref at, out long records) || records < 0)
        {
            return null;
        }

        long total = 0;

        for (long record = 0; record < records; record++)
        {
            if (!ReadVarint(index, ref at, out _) ||
                !ReadVarint(index, ref at, out long uncompressed) ||
                uncompressed < 0 || total > long.MaxValue - uncompressed)
            {
                return null;
            }

            total += uncompressed;
        }

        return total;
    }

    /// <summary>Reads one of the variable-length integers the xz index is written in.</summary>
    /// <param name="data">The index.</param>
    /// <param name="at">Where to read from; advanced past the value.</param>
    /// <param name="value">The value read.</param>
    /// <returns>Whether a complete, in-range value was there.</returns>
    private static bool ReadVarint(ReadOnlySpan<byte> data, ref int at, out long value)
    {
        value = 0;

        // Nine groups of seven bits is the format's own limit, and is also what keeps the shift
        // below from running off the end of a long.
        for (int shift = 0; shift < 63 && at < data.Length; shift += 7)
        {
            byte b = data[at++];
            value |= (long)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Reads exactly as many bytes as the buffer holds, from a given offset.</summary>
    /// <param name="file">The file to read.</param>
    /// <param name="offset">Where to read from.</param>
    /// <param name="buffer">What to fill.</param>
    /// <returns>Whether the whole buffer was filled.</returns>
    private static bool Fill(FileStream file, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > file.Length)
        {
            return false;
        }

        file.Seek(offset, SeekOrigin.Begin);
        return file.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }
}
