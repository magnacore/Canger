// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Tasks;

/// <summary>
/// Work that can say how much it has moved, but not how much there is.
/// </summary>
/// <remarks>
/// Deliberately smaller than <see cref="ISizedWork"/>, which the copy engine implements and which
/// promises a total, a remainder and a rate — enough for the queue to weight jobs against one
/// another and predict an ending. An archiver offers none of that: the file can be watched
/// growing, and how large it will end up depends on a compression ratio nobody knows in advance.
/// Saying so with a smaller contract keeps such a job out of the weighted figures, where an
/// invented total would spoil them, while still letting the task view show what is known.
/// </remarks>
public interface IReportsBytes
{
    /// <summary>Bytes moved so far, or <see langword="null"/> when nothing has been reported.</summary>
    long? Transferred { get; }
}
