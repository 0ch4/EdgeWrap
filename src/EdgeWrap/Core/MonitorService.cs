using System.Drawing;

namespace EdgeWrap.Core;

public static class MonitorService
{
    /// <summary>Enumerate the current monitors in virtual-desktop coordinates.</summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        int i = 1;
        foreach (var s in Screen.AllScreens)
        {
            list.Add(new MonitorInfo
            {
                Id = s.DeviceName,
                Bounds = s.Bounds,
                IsPrimary = s.Primary,
                Index = i++
            });
        }
        return list;
    }

    /// <summary>The bounding rectangle of every monitor (the whole virtual desktop).</summary>
    public static Rectangle VirtualBounds(IEnumerable<MonitorInfo> monitors)
    {
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        bool any = false;
        foreach (var m in monitors)
        {
            any = true;
            l = Math.Min(l, m.Bounds.Left);
            t = Math.Min(t, m.Bounds.Top);
            r = Math.Max(r, m.Bounds.Right);
            b = Math.Max(b, m.Bounds.Bottom);
        }
        return any ? Rectangle.FromLTRB(l, t, r, b) : Rectangle.Empty;
    }
}
