// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Vcs;

/// <summary>
/// Everything one version control system can be asked about a repository.
/// </summary>
/// <remarks>
/// <para>
/// Ranger implements this by swapping an object's class at runtime; here it is an ordinary
/// strategy, chosen once when a repository is found. The state that ranger keeps on the mutated
/// object lives in <see cref="VcsRepository"/> instead, so a backend is stateless and can be
/// shared between repositories.
/// </para>
/// <para>
/// Every method may throw <see cref="VcsException"/>. Callers are expected to treat that as
/// "this repository cannot be read just now" rather than as a failure worth stopping for: a
/// repository being rebased under you is normal, and the listing still has to draw.
/// </para>
/// </remarks>
public interface IVcsBackend
{
    /// <summary>The name used in settings, such as <c>git</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The directory whose presence marks a repository root, such as <c>.git</c>.
    /// </summary>
    string MarkerDirectory { get; }

    /// <summary>The program that is run, which is usually but not always the name.</summary>
    string Program { get; }

    /// <summary>The status of the repository as a whole, obtained as cheaply as possible.</summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The status.</returns>
    VcsStatus RootStatus(string root);

    /// <summary>
    /// The status of everything in the repository that is not in sync.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <returns>Paths relative to the root, and what each one's status is.</returns>
    /// <remarks>
    /// Only what differs is returned. A repository of ten thousand clean files should produce an
    /// empty dictionary, not ten thousand entries saying nothing happened.
    /// </remarks>
    IReadOnlyDictionary<string, VcsStatus> SubpathStatuses(string root);

    /// <summary>How the repository stands relative to the remote it tracks.</summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The status.</returns>
    VcsRemoteStatus RemoteStatus(string root);

    /// <summary>The current branch, where the backend has such a notion.</summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The branch name, or <see langword="null"/> when there is none.</returns>
    string? Branch(string root);

    /// <summary>The most recent revision.</summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The revision, or <see langword="null"/> when there is none yet.</returns>
    VcsCommit? Head(string root);

    /// <summary>Adds files to the index.</summary>
    /// <param name="root">The repository root.</param>
    /// <param name="paths">The files, or empty for everything.</param>
    void Add(string root, IReadOnlyList<string> paths);

    /// <summary>Takes files back out of the index.</summary>
    /// <param name="root">The repository root.</param>
    /// <param name="paths">The files, or empty for everything.</param>
    void Reset(string root, IReadOnlyList<string> paths);
}

/// <summary>
/// A version control command did not work.
/// </summary>
public sealed class VcsException : Exception
{
    /// <summary>Creates an exception.</summary>
    /// <param name="message">What went wrong.</param>
    public VcsException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    public VcsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception.</summary>
    public VcsException() : base("The version control command failed.")
    {
    }
}
