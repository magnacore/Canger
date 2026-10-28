// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Ui;

/// <summary>
/// The stretch after another program has had the terminal, during which every frame is written
/// in full.
/// </summary>
/// <remarks>
/// <para>
/// A program given the terminal can leave something behind that still holds it. The reported case
/// was a <c>.verso</c> file: nothing in <c>rifle.conf</c> claimed it, so it fell through to
/// <c>xdg-open</c>, which started a graphical editor and exited at once. Canger took its screen
/// back and repainted — and a second or two later the editor, still attached to the terminal,
/// wrote <c>Sandbox: CanCreateUserNamespace() clone() failure: EPERM</c> onto it. One line at the
/// bottom scrolls the whole screen up by one, and from then on the interface is drawn a row out
/// of place: the title bar gone, the status line off the end.
/// </para>
/// <para>
/// Nothing brought it back, because a frame is otherwise written as the difference against what
/// Canger believes is on screen, and that belief is now wrong in every cell. <c>&lt;C-l&gt;</c>
/// repaired it, which is the proof that only the trigger was missing: for a short while after a
/// program hands the terminal back, frames are painted in full and the loop looks up often enough
/// to do it without waiting for a keystroke.
/// </para>
/// <para>
/// Ranger breaks in exactly the same way and leaves the user to press <c>&lt;C-l&gt;</c>; its
/// <c>redraw_window</c> exists for the same reason. Healing without being asked is a deliberate
/// step past it. The window is long enough to cover a graphical program's first complaints, which
/// is when they nearly always arrive, and anything later is still a keypress away from being
/// repaired.
/// </para>
/// </remarks>
internal static class ScreenSettling
{
    /// <summary>How long the screen is treated as untrustworthy.</summary>
    private const long WindowMilliseconds = 10_000;

    /// <summary>How often to look at it during that time.</summary>
    private const int IntervalMilliseconds = 500;

    /// <summary>When the settling window that starts now would end.</summary>
    /// <param name="now">The current tick count.</param>
    /// <returns>The tick the window closes at.</returns>
    internal static long From(long now) => now + WindowMilliseconds;

    /// <summary>Whether a frame drawn now must be written in full.</summary>
    /// <param name="now">The current tick count.</param>
    /// <param name="until">When the window closes, or zero when there is none.</param>
    /// <returns><see langword="true"/> while the screen cannot be trusted.</returns>
    internal static bool Covers(long now, long until) => until > 0 && now < until;

    /// <summary>Shortens a wait so the screen is looked at again while it may be wrong.</summary>
    /// <param name="timeout">How long the loop would otherwise sleep, in milliseconds.</param>
    /// <param name="now">The current tick count.</param>
    /// <param name="until">When the window closes, or zero when there is none.</param>
    /// <returns>The wait to use.</returns>
    /// <remarks>
    /// Without this the repair would wait on a keystroke: the loop sleeps for the whole idle
    /// delay — two seconds by default, and longer where it is configured so — and the screen
    /// stays wrong for all of it. It is the same mistake the resize path made before it learned
    /// to wake the loop rather than only set a flag.
    /// </remarks>
    internal static int Clamp(int timeout, long now, long until) =>
        Covers(now, until) ? Math.Min(timeout, IntervalMilliseconds) : timeout;
}
