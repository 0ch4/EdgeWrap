using System.Drawing;
using System.Drawing.Drawing2D;
using EdgeWrap.Core;

namespace EdgeWrap.UI;

/// <summary>
/// Draws the monitors to scale and lets the user click two edges to create a link.
/// Click one edge (it highlights), then click another to connect them.
/// </summary>
public sealed class MonitorMapControl : Control
{
    private const int Pad = 28;
    private const float EdgeHitDistance = 12f;

    private static readonly Color[] LinkColors =
    {
        Color.FromArgb(0xE5, 0x39, 0x35), Color.FromArgb(0x1E, 0x88, 0xE5),
        Color.FromArgb(0x43, 0xA0, 0x47), Color.FromArgb(0xFB, 0x8C, 0x00),
        Color.FromArgb(0x8E, 0x24, 0xAA), Color.FromArgb(0x00, 0xAC, 0xC1),
    };

    private List<MonitorInfo> _monitors = new();
    private Rectangle _virtual;
    private List<EdgeLink> _links = new();

    private EdgeRef? _pending;
    private EdgeRef? _hover;

    private float _scale = 1f;
    private float _offsetX, _offsetY;

    /// <summary>Raised when the user completes a pair of edges.</summary>
    public event Action<EdgeLink>? LinkCreated;

    public MonitorMapControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.FromArgb(0x21, 0x25, 0x2B);
        ReloadMonitors();
    }

    public void SetLinks(List<EdgeLink> links)
    {
        _links = links;
        Invalidate();
    }

    public void ReloadMonitors()
    {
        _monitors = MonitorService.GetMonitors();
        _virtual = MonitorService.VirtualBounds(_monitors);
        _pending = null;
        Invalidate();
    }

    public void ClearPending()
    {
        if (_pending != null)
        {
            _pending = null;
            Invalidate();
        }
    }

    // ----- coordinate transform (virtual desktop -> control) -----

    private void ComputeTransform()
    {
        var area = new RectangleF(Pad, Pad, Math.Max(1, Width - 2 * Pad), Math.Max(1, Height - 2 * Pad));
        if (_virtual.Width <= 0 || _virtual.Height <= 0)
        {
            _scale = 1f;
            _offsetX = _offsetY = 0f;
            return;
        }
        _scale = Math.Min(area.Width / _virtual.Width, area.Height / _virtual.Height);
        float drawW = _virtual.Width * _scale;
        float drawH = _virtual.Height * _scale;
        _offsetX = area.Left + (area.Width - drawW) / 2f;
        _offsetY = area.Top + (area.Height - drawH) / 2f;
    }

    private RectangleF ToScreen(Rectangle r) => new(
        _offsetX + (r.Left - _virtual.Left) * _scale,
        _offsetY + (r.Top - _virtual.Top) * _scale,
        r.Width * _scale,
        r.Height * _scale);

    private static (PointF a, PointF b) EdgeSegment(RectangleF rr, Side side) => side switch
    {
        Side.Left => (new PointF(rr.Left, rr.Top), new PointF(rr.Left, rr.Bottom)),
        Side.Right => (new PointF(rr.Right, rr.Top), new PointF(rr.Right, rr.Bottom)),
        Side.Top => (new PointF(rr.Left, rr.Top), new PointF(rr.Right, rr.Top)),
        _ => (new PointF(rr.Left, rr.Bottom), new PointF(rr.Right, rr.Bottom)),
    };

    private static PointF EdgeMidpoint(RectangleF rr, Side side) => side switch
    {
        Side.Left => new PointF(rr.Left, (rr.Top + rr.Bottom) / 2f),
        Side.Right => new PointF(rr.Right, (rr.Top + rr.Bottom) / 2f),
        Side.Top => new PointF((rr.Left + rr.Right) / 2f, rr.Top),
        _ => new PointF((rr.Left + rr.Right) / 2f, rr.Bottom),
    };

    private static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq <= 0.0001f)
            return Distance(p, a);
        float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0f, 1f);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    private static float Distance(PointF p, PointF q)
    {
        float dx = p.X - q.X, dy = p.Y - q.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private EdgeRef? HitTest(Point p)
    {
        ComputeTransform();
        EdgeRef? best = null;
        float bestDist = EdgeHitDistance;
        foreach (var m in _monitors)
        {
            var rr = ToScreen(m.Bounds);
            foreach (Side side in Enum.GetValues<Side>())
            {
                var (a, b) = EdgeSegment(rr, side);
                float d = DistanceToSegment(p, a, b);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = new EdgeRef(m.Id, side);
                }
            }
        }
        return best;
    }

    // ----- input -----

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (!Nullable.Equals(hit, _hover))
        {
            _hover = hit;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null)
        {
            _hover = null;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
            return;

        var hit = HitTest(e.Location);
        if (hit == null)
        {
            ClearPending();
            return;
        }

        if (_pending == null)
        {
            _pending = hit;
        }
        else if (_pending.Equals(hit))
        {
            _pending = null; // clicked the same edge again -> deselect
        }
        else
        {
            LinkCreated?.Invoke(new EdgeLink(_pending.Value, hit.Value));
            _pending = null;
        }
        Invalidate();
    }

    // ----- painting -----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        ComputeTransform();

        if (_monitors.Count == 0)
            return;

        DrawMonitors(g);
        DrawEdges(g);
        DrawLinks(g);
        DrawHint(g);
    }

    private void DrawMonitors(Graphics g)
    {
        using var fill = new SolidBrush(Color.FromArgb(0x2E, 0x34, 0x3C));
        using var border = new Pen(Color.FromArgb(0x55, 0x5E, 0x6B), 2f);
        using var primaryBorder = new Pen(Color.FromArgb(0x6C, 0xB6, 0xFF), 2f);
        using var textBrush = new SolidBrush(Color.FromArgb(0xCF, 0xD6, 0xDF));
        using var subBrush = new SolidBrush(Color.FromArgb(0x8A, 0x93, 0x9F));
        using var titleFont = new Font(Font.FontFamily, 11f, FontStyle.Bold);

        foreach (var m in _monitors)
        {
            var rr = ToScreen(m.Bounds);
            g.FillRectangle(fill, rr);
            g.DrawRectangle(m.IsPrimary ? primaryBorder : border, rr.X, rr.Y, rr.Width, rr.Height);

            string title = $"Mon{m.Index}" + (m.IsPrimary ? "  ★" : "");
            string sub = $"{m.Bounds.Width}×{m.Bounds.Height}";
            var titleSize = g.MeasureString(title, titleFont);
            float cx = rr.X + (rr.Width - titleSize.Width) / 2f;
            float cy = rr.Y + rr.Height / 2f - titleSize.Height;
            g.DrawString(title, titleFont, textBrush, cx, cy);
            var subSize = g.MeasureString(sub, Font);
            g.DrawString(sub, Font, subBrush, rr.X + (rr.Width - subSize.Width) / 2f, cy + titleSize.Height + 2f);
        }
    }

    private void DrawEdges(Graphics g)
    {
        using var hoverPen = new Pen(Color.FromArgb(0x6C, 0xB6, 0xFF), 5f);
        using var pendingPen = new Pen(Color.FromArgb(0xFF, 0xCA, 0x28), 6f);

        foreach (var m in _monitors)
        {
            var rr = ToScreen(m.Bounds);
            foreach (Side side in Enum.GetValues<Side>())
            {
                var er = new EdgeRef(m.Id, side);
                bool isPending = _pending.HasValue && _pending.Value.Equals(er);
                bool isHover = _hover.HasValue && _hover.Value.Equals(er);
                if (!isPending && !isHover)
                    continue;

                var (a, b) = EdgeSegment(rr, side);
                g.DrawLine(isPending ? pendingPen : hoverPen, a, b);
            }
        }
    }

    private void DrawLinks(Graphics g)
    {
        var byId = _monitors.ToDictionary(m => m.Id);
        int ci = 0;
        foreach (var link in _links)
        {
            var color = LinkColors[ci % LinkColors.Length];
            ci++;
            if (!byId.TryGetValue(link.A.MonitorId, out var ma) || !byId.TryGetValue(link.B.MonitorId, out var mb))
                continue;

            var pa = EdgeMidpoint(ToScreen(ma.Bounds), link.A.Side);
            var pb = EdgeMidpoint(ToScreen(mb.Bounds), link.B.Side);

            using var pen = new Pen(color, 2.5f) { DashStyle = DashStyle.Dash };
            pen.CustomEndCap = new AdjustableArrowCap(5, 5);
            pen.CustomStartCap = new AdjustableArrowCap(5, 5);
            g.DrawLine(pen, pa, pb);

            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, pa.X - 4, pa.Y - 4, 8, 8);
            g.FillEllipse(dot, pb.X - 4, pb.Y - 4, 8, 8);
        }
    }

    private void DrawHint(Graphics g)
    {
        string hint = _pending != null
            ? "結びたいもう一方の辺をクリック（同じ辺をもう一度クリックで取り消し）"
            : "辺をクリック → もう一方の辺をクリックで連結";
        using var brush = new SolidBrush(Color.FromArgb(0x8A, 0x93, 0x9F));
        g.DrawString(hint, Font, brush, Pad, Height - Pad + 6);
    }
}
