using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace PcAgent;

/// <summary>
/// 主窗口：状态区（设备 / 焦点）+ 配对区（IP 选择 / 二维码 / 配对串）
/// + 设置区（映射开关 / 自启 / 最小化到托盘）+ 日志区。关闭按钮 = 最小化到托盘。
/// </summary>
internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly WsServer _server;
    private readonly FocusWatcher _watcher;

    private readonly ComboBox _ipCombo = new();
    private readonly TextBox _pairingText = new();
    private readonly PictureBox _qrBox = new();
    private readonly CheckBox _ckEnabled = new();
    private readonly CheckBox _ckAutoStart = new();
    private readonly CheckBox _ckMinimize = new();
    private readonly Label _lblDevice = new();
    private readonly Label _lblFocus = new();
    private readonly TextBox _logBox = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly Button _btnHide = new();

    private bool _bubbleShown;

    public MainForm(AppSettings settings, WsServer server, FocusWatcher watcher)
    {
        _settings = settings;
        _server = server;
        _watcher = watcher;

        Text = "PC 输入桥接";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 780);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Icon = App.MakeIcon();

        // ---- 状态区 ----
        var gpStatus = new GroupBox { Text = "状态", Left = 12, Top = 10, Width = 496, Height = 78 };
        _lblDevice.Left = 14; _lblDevice.Top = 24; _lblDevice.Width = 460;
        _lblDevice.Text = "设备：未连接";
        _lblFocus.Left = 14; _lblFocus.Top = 48; _lblFocus.Width = 460;
        _lblFocus.Text = "焦点：检测中…";
        gpStatus.Controls.Add(_lblDevice);
        gpStatus.Controls.Add(_lblFocus);

        // ---- 配对区 ----
        var gpPair = new GroupBox { Text = "手机配对（手机与电脑需在同一局域网）", Left = 12, Top = 96, Width = 496, Height = 396 };
        var lblIp = new Label { Text = "网络：", Left = 14, Top = 26, AutoSize = true };
        _ipCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _ipCombo.Left = 66; _ipCombo.Top = 22; _ipCombo.Width = 410;
        foreach (var ip in GetLocalIps()) _ipCombo.Items.Add(ip);
        if (_ipCombo.Items.Count > 0) _ipCombo.SelectedIndex = 0;

        _qrBox.Left = 140; _qrBox.Top = 54; _qrBox.Width = 220; _qrBox.Height = 220;
        _qrBox.SizeMode = PictureBoxSizeMode.Zoom;

        _pairingText.Left = 14; _pairingText.Top = 288; _pairingText.Width = 460; _pairingText.ReadOnly = true;
        var btnCopy = new Button { Text = "复制配对串", Left = 14, Top = 314, Width = 120 };
        btnCopy.Click += (s, e) =>
        {
            try { Clipboard.SetText(_pairingText.Text); btnCopy.Text = "已复制"; } catch { }
        };
        var lblTip = new Label
        {
            Text = "手机 App「设置」→ 粘贴配对串（或扫二维码）→ 保存并连接",
            Left = 14, Top = 346, Width = 460, AutoSize = true,
            ForeColor = Color.DimGray
        };
        var lblPort = new Label
        {
            Text = $"服务端口：{_settings.Port}（可在配置文件修改）",
            Left = 14, Top = 366, Width = 460, AutoSize = true,
            ForeColor = Color.DimGray
        };
        gpPair.Controls.AddRange(new Control[] { lblIp, _ipCombo, _qrBox, _pairingText, btnCopy, lblTip, lblPort });

        // ---- 设置区 ----
        var gpSet = new GroupBox { Text = "设置", Left = 12, Top = 500, Width = 496, Height = 110 };
        _ckEnabled.Text = "启用输入映射（关闭后手机发送将被拒绝）";
        _ckEnabled.Checked = _settings.MappingEnabled;
        _ckEnabled.Left = 14; _ckEnabled.Top = 24; _ckEnabled.Width = 460; _ckEnabled.AutoSize = true;
        _ckEnabled.CheckedChanged += (s, e) =>
        {
            _settings.MappingEnabled = _ckEnabled.Checked;
            _server.MappingEnabled = _ckEnabled.Checked;
            _settings.Save();
        };

        _ckAutoStart.Text = "开机自动启动";
        _ckAutoStart.Checked = AutoStartHelper.IsSet();
        _ckAutoStart.Left = 14; _ckAutoStart.Top = 50; _ckAutoStart.Width = 460; _ckAutoStart.AutoSize = true;
        _ckAutoStart.CheckedChanged += (s, e) =>
        {
            AutoStartHelper.Set(_ckAutoStart.Checked);
            _settings.AutoStart = _ckAutoStart.Checked;
            _settings.Save();
        };

        _ckMinimize.Text = "关闭窗口时最小化到托盘（不退出）";
        _ckMinimize.Checked = true;
        _ckMinimize.Left = 14; _ckMinimize.Top = 76; _ckMinimize.Width = 460; _ckMinimize.AutoSize = true;
        gpSet.Controls.Add(_ckEnabled);
        gpSet.Controls.Add(_ckAutoStart);
        gpSet.Controls.Add(_ckMinimize);

        // ---- 日志区 ----
        var gpLog = new GroupBox { Text = "运行日志", Left = 12, Top = 618, Width = 496, Height = 108 };
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Left = 14; _logBox.Top = 20; _logBox.Width = 466; _logBox.Height = 80;
        _logBox.Font = new Font("Consolas", 9f);
        gpLog.Controls.Add(_logBox);

        _btnHide.Text = "最小化到托盘";
        _btnHide.Left = 388; _btnHide.Top = 734; _btnHide.Width = 120;
        _btnHide.Click += (s, e) => Hide();

        Controls.AddRange(new Control[] { gpStatus, gpPair, gpSet, gpLog, _btnHide });

        // ---- 事件与定时刷新 ----
        _ipCombo.SelectedIndexChanged += (s, e) => RefreshPairing();
        _server.ClientChanged += OnClientChanged;
        _server.Log += OnServerLog;
        _uiTimer.Interval = 1000;
        _uiTimer.Tick += (s, e) => RefreshStatus();
        _uiTimer.Start();

        RefreshPairing();
        RefreshStatus();
        AppendLog("程序已启动。首次使用请在防火墙中放行端口 " + _settings.Port + "。");

        FormClosed += (s, e) =>
        {
            _server.ClientChanged -= OnClientChanged;
            _server.Log -= OnServerLog;
            _uiTimer.Stop();
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason == CloseReason.UserClosing && _ckMinimize.Checked)
        {
            e.Cancel = true;
            Hide();
            if (!_bubbleShown)
            {
                _bubbleShown = true;
                App.ShowTrayBubble("程序已最小化到托盘，右键托盘图标可退出。");
            }
        }
    }

    private void OnClientChanged()
    {
        if (IsHandleCreated) BeginInvoke(RefreshStatus);
    }

    private void OnServerLog(string msg)
    {
        if (IsHandleCreated) BeginInvoke(() => AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}"));
    }

    public void AppendLog(string line)
    {
        if (_logBox.TextLength > 60_000) _logBox.Clear();
        _logBox.AppendText(line + Environment.NewLine);
    }

    private void RefreshStatus()
    {
        var d = _server.CurrentDevice;
        _lblDevice.Text = d == null ? "设备：未连接" : $"设备：{d}";
        _lblDevice.ForeColor = d == null ? Color.DimGray : Color.FromArgb(46, 125, 50);
        var f = _watcher.QueryNow();
        _lblFocus.Text = f.Ready
            ? $"焦点：{f.App}{(f.Field.Length > 0 ? " · " + f.Field : "")}（可接收输入）"
            : "焦点：当前无文本输入框";
        _lblFocus.ForeColor = f.Ready ? Color.FromArgb(46, 125, 50) : Color.DimGray;
    }

    private void RefreshPairing()
    {
        var ip = _ipCombo.SelectedItem as string ?? "127.0.0.1";
        var s = $"ws://{ip}:{_settings.Port}/?token={_settings.Token}";
        _pairingText.Text = s;
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(s, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            using var ms = new MemoryStream(png.GetGraphic(6));
            var old = _qrBox.Image;
            _qrBox.Image = new Bitmap(ms);
            old?.Dispose();
        }
        catch { }
    }

    private static IEnumerable<string> GetLocalIps()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString());
    }
}
