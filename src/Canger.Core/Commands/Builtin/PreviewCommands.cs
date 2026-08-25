// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Previews;

namespace Canger.Core.Commands.Builtin;

/// <summary>Forgets every cached preview, so they are generated afresh.</summary>
[Command("reset_previews", Summary = "Discard cached previews and generate them again.")]
public sealed class ResetPreviewsCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // Useful when a file changed underneath a preview, or after editing the preview script.
        FileManager.InvalidatePreviews();
        FileManager.Notify("previews reset");
    }
}

/// <summary>Throws away everything Canger has cached about the filesystem and starts again.</summary>
/// <remarks>
/// <para>
/// <c>map &lt;C-r&gt; reset</c>. Ranger's is <c>core/actions.py:64-77</c>: it drops the preview
/// cache, discards every cached directory with <c>garbage_collect(-1)</c>, re-enters where it was,
/// and returns to normal mode. Dropping the directories is what makes a measured cumulative size
/// go away and the entry count come back, because the fresh <c>Directory</c> has never been
/// measured.
/// </para>
/// <para>
/// This did not exist. <c>&lt;C-r&gt; reset</c> resolved by unambiguous-prefix abbreviation to
/// <c>reset_previews</c>, so Ctrl-R quietly did a fraction of its job — previews were discarded
/// and nothing else was.
/// </para>
/// </remarks>
[Command("reset", Summary = "Discard cached directories and previews, and reload.")]
public sealed class ResetCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string here = FileManager.CurrentTab.Path;

        FileManager.InvalidatePreviews();

        // Every cached directory goes, which is what clears measured sizes, entry counts and
        // marks. The tabs are then pointed back at where they were, which re-interns what they
        // need and leaves everything else unread until it is looked at.
        FileManager.Directories.Clear();

        foreach (Model.Tab tab in FileManager.Tabs.Values)
        {
            tab.Enter(tab.Path, recordHistory: false);
        }

        FileManager.CurrentTab.Enter(here, recordHistory: false);
        FileManager.ChangeMode("normal");
        FileManager.ReloadCurrentDirectory();
        FileManager.Notify("reset");
    }
}

/// <summary>Shows the selection in the pager.</summary>
[Command("display_file", Summary = "Show the selected file in the pager.")]
public sealed class DisplayFileCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (FileManager.CurrentFile is not { } file || file.IsDirectory)
        {
            FileManager.Notify("nothing to display", isError: true);
            return;
        }

        // Read directly rather than through the preview provider: the pager has the whole screen,
        // so it wants the file itself rather than a preview sized for a column.
        byte[] content = FileManager.FileSystem.ReadFilePrefix(file.Path, 512 * 1024);

        if (content.Length == 0)
        {
            FileManager.Notify("nothing to display", isError: true);
            return;
        }

        FileManager.ShowInPager(System.Text.Encoding.UTF8.GetString(content));
    }
}

/// <summary>Closes the pager.</summary>
[Command("pager_close", Summary = "Close the pager.")]
public sealed class PagerCloseCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute() => FileManager.ClosePager();
}
