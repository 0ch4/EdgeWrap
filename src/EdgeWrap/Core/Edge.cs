namespace EdgeWrap.Core;

/// <summary>Which side of a monitor an edge sits on.</summary>
public enum Side
{
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>A reference to one edge of one monitor.</summary>
public readonly record struct EdgeRef(string MonitorId, Side Side);

public static class SideExtensions
{
    /// <summary>True for Left/Right edges, whose parallel axis is vertical (Y).</summary>
    public static bool IsVertical(this Side side) => side is Side.Left or Side.Right;
}
