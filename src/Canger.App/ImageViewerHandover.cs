// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;
using Canger.Core.Commands;
using Canger.Core.Processes;
using Canger.Rifle;

namespace Canger.App;

/// <summary>
/// Hands an image viewer the whole directory, opened at the file that was chosen.
/// </summary>
/// <remarks>
/// <para>
/// The <c>open_all_images</c> setting, which did nothing: opening one photograph opened exactly
/// one photograph, with no way to page through the rest without going back and opening each in
/// turn. Ranger installs the same rewrite as a rifle hook (<c>core/fm.py:246-284</c>).
/// </para>
/// <para>
/// It has to be a rewrite rather than simply passing more files, because each viewer names its
/// starting position differently and two of them count from one while another counts from zero.
/// Getting that wrong opens the right set at the wrong picture, which is worse than not doing it.
/// </para>
/// </remarks>
internal static partial class ImageViewerHandover
{
    /// <summary>Installs the rewrite on a launcher.</summary>
    /// <param name="opener">The launcher to hook, when it is a rifle one.</param>
    /// <param name="fileManager">Where the listing and the settings come from.</param>
    internal static void Install(IFileOpener opener, IFileManager fileManager)
    {
        if (opener is RifleLauncher rifle)
        {
            rifle.PreprocessAction = action => Rewrite(action, fileManager);
        }
    }

    /// <summary>Rewrites one command, or returns it unchanged.</summary>
    /// <param name="action">The command from the matching rifle rule.</param>
    /// <param name="browser">Where the listing and the settings come from.</param>
    /// <returns>The command to run.</returns>
    internal static string Rewrite(string action, IFileManager browser)
    {
        // Marked files are an explicit choice of what to open, so they are left alone — the
        // whole point of marking is that the selection is not "everything here".
        if (!browser.Settings.OpenAllImages ||
            browser.CurrentDirectory.MarkedEntries.Count > 0 ||
            !action.Contains("$@", StringComparison.Ordinal) ||
            Viewer().Match(action) is not { Success: true } viewer)
        {
            return action;
        }

        if (browser.CurrentTab.Selected is not { IsImage: true } current)
        {
            return action;
        }

        string[] images = [.. browser.CurrentDirectory.Entries
                                  .Where(e => e.IsImage)
                                  .Select(e => e.RelativePath)];

        int index = Array.IndexOf(images, current.RelativePath);

        // One image is the case the viewer already handles, and a cursor that is somehow not in
        // its own listing is not something to guess about.
        if (index < 0 || images.Length < 2)
        {
            return action;
        }

        string name = viewer.Groups[1].Value;

        // Each viewer spells "start here" differently, and pqiv counts from zero where the
        // others count from one.
        string? started = name switch
        {
            "sxiv" or "nsxiv" or "imv" =>
                action.Insert(viewer.Length, $"-n {index + 1} "),
            "feh" =>
                action.Insert(viewer.Length,
                              $"--start-at {RifleLauncher.Quote(current.RelativePath)} "),
            "pqiv" =>
                action.Insert(viewer.Length,
                              $"--action \"goto_file_byindex({index})\" "),
            _ => null,
        };

        if (started is null)
        {
            return action;
        }

        // A second `set --` overrides the first, so the files the launcher is about to attach
        // are replaced by these. Ranger relies on the same shell behaviour.
        StringBuilder rewritten = new("set --");

        foreach (string image in images)
        {
            if (!image.Contains('\0', StringComparison.Ordinal))
            {
                rewritten.Append(' ').Append(RifleLauncher.Quote(image));
            }
        }

        return rewritten.Append("; ").Append(started).ToString();
    }

    /// <summary>The image viewers whose ordering ranger knows how to preserve.</summary>
    [GeneratedRegex(@"^(feh|nsxiv|sxiv|imv|pqiv)\s+")]
    private static partial Regex Viewer();
}
