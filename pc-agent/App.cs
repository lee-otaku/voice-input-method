using System.Drawing;
using System.Drawing.Drawing2D;

namespace PcAgent;

/// <summary>托盘应用主体：装配 服务 / 焦点检测 / 托盘菜单 / 配对窗口</summary>
internal sealed class App : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly WsServer _server;
    private readonly FocusWatcher _watcher = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _miStatus;
    private readonly ToolStripMenuItem _miEnabled;
    private readonly ToolStripMenuItem _miAutoStart;
    private SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    private PairingForm? _pairing;

    public App()
    {
        _settings = AppSettings.Load();

        _server = new WsServer
        {
            Port = _settings.Port,
            Token = _settings.Token,
            MappingEnabled = _settings.MappingEnabled,
            QueryFocus = _watcher.QueryNow,
        };

        // ---- 托盘 UI（先创建控件，WindowsFormsSynchronizationContext 随首个控件安装）----
        _miStatus = new ToolStripMenuItem("状态：未连接设备") { Enabled = false };
        _miEnabled = new ToolStripMenuItem("启用输入映射") { Checked = _settings.MappingEnabled };
        _miEnabled.Click += (s, e) =>
        {
            _miEnabled.Checked = !_miEnabled.Checked;
            _settings.MappingEnabled = _miEnabled.Checked;
            _server.MappingEnabled = _miEnabled.Checked;
            _settings.Save();
        };
        _miAutoStart = new ToolStripMenuItem("开机自启") { Checked = AutoStartHelper.IsSet() };
        _miAutoStart.Click += (s, e) =>
        {
            _miAutoStart.Checked = !_miAutoStart.Checked;
            AutoStartHelper.Set(_miAutoStart.Checked);
            _settings.AutoStart = _miAutoStart.Checked;
            _settings.Save();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_miStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miEnabled);
        menu.Items.Add("显示配对信息…", null, (s, e) => ShowPairingDialog());
        menu.Items.Add("断开设备", null, (s, e) => _server.DisconnectClient());
        menu.Items.Add(_miAutoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "PC 输入桥接 - 未连接",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (s, e) => ShowPairingDialog();

        // ---- 事件接线（此时 WinForms 同步上下文已安装，Post 会回到 UI 线程）----
        _ui = SynchronizationContext.Current ?? _ui;
        _server.Log += msg => _ui.Post(_ => _pairing?.AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}"), null);
        _server.ClientChanged += () => _ui.Post(_ => UpdateTrayStatus(), null);
        _watcher.Changed += fs =>
        {
            _ = _server.PushFocusAsync(fs);
            _ui.Post(_ => UpdateTrayStatus(), null);
        };

        _watcher.Start();
        _server.Start();
    }

    private void ShowPairingDialog()
    {
        if (_pairing != null) { _pairing.Activate(); return; }
        _pairing = new PairingForm(_settings, _server);
        _pairing.FormClosed += (s, e) => _pairing = null;
        _pairing.Show();
    }

    private void UpdateTrayStatus()
    {
        var device = _server.CurrentDevice;
        _miStatus.Text = device == null ? "状态：未连接设备" : $"状态：已连接 {device}";
        _tray.Text = device == null ? "PC 输入桥接 - 未连接" : $"PC 输入桥接 - {device}";
    }

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(40, 108, 255));
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString("桥", font);
            g.DrawString("桥", font, Brushes.White, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _pairing?.Close();
        _watcher.Dispose();
        _server.Dispose();
        Application.Exit();
    }
}
