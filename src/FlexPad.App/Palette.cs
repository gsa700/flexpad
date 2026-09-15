using Avalonia.Media;

namespace FlexPad.App;

/// <summary>
/// Shared palette, matched to the PowerShell FlexPad so the two station tools look
/// like a family. XAML pulls the same colors from App.axaml resources; this is the
/// code-side mirror for custom-drawn controls (bars, peak marker) in later phases.
/// </summary>
public static class Palette
{
    public static readonly Color Bg = Color.FromRgb(0x1C, 0x1C, 0x1E);
    public static readonly Color Panel = Color.FromRgb(0x2C, 0x2C, 0x30);
    public static readonly Color Track = Color.FromRgb(0x3C, 0x3C, 0x42);
    public static readonly Color Text = Color.FromRgb(0xDC, 0xDC, 0xDC);
    public static readonly Color Amber = Color.FromRgb(0xFF, 0xB0, 0x00);
    public static readonly Color Green = Color.FromRgb(0x3C, 0xC8, 0x50);
    public static readonly Color Red = Color.FromRgb(0xEB, 0x46, 0x46);
    public static readonly Color Dim = Color.FromRgb(0x8C, 0x8C, 0x94);
    public static readonly Color Cyan = Color.FromRgb(0x00, 0xD2, 0xEB);
    public static readonly Color Blue = Color.FromRgb(0x4A, 0x90, 0xE2);   // forward-power bar fill

    public static readonly IBrush AmberBrush = new SolidColorBrush(Amber);
    public static readonly IBrush GreenBrush = new SolidColorBrush(Green);
    public static readonly IBrush RedBrush = new SolidColorBrush(Red);
    public static readonly IBrush DimBrush = new SolidColorBrush(Dim);
    public static readonly IBrush TextBrush = new SolidColorBrush(Text);
    public static readonly IBrush CyanBrush = new SolidColorBrush(Cyan);
    public static readonly IBrush PanelBrush = new SolidColorBrush(Panel);
    public static readonly IBrush TrackBrush = new SolidColorBrush(Track);
    public static readonly IBrush BlueBrush = new SolidColorBrush(Blue);
    public static readonly IBrush GoldBrush = new SolidColorBrush(Amber);  // SWR bar (same amber/gold)
}
