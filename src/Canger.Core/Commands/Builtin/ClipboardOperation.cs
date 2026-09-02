// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Input;
using Canger.Core.Model;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// How much is put on the clipboard relative to what is already there.
/// </summary>
public enum ClipboardMode
{
    /// <summary>Replace whatever was there.</summary>
    Set,

    /// <summary>Add to whatever was there.</summary>
    Add,

    /// <summary>Take away from whatever was there.</summary>
    Remove,

    /// <summary>Add what is absent and take away what is present.</summary>
    Toggle,
}

/// <summary>
/// The work behind <c>:copy</c> and <c>:cut</c>, which differ only in what happens on paste.
/// </summary>
/// <remarks>
/// Both accept a direction, which is what lets <c>dj</c> cut the next file and <c>3dj</c> the
/// next three without marking anything first — the range from the cursor is taken as the
/// selection, and the cursor lands past it so a repeat continues rather than repeating itself.
/// Ranger expresses this as <c>eval fm.cut(dirarg=dict(down=1), narg=quantifier)</c>; naming the
/// arguments says the same thing without an interpreter.
/// </remarks>
internal static class ClipboardOperation
{
    /// <summary>Runs a clipboard command.</summary>
    /// <param name="fileManager">Whose clipboard and selection are involved.</param>
    /// <param name="arguments">The command's <c>name=value</c> arguments.</param>
    /// <param name="quantifier">The count the user typed, if any.</param>
    /// <param name="cut">Whether the entries should be moved rather than copied.</param>
    public static void Run(IFileManager fileManager, Dictionary<string, string> arguments,
                           int? quantifier, bool cut)
    {
        ArgumentNullException.ThrowIfNull(fileManager);
        ArgumentNullException.ThrowIfNull(arguments);

        ClipboardMode mode = ParseMode(arguments);
        IReadOnlyList<FsNode> chosen = Choose(fileManager, arguments, quantifier);
        IReadOnlyList<string> updated =
            Combine(fileManager.CopyBuffer, [.. chosen.Select(f => f.Path)], mode);

        fileManager.SetCopyBufferPaths(updated, cut);
        fileManager.Notify($"{updated.Count} marked for {(cut ? "moving" : "copying")}");
    }

    /// <summary>Works out which entries the command applies to.</summary>
    private static IReadOnlyList<FsNode> Choose(IFileManager fileManager,
                                                Dictionary<string, string> arguments,
                                                int? quantifier)
    {
        bool hasDirection = arguments.Keys.Any(IsDirectionArgument);

        // With neither a direction nor a count, this is the plain form: whatever is marked, or
        // the entry under the cursor.
        if (!hasDirection && quantifier is null)
        {
            return fileManager.Selection;
        }

        DirectoryNode directory = fileManager.CurrentDirectory;

        // A bare count means "this many downwards", so 3yy yanks three files. The offset differs
        // because a count counts entries while a direction names a destination.
        Direction direction = hasDirection
            ? Direction.FromArguments(arguments)
            : new Direction(down: 1);

        int offset = hasDirection ? 1 : 0;

        (int destination, IReadOnlyList<FsNode> selection) = direction.Select(
            directory.Entries,
            directory.Cursor.Index,
            Math.Max(fileManager.Settings.ScrollOffset * 2, 1),
            over: quantifier,
            offset: offset);

        // Leaving the cursor past the range means repeating the key walks on rather than
        // taking the same files again.
        fileManager.CurrentTab.MoveCursor(destination);

        return selection;
    }

    /// <summary>Applies a mode to what is already on the clipboard.</summary>
    private static IReadOnlyList<string> Combine(IReadOnlyList<string> existing,
                                                 IReadOnlyList<string> chosen,
                                                 ClipboardMode mode)
    {
        if (mode == ClipboardMode.Set)
        {
            return chosen;
        }

        // Order is preserved rather than using a set, so a paste happens in the order the files
        // were gathered, which is what the user watched happen.
        List<string> combined = [.. existing];

        foreach (string entry in chosen)
        {
            bool present = combined.Contains(entry);

            switch (mode)
            {
                case ClipboardMode.Add when !present:
                    combined.Add(entry);
                    break;

                case ClipboardMode.Remove when present:
                    combined.Remove(entry);
                    break;

                case ClipboardMode.Toggle:
                    if (present)
                    {
                        combined.Remove(entry);
                    }
                    else
                    {
                        combined.Add(entry);
                    }

                    break;

                default:
                    break;
            }
        }

        return combined;
    }

    private static ClipboardMode ParseMode(Dictionary<string, string> arguments) =>
        arguments.TryGetValue("mode", out string? mode)
            ? mode.ToLowerInvariant() switch
            {
                "add" => ClipboardMode.Add,
                "remove" => ClipboardMode.Remove,
                "toggle" => ClipboardMode.Toggle,
                _ => ClipboardMode.Set,
            }
            : ClipboardMode.Set;

    private static bool IsDirectionArgument(string name) =>
        name is "down" or "up" or "to" or "left" or "right";
}
