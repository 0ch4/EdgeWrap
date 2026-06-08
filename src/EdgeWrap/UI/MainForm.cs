using System.Drawing;
using EdgeWrap.Config;
using EdgeWrap.Core;
using Microsoft.Win32;

namespace EdgeWrap.UI;

public sealed class MainForm : Form
{
    private readonly MonitorMapControl _map = new();
    private readonly ListBox _list = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnApply = new();
    private readonly Button _btnClose = new();
    private readonly CheckBox _chkEnabled = new();
    private readonly CheckBox _chkAutoStart = new();
    private readonly CheckBox _chkSeamMirror = new();

    private List<MonitorInfo> _monitors = MonitorService.GetMonitors();
    private List<EdgeLink> _links;

    /// <summary>Raised when the user clicks 適用 (Apply). Carries the new config.</summary>
    public event Action<AppConfig>? ConfigApplied;

    public MainForm(AppConfig config)
    {
        _links = config.Links.Select(l => new EdgeLink(l.A, l.B)).ToList();

        Text = "EdgeWrap — 設定";
        ClientSize = new Size(860, 588);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x2A, 0x2F, 0x36);
        ForeColor = Color.FromArgb(0xE6, 0xEA, 0xEF);
        Font = new Font("Segoe UI", 9f);

        BuildLayout();

        _chkEnabled.Checked = config.Enabled;
        _chkAutoStart.Checked = config.AutoStart;
        _chkSeamMirror.Checked = config.ExperimentalSeamMirror;

        _map.LinkCreated += OnLinkCreated;
        RefreshLinks();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        FormClosed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
    }

    private void BuildLayout()
    {
        _map.SetBounds(12, 12, 560, 564);

        var lblList = new Label
        {
            Text = "連結リスト",
            Bounds = new Rectangle(584, 12, 264, 18),
            ForeColor = Color.FromArgb(0xB8, 0xC0, 0xCC)
        };

        _list.SetBounds(584, 34, 264, 322);
        _list.BackColor = Color.FromArgb(0x21, 0x25, 0x2B);
        _list.ForeColor = ForeColor;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.IntegralHeight = false;

        _btnRemove.Text = "選択した連結を削除";
        _btnRemove.SetBounds(584, 362, 264, 28);
        StyleButton(_btnRemove);
        _btnRemove.Click += (_, _) => RemoveSelected();

        _chkEnabled.Text = "有効（ワープを動作させる）";
        _chkEnabled.SetBounds(584, 400, 264, 22);
        _chkEnabled.ForeColor = ForeColor;

        _chkAutoStart.Text = "Windows起動時に自動で開始";
        _chkAutoStart.SetBounds(584, 424, 264, 22);
        _chkAutoStart.ForeColor = ForeColor;

        _chkSeamMirror.Text = "実験: ウィンドウも継ぎ目をまたぐ(視覚のみ)";
        _chkSeamMirror.SetBounds(584, 448, 264, 22);
        _chkSeamMirror.ForeColor = Color.FromArgb(0xFF, 0xCA, 0x28);

        var lblHint = new Label
        {
            Bounds = new Rectangle(584, 476, 264, 64),
            ForeColor = Color.FromArgb(0x8A, 0x93, 0x9F),
            Text = "「適用」で保存・即反映します。\n" +
                   "設定ファイル:\n" + ConfigStore.Location
        };

        _btnApply.Text = "適用";
        _btnApply.SetBounds(584, 546, 128, 32);
        StyleButton(_btnApply, accent: true);
        _btnApply.Click += (_, _) => ApplyConfig();

        _btnClose.Text = "閉じる";
        _btnClose.SetBounds(720, 546, 128, 32);
        StyleButton(_btnClose);
        _btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            _map, lblList, _list, _btnRemove,
            _chkEnabled, _chkAutoStart, _chkSeamMirror, lblHint, _btnApply, _btnClose
        });
    }

    private void StyleButton(Button b, bool accent = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = accent ? Color.FromArgb(0x2F, 0x6F, 0xED) : Color.FromArgb(0x3A, 0x41, 0x4B);
        b.ForeColor = Color.White;
        b.Cursor = Cursors.Hand;
    }

    private void OnLinkCreated(EdgeLink link)
    {
        // Avoid exact duplicates (same unordered pair).
        bool exists = _links.Any(l =>
            (l.A.Equals(link.A) && l.B.Equals(link.B)) ||
            (l.A.Equals(link.B) && l.B.Equals(link.A)));
        if (!exists)
            _links.Add(link);
        RefreshLinks();
    }

    private void RemoveSelected()
    {
        int i = _list.SelectedIndex;
        if (i >= 0 && i < _links.Count)
        {
            _links.RemoveAt(i);
            RefreshLinks();
        }
    }

    private void RefreshLinks()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var l in _links)
            _list.Items.Add($"{FormatEdge(l.A)}　⇄　{FormatEdge(l.B)}");
        _list.EndUpdate();
        _map.SetLinks(_links);
    }

    private string FormatEdge(EdgeRef e)
    {
        var m = _monitors.FirstOrDefault(x => x.Id == e.MonitorId);
        string name = m != null ? $"Mon{m.Index}" : "Mon?";
        return $"{name} {SideJp(e.Side)}";
    }

    private static string SideJp(Side s) => s switch
    {
        Side.Left => "左端",
        Side.Right => "右端",
        Side.Top => "上端",
        Side.Bottom => "下端",
        _ => ""
    };

    private void ApplyConfig()
    {
        var cfg = new AppConfig
        {
            Links = _links.Select(l => new EdgeLink(l.A, l.B)).ToList(),
            Enabled = _chkEnabled.Checked,
            AutoStart = _chkAutoStart.Checked,
            ExperimentalSeamMirror = _chkSeamMirror.Checked
        };
        ConfigApplied?.Invoke(cfg);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        _monitors = MonitorService.GetMonitors();
        _map.ReloadMonitors();
        RefreshLinks();
    }

    // Called by the tray when these are toggled from the context menu.
    public void SetEnabledState(bool enabled) => _chkEnabled.Checked = enabled;
    public void SetAutoStartState(bool enabled) => _chkAutoStart.Checked = enabled;
    public void SetSeamMirrorState(bool enabled) => _chkSeamMirror.Checked = enabled;
}
