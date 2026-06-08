using System.Drawing;
using System.Runtime.InteropServices;
using EdgeWrap.Core;

namespace EdgeWrap.UI;

// ============================================================================
//  Experimental "seam mirror".
//  When a window is dragged past the outer edge of a donut seam (e.g. off the
//  right edge of the rightmost monitor), the off-screen strip is mirrored onto
//  the opposite monitor using a live DWM thumbnail, so the window *appears* to
//  wrap around the seam. Visual only -- the mirrored strip is click-through and
//  cannot be interacted with. Off by default.
// ============================================================================

internal static class Dwm
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public Native.RECT rcDestination;
        public Native.RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    public const int DWM_TNP_RECTDESTINATION = 0x1;
    public const int DWM_TNP_RECTSOURCE = 0x2;
    public const int DWM_TNP_OPACITY = 0x4;
    public const int DWM_TNP_VISIBLE = 0x8;
    public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumbId);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr thumbId);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(IntPtr thumbId, ref DWM_THUMBNAIL_PROPERTIES props);
}

/// <summary>A borderless, click-through, top-most overlay that hosts a DWM thumbnail.</summary>
internal sealed class MirrorOverlay : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    private IntPtr _thumb = IntPtr.Zero;
    private IntPtr _src = IntPtr.Zero;

    public MirrorOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Visible = false;
        _ = Handle; // force creation so a thumbnail can target us
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = HTTRANSPARENT; // pass all clicks through to whatever is beneath
            return;
        }
        base.WndProc(ref m);
    }

    public void ShowMirror(IntPtr src, Native.RECT rcSource, Rectangle dest)
    {
        if (dest.Width < 1 || dest.Height < 1 || src == IntPtr.Zero)
        {
            HideMirror();
            return;
        }

        if (_src != src)
        {
            Unregister();
            if (Dwm.DwmRegisterThumbnail(Handle, src, out _thumb) != 0)
            {
                _thumb = IntPtr.Zero;
                HideMirror();
                return;
            }
            _src = src;
        }

        Bounds = dest;
        if (!Visible)
            Visible = true;

        var props = new Dwm.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = Dwm.DWM_TNP_RECTDESTINATION | Dwm.DWM_TNP_RECTSOURCE |
                      Dwm.DWM_TNP_OPACITY | Dwm.DWM_TNP_VISIBLE | Dwm.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new Native.RECT { Left = 0, Top = 0, Right = dest.Width, Bottom = dest.Height },
            rcSource = rcSource,
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = false
        };
        Dwm.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    public void HideMirror()
    {
        if (Visible)
            Visible = false;
        Unregister();
    }

    private void Unregister()
    {
        if (_thumb != IntPtr.Zero)
        {
            Dwm.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
        }
        _src = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Unregister();
        base.Dispose(disposing);
    }
}

/// <summary>Watches the foreground window and mirrors seam overflow. Runs on the UI thread.</summary>
public sealed class SeamMirrorService : IDisposable
{
    private readonly record struct Seam(Rectangle RightMon, Rectangle LeftMon);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly MirrorOverlay _overlay = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    private List<Seam> _seams = new();
    private bool _enabled;

    public SeamMirrorService()
    {
        _timer.Tick += (_, _) => Tick();
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (_enabled)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _overlay.HideMirror();
            }
        }
    }

    /// <summary>Rebuild seams from horizontal (Left&lt;-&gt;Right) links. Safe to call anytime.</summary>
    public void Configure(IEnumerable<EdgeLink> links)
    {
        var byId = MonitorService.GetMonitors().ToDictionary(m => m.Id);
        var seams = new List<Seam>();
        foreach (var l in links)
        {
            var rightEnd = SidedEnd(l, Side.Right);
            var leftEnd = SidedEnd(l, Side.Left);
            if (rightEnd is null || leftEnd is null)
                continue;
            if (byId.TryGetValue(rightEnd.Value.MonitorId, out var rm) &&
                byId.TryGetValue(leftEnd.Value.MonitorId, out var lm))
            {
                seams.Add(new Seam(rm.Bounds, lm.Bounds));
            }
        }
        _seams = seams;
    }

    private static EdgeRef? SidedEnd(EdgeLink l, Side side)
    {
        if (l.A.Side == side) return l.A;
        if (l.B.Side == side) return l.B;
        return null;
    }

    private void Tick()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
        {
            _overlay.HideMirror();
            return;
        }

        // Never mirror our own windows (overlay / settings / tray).
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid)
        {
            _overlay.HideMirror();
            return;
        }

        if (!Native.GetWindowRect(hwnd, out var wr))
        {
            _overlay.HideMirror();
            return;
        }

        foreach (var seam in _seams)
        {
            // (a) window spills off the RIGHT edge of the rightmost monitor -> show on the LEFT monitor's left edge
            if (wr.Right > seam.RightMon.Right && wr.Left < seam.RightMon.Right)
            {
                int ow = wr.Right - seam.RightMon.Right;
                int srcLeft = seam.RightMon.Right - wr.Left;
                var rcSource = new Native.RECT { Left = srcLeft, Top = 0, Right = wr.Width, Bottom = wr.Height };
                var dest = new Rectangle(seam.LeftMon.Left, wr.Top, ow, wr.Height);
                _overlay.ShowMirror(hwnd, rcSource, dest);
                return;
            }

            // (b) window spills off the LEFT edge of the leftmost monitor -> show on the RIGHT monitor's right edge
            if (wr.Left < seam.LeftMon.Left && wr.Right > seam.LeftMon.Left)
            {
                int ow = seam.LeftMon.Left - wr.Left;
                var rcSource = new Native.RECT { Left = 0, Top = 0, Right = ow, Bottom = wr.Height };
                var dest = new Rectangle(seam.RightMon.Right - ow, wr.Top, ow, wr.Height);
                _overlay.ShowMirror(hwnd, rcSource, dest);
                return;
            }
        }

        _overlay.HideMirror();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _overlay.Dispose();
    }
}
