// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>
/// Something taking the keyboard for a while, ahead of the browser's own bindings.
/// </summary>
/// <remarks>
/// <para>
/// For handing the keys to a program Canger is running rather than to Canger: mpv's own
/// <c>[</c> and <c>]</c> for speed and <c>8</c> and <c>9</c> for volume are useless while every
/// one of them means something else to the browser. A grab lets the user say "these keys are for
/// that program now", and take them back.
/// </para>
/// <para>
/// Only ahead of the <em>browser</em>. The console, the pager, the task view and the device list
/// keep their keys, because each is a thing the user opened and expects to be able to close;
/// a grab that swallowed the key closing them would be a trap with no way out.
/// </para>
/// </remarks>
public interface IKeyGrab
{
    /// <summary>Offers one keystroke.</summary>
    /// <param name="key">The key, as <see cref="KeyCodes"/> numbers them.</param>
    /// <returns>
    /// <see langword="true"/> when the key was taken, so the browser does not also act on it.
    /// </returns>
    bool Handle(int key);
}
