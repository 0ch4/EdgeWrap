using System.Diagnostics;
using System.Drawing;

namespace EdgeWrap.Core;

/// <summary>
/// Polls the cursor on a background thread and teleports it across configured
/// edge links. The perpendicular position is mapped proportionally so monitors
/// of different sizes or vertical offsets line up corner-to-corner.
/// </summary>
public sealed class WrapEngine : IDisposable
{
    // One actionable end of a link: when the cursor hits Src, jump to Dst.
    private sealed record EndPlan(Rectangle SrcBounds, Side SrcSide, Rectangle DstBounds, Side DstSide);

    private sealed record Snapshot(IReadOnlyList<EndPlan> Ends, Rectangle Virtual)
    {
        public static readonly Snapshot Empty = new(Array.Empty<EndPlan>(), Rectangle.Empty);
    }

    private readonly object _gate = new();
    private Snapshot _snap = Snapshot.Empty;
    private int _margin = 3;
    private int _pollMs = 6;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Rebuild the engine's view of monitors and links. Safe to call while running.</summary>
    public void Configure(IEnumerable<EdgeLink> links, int margin, int pollMs)
    {
        var monitors = MonitorService.GetMonitors();
        var vb = MonitorService.VirtualBounds(monitors);
        var byId = monitors.ToDictionary(m => m.Id);

        var ends = new List<EndPlan>();
        foreach (var link in links)
        {
            AddEnd(ends, byId, link.A, link.B);
            AddEnd(ends, byId, link.B, link.A);
        }

        lock (_gate)
        {
            _snap = new Snapshot(ends, vb);
            _margin = Math.Clamp(margin, 1, 50);
            _pollMs = Math.Clamp(pollMs, 2, 50);
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "EdgeWrap.Poll" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(250);
        _thread = null;
    }

    public void Dispose() => Stop();

    private static void AddEnd(List<EndPlan> ends, IReadOnlyDictionary<string, MonitorInfo> byId, EdgeRef src, EdgeRef dst)
    {
        if (byId.TryGetValue(src.MonitorId, out var sm) && byId.TryGetValue(dst.MonitorId, out var dm))
            ends.Add(new EndPlan(sm.Bounds, src.Side, dm.Bounds, dst.Side));
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        long cooldownUntil = 0;

        while (_running)
        {
            int pollMs;
            try
            {
                Snapshot snap;
                int margin;
                lock (_gate)
                {
                    snap = _snap;
                    margin = _margin;
                    pollMs = _pollMs;
                }

                if (_enabled && snap.Ends.Count > 0 &&
                    clock.ElapsedMilliseconds >= cooldownUntil &&
                    Native.GetCursorPos(out var p) &&
                    TryWrap(snap, p.X, p.Y, margin))
                {
                    // Brief cooldown so the teleport doesn't immediately re-trigger.
                    cooldownUntil = clock.ElapsedMilliseconds + 80;
                }
            }
            catch
            {
                // Ignore transient failures (e.g. display change mid-poll); keep running.
                pollMs = 6;
            }

            Thread.Sleep(pollMs);
        }
    }

    private static bool TryWrap(Snapshot snap, int x, int y, int margin)
    {
        foreach (var e in snap.Ends)
        {
            if (!IsAtOuterEdge(e.SrcBounds, e.SrcSide, snap.Virtual, x, y))
                continue;

            var (dx, dy) = MapToDestination(e, x, y, margin);
            Native.SetCursorPos(dx, dy);
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when the cursor is pressed against this edge AND the edge is on the
    /// outer boundary of the virtual desktop (so it faces empty space, not a neighbor).
    /// </summary>
    private static bool IsAtOuterEdge(Rectangle m, Side side, Rectangle vb, int x, int y)
    {
        return side switch
        {
            Side.Left => m.Left <= vb.Left && x <= m.Left && y >= m.Top && y < m.Bottom,
            Side.Right => m.Right >= vb.Right && x >= m.Right - 1 && y >= m.Top && y < m.Bottom,
            Side.Top => m.Top <= vb.Top && y <= m.Top && x >= m.Left && x < m.Right,
            Side.Bottom => m.Bottom >= vb.Bottom && y >= m.Bottom - 1 && x >= m.Left && x < m.Right,
            _ => false
        };
    }

    private static (int x, int y) MapToDestination(EndPlan e, int x, int y, int margin)
    {
        // Position along the SOURCE edge, as a 0..1 ratio.
        bool srcVertical = e.SrcSide.IsVertical();
        double srcStart = srcVertical ? e.SrcBounds.Top : e.SrcBounds.Left;
        double srcLen = srcVertical ? e.SrcBounds.Height : e.SrcBounds.Width;
        double pos = srcVertical ? y : x;
        double ratio = srcLen > 0 ? (pos - srcStart) / srcLen : 0;
        ratio = Math.Clamp(ratio, 0.0, 1.0);

        // Map that ratio onto the DESTINATION edge's parallel axis.
        var d = e.DstBounds;
        bool dstVertical = e.DstSide.IsVertical();
        double dstStart = dstVertical ? d.Top : d.Left;
        double dstLen = dstVertical ? d.Height : d.Width;
        int parallel = (int)Math.Round(dstStart + ratio * (dstLen - 1));

        return e.DstSide switch
        {
            Side.Left => (d.Left + margin, Clamp(parallel, d.Top, d.Bottom - 1)),
            Side.Right => (d.Right - 1 - margin, Clamp(parallel, d.Top, d.Bottom - 1)),
            Side.Top => (Clamp(parallel, d.Left, d.Right - 1), d.Top + margin),
            _ => (Clamp(parallel, d.Left, d.Right - 1), d.Bottom - 1 - margin)
        };
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
