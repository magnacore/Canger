// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Settings;

/// <summary>
/// Where a setting assignment applies.
/// </summary>
/// <remarks>
/// Canger resolves a setting by consulting the scopes in the order local, then tag, then global
/// — the same precedence as <c>ranger/container/settings.py:221-248</c>. A path-scoped value
/// therefore beats a tag-scoped one, which beats the global value.
/// </remarks>
/// <param name="PathPattern">
/// A regular expression matched against a directory path, or <see langword="null"/> when the
/// assignment is not path-scoped. <c>:setinpath</c> supplies a literal path, which is escaped
/// and anchored at the end, so <c>path=build</c> matches a build directory anywhere;
/// <c>:setinregex</c> supplies the pattern directly.
/// </param>
/// <param name="Tags">
/// The tag characters this assignment applies to, or <see langword="null"/> when the assignment
/// is not tag-scoped.
/// </param>
public readonly record struct SettingScope(string? PathPattern, IReadOnlyList<char>? Tags)
{
    /// <summary>The scope applying to everything, used when no narrower scope is given.</summary>
    public static SettingScope Global => default;

    /// <summary>Whether this is the global scope.</summary>
    public bool IsGlobal => PathPattern is null && Tags is null;

    /// <summary>Creates a scope restricted to paths matching a regular expression.</summary>
    /// <param name="pattern">The regular expression.</param>
    /// <returns>The scope.</returns>
    public static SettingScope ForPathPattern(string pattern) => new(pattern, null);

    /// <summary>Creates a scope restricted to files carrying any of the given tags.</summary>
    /// <param name="tags">The tag characters.</param>
    /// <returns>The scope.</returns>
    public static SettingScope ForTags(IReadOnlyList<char> tags) => new(null, tags);
}
