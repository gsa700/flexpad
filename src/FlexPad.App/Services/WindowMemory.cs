using Avalonia;
using Avalonia.Controls;

namespace FlexPad.App.Services;

/// <summary>
/// Remembers where a window is by listening to its own move events while it is visible, so that
/// the value recorded at close time is one the window reported when it was really on screen.
/// </summary>
/// <remarks>
/// Reading <see cref="Window.Position"/> at closing time is not reliable everywhere: a window
/// that has already closed reports (0,0), and under GNOME on XWayland the Setup window reported a
/// position that did not match where it was. Move events are the window manager telling us where
/// it put the window; the last of those, taken while the window was visible, is the truth.
/// A (0,0) is ignored on the assumption that nobody parks a window exactly there on purpose,
/// which is a smaller risk than recording the closed-window zero every time.
/// </remarks>
public sealed class WindowMemory
{
    private PixelPoint? _last;

    public WindowMemory(Window w)
    {
        w.PositionChanged += (_, e) =>
        {
            if (w.IsVisible && (e.Point.X != 0 || e.Point.Y != 0)) _last = e.Point;
        };
        w.Opened += (_, _) =>
        {
            var p = w.Position;
            if (p.X != 0 || p.Y != 0) _last = p;
        };
    }

    /// <summary>The last good position, or null if the window never reported one.</summary>
    public PixelPoint? Position => _last;

    /// <summary>
    /// The position to save so that a restore lands the window where it was.
    /// </summary>
    /// <remarks>
    /// On Windows, <see cref="Window.Position"/> is the frame's corner and restoring it is exact.
    /// On X11 (GNOME over XWayland, measured 2026-09-14) it is the client area's corner, while a
    /// restore places the frame there, so every restart drifted the window down by one title bar
    /// — 37 px on Fedora — and right by the border. Subtracting the frame extents gives the frame
    /// corner the window manager will honour.
    /// </remarks>
    public PixelPoint? SavePosition(Window w)
    {
        if (_last is not { } p) return null;
        if (OperatingSystem.IsWindows() || w.FrameSize is not { } frame) return p;
        var top = (int)Math.Round(frame.Height - w.ClientSize.Height);
        var side = (int)Math.Round((frame.Width - w.ClientSize.Width) / 2);
        if (top < 0 || top > 200 || side < 0 || side > 50) return p;   // nonsense extents: leave it
        return new PixelPoint(p.X - side, p.Y - top);
    }

    /// <summary>Apply a remembered position and size before the window is shown.</summary>
    public static void Restore(Window w, double? x, double? y, double? width = null, double? height = null)
    {
        if (x is not null && y is not null && (x != 0 || y != 0))
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)x.Value, (int)y.Value);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (width is > 100 && height is > 80)
        {
            w.Width = width.Value;
            w.Height = height.Value;
        }
    }
}
