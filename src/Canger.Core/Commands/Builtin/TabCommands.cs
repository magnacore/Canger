// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// Opens a new tab, at the lowest free number.
/// </summary>
/// <remarks>
/// A quantifier names the tab instead, so <c>3gn</c> opens tab three specifically — which is how
/// a tab can be given a number worth remembering rather than whichever came next.
/// </remarks>
[Command("tab_new", Summary = "Open a new tab: tab_new [<path>]")]
public sealed class TabNewCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // A label names the tab, which is how a configuration keeps a set of working directories
        // one keystroke away and still says which is which.
        (string path, string? label) = Split(Rest(1));
        string? target = path.Length > 0 ? path : null;

        if (Quantifier is { } number and > 0)
        {
            FileManager.OpenTab(number, target);
            Name(label);
            return;
        }

        int free = 1;

        while (FileManager.Tabs.ContainsKey(free))
        {
            free++;
        }

        FileManager.OpenTab(free, target);
        Name(label);
    }

    /// <summary>Separates the path from a trailing <c>label=</c>.</summary>
    /// <param name="rest">Everything after the command name.</param>
    /// <returns>The path, and the label when the line carried one.</returns>
    /// <remarks>
    /// The label runs to the end of the line rather than to the next space, because ranger's
    /// equivalent is a quoted string that may contain them —
    /// <c>fm.tab_new(narg='Task Capture Bin', ...)</c> is one of the user configurations this
    /// has to survive. Splitting it as an ordinary named argument kept only <c>Task</c>.
    /// </remarks>
    private static (string Path, string? Label) Split(string rest)
    {
        int marker = rest.LastIndexOf("label=", StringComparison.Ordinal);

        if (marker < 0)
        {
            return (rest, null);
        }

        return (rest[..marker].TrimEnd(), rest[(marker + "label=".Length)..].Trim());
    }

    /// <summary>Names the tab, when the line said what to call it.</summary>
    private void Name(string? label)
    {
        if (label is { Length: > 0 })
        {
            FileManager.CurrentTab.Label = label;
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction) => CompleteDirectoryContent();
}

/// <summary>Switches to a numbered tab, creating it if there is none.</summary>
[Command("tab_open", Summary = "Switch to a tab by number: tab_open <n> [<path>]")]
public sealed class TabOpenCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!int.TryParse(Argument(1), CultureInfo.InvariantCulture, out int number) || number < 1)
        {
            FileManager.Notify("Usage: tab_open <n> [<path>]", isError: true);
            return;
        }

        string path = Rest(2);
        FileManager.OpenTab(number, path.Length > 0 ? path : null);
    }
}

/// <summary>Closes a tab.</summary>
[Command("tab_close", Summary = "Close the current tab: tab_close [<n>]")]
public sealed class TabCloseCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        int? number = int.TryParse(Argument(1), CultureInfo.InvariantCulture, out int explicitly)
            ? explicitly
            : null;

        if (!FileManager.CloseTab(number))
        {
            FileManager.Notify("cannot close the last tab", isError: true);
        }
    }
}

/// <summary>
/// Moves the focus a number of tabs along, wrapping at either end.
/// </summary>
/// <remarks>
/// Wrapping rather than stopping means <c>gt</c> alone cycles through every tab, which is how the
/// binding is used in practice. A quantifier switches to that tab number directly instead.
/// </remarks>
[Command("tab_move", Summary = "Switch tabs by an offset: tab_move <offset>")]
public sealed class TabMoveCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        // A quantifier names a tab outright, which is what makes 3gt go to tab three.
        if (Quantifier is { } wanted and > 0)
        {
            FileManager.OpenTab(wanted);
            return;
        }

        if (!int.TryParse(Argument(1), CultureInfo.InvariantCulture, out int offset))
        {
            offset = 1;
        }

        int[] numbers = [.. FileManager.Tabs.Keys.Order()];

        if (numbers.Length == 0)
        {
            return;
        }

        int current = Array.IndexOf(numbers, FileManager.CurrentTabNumber);

        // Modulo in C# keeps the sign of the dividend, so a negative offset needs correcting
        // before it can index.
        int next = (((current + offset) % numbers.Length) + numbers.Length) % numbers.Length;

        FileManager.OpenTab(numbers[next]);
    }
}

/// <summary>
/// Moves the current tab to a different position, taking it with you.
/// </summary>
/// <remarks>
/// Unlike <c>tab_move</c>, which changes which tab you are looking at, this changes where the
/// current tab sits — so the tabs can be put in the order the work wants.
/// </remarks>
[Command("tab_shift", Summary = "Move the current tab: tab_shift <offset>")]
public sealed class TabShiftCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!int.TryParse(Argument(1), CultureInfo.InvariantCulture, out int offset))
        {
            offset = 1;
        }

        FileManager.ShiftTab(FileManager.CurrentTabNumber + offset);
    }
}

/// <summary>Reopens the most recently closed tab.</summary>
[Command("tab_restore", Summary = "Reopen the last closed tab.")]
public sealed class TabRestoreCommand : CangerCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        if (!FileManager.RestoreTab())
        {
            FileManager.Notify("no closed tabs to restore", isError: true);
        }
    }
}
