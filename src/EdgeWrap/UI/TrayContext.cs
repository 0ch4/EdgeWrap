using System.Drawing;
using EdgeWrap.Config;
using EdgeWrap.Core;
using Microsoft.Win32;

namespace EdgeWrap.UI;

public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly WrapEngine _engine = new();
    private readonly SeamMirrorService _mirror = new();
    private readonly Control _marshal; // hidden control to marshal cross-thread calls onto the UI thread

    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _mirrorItem;

    private AppConfig _config;
    private MainForm? _form;

    public TrayContext(bool startSilent)
    {
        _config = ConfigStore.Load();
        _config.AutoStart = AutoStart.IsEnabled(); // trust the registry as the source of truth

        _marshal = new Control();
        _ = _marshal.Handle; // force handle creation on the UI thread

        _enabledItem = new ToolStripMenuItem("有効", null, (_, _) => ToggleEnabled()) { Checked = _config.Enabled };
        _autoStartItem = new ToolStripMenuItem("Windows起動時に開始", null, (_, _) => ToggleAutoStart()) { Checked = _config.AutoStart };
        _mirrorItem = new ToolStripMenuItem("実験: ウィンドウも継ぎ目をまたぐ(視覚)", null, (_, _) => ToggleSeamMirror()) { Checked = _config.ExperimentalSeamMirror };

        var settingsItem = new ToolStripMenuItem("設定…", null, (_, _) => ShowSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);

        var menu = new ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(_mirrorItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => ExitApp()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "EdgeWrap",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        ApplyConfigToEngine();
        _engine.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (startSilent)
            _icon.ShowBalloonTip(2000, "EdgeWrap", "稼働中（タスクトレイに常駐）", ToolTipIcon.Info);
        else
            ShowSettings();
    }

    /// <summary>Thread-safe entry point used by Program when a second launch occurs.</summary>
    public void RequestShowSettings()
    {
        try
        {
            if (_marshal.IsHandleCreated)
                _marshal.BeginInvoke(new Action(ShowSettings));
        }
        catch
        {
            // window is tearing down; ignore
        }
    }

    private void ShowSettings()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(_config);
            _form.ConfigApplied += OnConfigApplied;
            _form.FormClosed += (_, _) => _form = null;
            _form.Show();
        }

        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
        _form.BringToFront();
    }

    private void OnConfigApplied(AppConfig updated)
    {
        _config = updated;
        ConfigStore.Save(_config);
        AutoStart.Apply(_config.AutoStart);
        _config.AutoStart = AutoStart.IsEnabled();

        _enabledItem.Checked = _config.Enabled;
        _autoStartItem.Checked = _config.AutoStart;
        _mirrorItem.Checked = _config.ExperimentalSeamMirror;
        ApplyConfigToEngine();
    }

    private void ApplyConfigToEngine()
    {
        _engine.Enabled = _config.Enabled;
        _engine.Configure(_config.Links, _config.Margin, _config.PollIntervalMs);
        _mirror.Configure(_config.Links);
        _mirror.Enabled = _config.ExperimentalSeamMirror;
    }

    private void ToggleEnabled()
    {
        _config.Enabled = !_config.Enabled;
        _enabledItem.Checked = _config.Enabled;
        _engine.Enabled = _config.Enabled;
        ConfigStore.Save(_config);
        _form?.SetEnabledState(_config.Enabled);
    }

    private void ToggleAutoStart()
    {
        AutoStart.Apply(!_config.AutoStart);
        _config.AutoStart = AutoStart.IsEnabled();
        _autoStartItem.Checked = _config.AutoStart;
        ConfigStore.Save(_config);
        _form?.SetAutoStartState(_config.AutoStart);
    }

    private void ToggleSeamMirror()
    {
        _config.ExperimentalSeamMirror = !_config.ExperimentalSeamMirror;
        _mirrorItem.Checked = _config.ExperimentalSeamMirror;
        if (_config.ExperimentalSeamMirror)
            _mirror.Configure(_config.Links);
        _mirror.Enabled = _config.ExperimentalSeamMirror;
        ConfigStore.Save(_config);
        _form?.SetSeamMirrorState(_config.ExperimentalSeamMirror);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => ApplyConfigToEngine();

    private void ExitApp()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _engine.Dispose();
        _mirror.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _marshal.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Dispose();
            _mirror.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
