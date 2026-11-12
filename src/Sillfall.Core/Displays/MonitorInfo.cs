using Sillfall.Core.Geometry;

namespace Sillfall.Core.Displays;

/// <summary>One monitor of the virtual desktop, in physical pixels.</summary>
/// <param name="DeviceName">System name of the display device, for example <c>\\.\DISPLAY1</c>. Identifies the monitor.</param>
/// <param name="Bounds">Full monitor area.</param>
/// <param name="WorkArea">Monitor area without the taskbar and other app bars.</param>
/// <param name="Dpi">Effective DPI. 96 means 100 % scaling.</param>
/// <param name="IsPrimary">Whether this is the primary monitor.</param>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)
{
    /// <summary>DPI at 100 % scaling.</summary>
    public const int DefaultDpi = 96;

    /// <summary>Scale factor relative to 100 %, for example 1.5 at 144 DPI.</summary>
    public float ScaleFactor => Dpi / (float)DefaultDpi;
}
