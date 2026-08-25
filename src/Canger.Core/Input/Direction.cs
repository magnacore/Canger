// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Canger.Core.Input;

/// <summary>
/// A movement request, and the arithmetic that turns it into a destination index.
/// </summary>
/// <remarks>
/// <para>
/// Every movement in Canger goes through this one kernel: the file list, the pager, the task
/// view and the console cursor. That is why <c>move down=1</c>, <c>move down=0.5 pages=True</c>,
/// <c>move to=-1</c> and <c>pager_move right=4</c> all behave consistently, and why a numeric
/// prefix multiplies any of them.
/// </para>
/// <para>
/// Two details are presence-sensitive rather than value-sensitive, and both matter.
/// <c>move right=0</c> counts as horizontal movement even though it moves nowhere, because the
/// key is present; that is how a binding can mean "act horizontally" without moving. And
/// <c>to=N</c> is sugar for <c>down=N absolute</c>, so a negative absolute position counts back
/// from the end, which is what makes <c>move to=-1</c> jump to the last entry.
/// </para>
/// </remarks>
public sealed class Direction
{
    /// <summary>Creates a movement request.</summary>
    /// <param name="down">Rows downward.</param>
    /// <param name="up">Rows upward. Equivalent to a negative <paramref name="down"/>.</param>
    /// <param name="right">Columns rightward, or levels deeper in the hierarchy.</param>
    /// <param name="left">Columns leftward, or levels up. Equivalent to a negative right.</param>
    /// <param name="to">
    /// An absolute destination. Sugar for <paramref name="down"/> together with
    /// <paramref name="absolute"/>.
    /// </param>
    /// <param name="absolute">Whether the amount is a position rather than an offset.</param>
    /// <param name="relative">Whether the amount is an offset. The default.</param>
    /// <param name="pages">Whether the amount counts pages rather than rows.</param>
    /// <param name="percentage">Whether the amount is a percentage of the total.</param>
    /// <param name="cycle">Whether movement past either end wraps around.</param>
    /// <param name="oneIndexed">Whether an absolute position counts from one rather than zero.</param>
    public Direction(double? down = null, double? up = null, double? right = null,
                     double? left = null, double? to = null, bool? absolute = null,
                     bool? relative = null, bool pages = false, bool percentage = false,
                     bool cycle = false, bool oneIndexed = false)
    {
        // "to" is sugar: it sets the vertical amount and forces absolute positioning.
        if (to is not null)
        {
            down = to;
            absolute = true;
        }

        DownValue = down;
        UpValue = up;
        RightValue = right;
        LeftValue = left;
        AbsoluteValue = absolute;
        RelativeValue = relative;
        Pages = pages;
        Percentage = percentage;
        Cycle = cycle;
        OneIndexed = oneIndexed;
    }

    private double? DownValue { get; }

    private double? UpValue { get; }

    private double? RightValue { get; }

    private double? LeftValue { get; }

    private bool? AbsoluteValue { get; }

    private bool? RelativeValue { get; }

    /// <summary>Whether the amount counts pages rather than rows.</summary>
    public bool Pages { get; }

    /// <summary>Whether the amount is a percentage of the total.</summary>
    public bool Percentage { get; }

    /// <summary>Whether movement past either end wraps around.</summary>
    public bool Cycle { get; }

    /// <summary>Whether an absolute position counts from one rather than zero.</summary>
    public bool OneIndexed { get; }

    /// <summary>How many times the last <see cref="Move"/> wrapped around.</summary>
    /// <remarks>Visual selection uses this to know it has passed an end of the list.</remarks>
    public int CycleCount { get; private set; }

    /// <summary>The vertical amount, downward being positive.</summary>
    public double Down => DownValue ?? (UpValue is { } up ? -up : 0);

    /// <summary>The vertical amount, upward being positive.</summary>
    public double Up => -Down;

    /// <summary>The horizontal amount, rightward being positive.</summary>
    public double Right => RightValue ?? (LeftValue is { } left ? -left : 0);

    /// <summary>The horizontal amount, leftward being positive.</summary>
    public double Left => -Right;

    /// <summary>Whether the amount is a position rather than an offset.</summary>
    public bool IsAbsolute => AbsoluteValue ?? (RelativeValue is { } relative ? !relative : false);

    /// <summary>Whether the amount is an offset from the current position.</summary>
    public bool IsRelative => !IsAbsolute;

    /// <summary>
    /// Whether this request concerns vertical movement, judged by whether a vertical amount was
    /// given at all rather than by its value.
    /// </summary>
    public bool IsVertical => DownValue is not null || UpValue is not null;

    /// <summary>
    /// Whether this request concerns horizontal movement, judged by whether a horizontal amount
    /// was given at all rather than by its value.
    /// </summary>
    public bool IsHorizontal => RightValue is not null || LeftValue is not null;

    /// <summary>The sign of the vertical amount: -1, 0 or 1.</summary>
    public int VerticalSign => Math.Sign(Down);

    /// <summary>The sign of the horizontal amount: -1, 0 or 1.</summary>
    public int HorizontalSign => Math.Sign(Right);

    /// <summary>
    /// Computes the destination index for a movement.
    /// </summary>
    /// <param name="amount">
    /// The amount to move, normally <see cref="Down"/> or <see cref="Right"/>.
    /// </param>
    /// <param name="over">
    /// The numeric prefix the user typed, which multiplies a relative amount or replaces an
    /// absolute one. <see langword="null"/> when none was typed.
    /// </param>
    /// <param name="minimum">The lowest valid index.</param>
    /// <param name="maximum">One past the highest valid index, normally the item count.</param>
    /// <param name="current">The index being moved from.</param>
    /// <param name="pageSize">Rows per page, used when <see cref="Pages"/> is set.</param>
    /// <param name="offset">
    /// Adjusts the upper bound. Selection passes 1 so the position just past the end is valid.
    /// </param>
    /// <returns>The destination index.</returns>
    public int Move(double amount, int? over = null, int minimum = 0, int maximum = 9999,
                    int current = 0, int pageSize = 1, int offset = 0)
    {
        double position = amount;

        if (over is { } quantifier)
        {
            // A prefix replaces an absolute destination but multiplies a relative one, so "5G"
            // goes to entry five while "5j" moves five rows.
            position = IsAbsolute
                ? (OneIndexed ? quantifier - 1 : quantifier)
                : position * quantifier;
        }

        if (Pages)
        {
            position *= pageSize;
        }
        else if (Percentage)
        {
            position *= maximum / 100.0;
        }

        if (IsAbsolute)
        {
            // A negative absolute position counts back from the end, so to=-1 is the last entry.
            if (position < minimum)
            {
                position += maximum;
            }
        }
        else
        {
            position += current;
        }

        double result;
        if (Cycle)
        {
            int span = maximum + offset - minimum;
            if (span <= 0)
            {
                CycleCount = 0;
                return minimum;
            }

            // Floored division, so wrapping past the start lands at the end rather than
            // producing a negative index.
            double wrapped = position - (span * Math.Floor(position / span));
            CycleCount = (int)Math.Floor(position / span);
            result = minimum + wrapped;
        }
        else
        {
            CycleCount = 0;
            result = Math.Max(Math.Min(position, maximum + offset - 1), minimum);
        }

        // Round toward the origin, so a half-page movement lands on the same row whichever
        // direction it came from.
        return amount < 0 ? (int)Math.Ceiling(result) : (int)result;
    }

    /// <summary>
    /// Computes the destination and the range of items spanned, for commands that act on
    /// everything between here and there, such as <c>dj</c> and <c>dgg</c>.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The list being moved over.</param>
    /// <param name="current">The index being moved from.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="over">The numeric prefix the user typed.</param>
    /// <param name="offset">Adjusts the upper bound.</param>
    /// <returns>The destination index and the items spanned, in list order.</returns>
    public (int Destination, IReadOnlyList<T> Selection) Select<T>(
        IReadOnlyList<T> items, int current, int pageSize, int? over = null, int offset = 1)
    {
        ArgumentNullException.ThrowIfNull(items);

        int destination = Move(Down, over, minimum: 0, maximum: items.Count + 1,
                               current: current, pageSize: pageSize);

        int low = Math.Clamp(Math.Min(destination, current), 0, items.Count);
        int high = Math.Clamp(Math.Max(destination, current) + offset, 0, items.Count);

        return (Math.Clamp(destination + offset - 1, 0, Math.Max(items.Count - 1, 0)),
                [.. items.Skip(low).Take(high - low)]);
    }

    /// <summary>
    /// Builds a movement request from the named arguments of a command, as written in a key
    /// binding such as <c>move down=1 pages=True</c>.
    /// </summary>
    /// <param name="arguments">The argument names and their textual values.</param>
    /// <param name="cycle">
    /// Whether movement wraps round the ends when the binding does not say. This is the
    /// <c>wrap_scroll</c> setting: ranger passes it as a default the binding may override
    /// (<c>core/actions.py:485</c>), which is the only thing that setting does.
    /// </param>
    /// <param name="oneIndexed">
    /// Whether an absolute destination counts from one. The <c>one_indexed</c> setting, passed
    /// the same way (<c>core/actions.py:486</c>).
    /// </param>
    /// <returns>The movement request.</returns>
    public static Direction FromArguments(IReadOnlyDictionary<string, string> arguments,
                                          bool cycle = false, bool oneIndexed = false)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return new Direction(
            down: Number(arguments, "down"),
            up: Number(arguments, "up"),
            right: Number(arguments, "right"),
            left: Number(arguments, "left"),
            to: Number(arguments, "to"),
            absolute: Flag(arguments, "absolute"),
            relative: Flag(arguments, "relative"),
            pages: Flag(arguments, "pages") ?? false,
            percentage: Flag(arguments, "percentage") ?? false,
            cycle: Flag(arguments, "cycle") ?? cycle,
            oneIndexed: Flag(arguments, "one_indexed") ?? oneIndexed);
    }

    private static double? Number(IReadOnlyDictionary<string, string> arguments, string name) =>
        arguments.TryGetValue(name, out string? text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    private static bool? Flag(IReadOnlyDictionary<string, string> arguments, string name) =>
        arguments.TryGetValue(name, out string? text)
            ? text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
              text.Equals("on", StringComparison.OrdinalIgnoreCase) ||
              text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
              text == "1"
            : null;
}
