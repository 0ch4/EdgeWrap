using System.Drawing;

namespace EdgeWrap.Core;

/// <summary>A monitor described in virtual-desktop coordinates.</summary>
public sealed class MonitorInfo
{
    public required string Id { get; init; }        // Screen.DeviceName, e.g. \\.\DISPLAY1
    public required Rectangle Bounds { get; init; }  // virtual-desktop pixels (can be negative)
    public bool IsPrimary { get; init; }
    public int Index { get; init; }                  // 1-based, for display labels
}
