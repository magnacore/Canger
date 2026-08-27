// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Devices;
using Canger.Core.Model;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// The list of removable drives, where they can be mounted, unmounted and removed safely.
/// </summary>
/// <remarks>
/// <para>
/// Canger's own; ranger has no such view, and no removable-media handling at all. It is shaped
/// after the task view so that it reads as part of the same program rather than as something
/// bolted on: an overlay in the same place, a cursor moved the same way, and a key map of its own.
/// </para>
/// <para>
/// It colours itself as the task view, using <see cref="ContextKey.InTaskview"/>. The context keys
/// are generated from ranger's <c>gui/context.py</c> and must not gain members of Canger's own —
/// the generator would drop them and the count would no longer match. Since the two views are the
/// same kind of overlay, every colourscheme ever written for ranger already styles this one.
/// </para>
/// </remarks>
public sealed class DeviceView(IColorScheme colorScheme, DeviceSession session) : Widget
{
    /// <summary>
    /// Whether to divide sizes by 1024 and label them <c>Gi</c> rather than <c>G</c>.
    /// </summary>
    /// <remarks>
    /// The <c>binary_size_prefix</c> setting, so a drive's size here reads the same as the same
    /// drive's size in the file listing. <c>lsblk</c> answers in binary prefixes whatever this
    /// says — its <c>3.6T</c> and Canger's <c>4 T</c> are the same 4 000 752 599 040 bytes.
    /// </remarks>
    public bool BinaryPrefix { get; set; }

    /// <summary>What the second column says about a volume.</summary>
    /// <param name="device">The volume.</param>
    /// <returns>Its state in a word or two.</returns>
    public static string StateOf(BlockDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.MountPoint is { Length: > 0 } mountPoint)
        {
            return mountPoint;
        }

        return device.Kind == VolumeKind.Encrypted
            ? device.IsUnlocked ? "unlocked" : "locked"
            : "not mounted";
    }

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        CellStyle baseStyle = colorScheme.Resolve(StyleContext.Of(ContextKey.InTaskview));
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, baseStyle);

        CellStyle titleStyle = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InTaskview, ContextKey.Title));

        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, 1, titleStyle);
        screen.Write(Bounds.X, Bounds.Y, "Devices", titleStyle);

        if (session.Devices.Count == 0)
        {
            screen.Write(Bounds.X, Bounds.Y + 2,
                         new WideString(session.Problem ?? "No removable drives attached.")
                             .Truncate(Bounds.Width),
                         colorScheme.Resolve(StyleContext.Of(ContextKey.InTaskview,
                                                             ContextKey.Error)));
            return;
        }

        // The name column is as wide as the widest name, so the states line up down the page and
        // the eye can run down them. Bounded, so one absurdly long label cannot push the rest off
        // the screen.
        int names = Math.Min(Math.Max(session.Devices.Max(d => new WideString(d.DisplayName).Width), 8),
                             Math.Max(Bounds.Width / 3, 8));

        for (int i = 0; i < session.Devices.Count && i + 2 < Bounds.Height - 1; i++)
        {
            DrawDevice(screen, session.Devices[i], i, Bounds.Y + i + 2, names);
        }

        DrawKeys(screen, baseStyle);
    }

    /// <summary>Draws one drive.</summary>
    private void DrawDevice(ScreenBuffer screen, BlockDevice device, int index, int row, int names)
    {
        bool isCursor = index == session.CursorIndex;

        CellStyle style = colorScheme.Resolve(
            StyleContext.Of(ContextKey.InTaskview).With(isCursor, ContextKey.Selected));

        screen.Fill(Bounds.X, row, Bounds.Width, 1, style);

        string name = new WideString(device.DisplayName).Truncate(names).ToString().PadRight(names);
        string size = HumanReadable.Format(device.SizeBytes, BinaryPrefix).PadLeft(8);
        string kind = (device.Kind == VolumeKind.Encrypted
                          ? "LUKS"
                          : device.FileSystem ?? "?").PadRight(8);

        string line = $"  {name}  {size}  {kind}  {StateOf(device)}  ({device.DiskName})";

        screen.Write(Bounds.X, row, new WideString(line).Truncate(Bounds.Width), style);
    }

    /// <summary>Draws the reminder of what the keys do, along the bottom.</summary>
    /// <remarks>
    /// Spelled out because none of it is guessable and the actions are consequential. The task
    /// view can leave its keys to the manual; a key that cuts the power to a drive should not
    /// have to be looked up.
    /// </remarks>
    private void DrawKeys(ScreenBuffer screen, CellStyle baseStyle)
    {
        const string Keys =
            "  <CR> mount and enter   m mount   u unmount   l unlock   L lock   "
            + "e eject   r reload   q close";

        screen.Write(Bounds.X, Bounds.Y + Bounds.Height - 1,
                     new WideString(Keys).Truncate(Bounds.Width),
                     colorScheme.Resolve(StyleContext.Of(ContextKey.InTaskview,
                                                         ContextKey.Title)));
    }
}
