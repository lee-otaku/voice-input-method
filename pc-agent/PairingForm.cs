using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace PcAgent;

/// <summary>配对窗口：展示 本机 IP / 配对串 / 二维码 / 设备状态 / 运行日志</summary>
internal sealed class PairingForm : Form
{
    private readonly AppSettings _settings;
    private readonly WsServer _server;
    private readonly ComboBox _ipCombo = new();
    private readonly TextBox _pairingText = new();
    private readonly PictureBox _qrBox = new();
    private readonly Label _deviceLabel = new();
    private readonly ListBox _logBox = new();

    public PairingForm(AppSettings settings, WsServer server)
    {
        _settings = settings;
        _server = server;

        Text = "配对信息 - PC 输入桥接";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 660);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var lbl1 = new Label { Text = "网络接口：", Left = 16, Top = 18, AutoSize = true };
        _ipCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _ipCombo.Left = 100; _ipCombo.Top = 14; _ipCombo.Width = 330;
        foreach (var ip in GetLocalIps()) _ipCombo.Items.Add(ip);
        if (_ipCombo.Items.Count > 0) _ipCombo.SelectedIndex = 0;

        var lbl2 = new Label { Text = "配对串（复制后在手机 App「设置」里粘贴解析）：", Left = 16, Top = 52, AutoSize = true };
        _pairingText.Left = 16; _pairingText.Top = 72; _pairingText.Width = 396; _pairingText.ReadOnly = true;
        var btnCopy = new Button { Text = "复制配对串", Left = 16, Top = 98, Width = 110 };
        btnCopy.Click += (s, e) =>
        {
            try { Clipboard.SetText(_pairingText.Text); btnCopy.Text = "已复制"; } catch { }
        };

        _qrBox.Left = 110; _qrBox.Top = 140; _qrBox.Width = 220; _qrBox.Height = 220;
        _qrBox.SizeMode = PictureBoxSizeMode.Zoom;

        _deviceLabel.Left = 16; _deviceLabel.Top = 374; _deviceLabel.Width = 428;
        _deviceLabel.Text = "当前未连接设备";

        var lbl3 = new Label { Text = "运行日志：", Left = 16, Top = 404, AutoSize = true };
        _logBox.Left = 16; _logBox.Top = 424; _logBox.Width = 428; _logBox.Height = 200;
        _logBox.HorizontalScrollbar = true;

        Controls.AddRange(new Control[] { lbl1, _ipCombo, lbl2, _pairingText, btnCopy, _qrBox, _deviceLabel, lbl3, _logBox });

        _ipCombo.SelectedIndexChanged += (s, e) => RefreshPairing();
        _server.ClientChanged += OnClientChanged;
        _server.Log += OnServerLog;

        RefreshPairing();
        UpdateDevice();

        FormClosed += (s, e) =>
        {
            _server.ClientChanged -= OnClientChanged;
            _server.Log -= OnServerLog;
        };
    }

    /// <summary>供 App 转发运行日志（已在 UI 线程调用）</summary>
    public void AppendLog(string line)
    {
        _logBox.Items.Add(line);
        while (_logBox.Items.Count > 200) _logBox.Items.RemoveAt(0);
        _logBox.TopIndex = _logBox.Items.Count - 1;
    }

    private void OnClientChanged()
    {
        if (IsHandleCreated) BeginInvoke(UpdateDevice);
    }

    private void OnServerLog(string msg)
    {
        if (IsHandleCreated) BeginInvoke(() => AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}"));
    }

    private void UpdateDevice()
    {
        var d = _server.CurrentDevice;
        _deviceLabel.Text = d == null ? "当前未连接设备" : $"已连接设备：{d}";
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
