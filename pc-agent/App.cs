using System.Drawing;
using System.Drawing.Drawing2D;

namespace PcAgent;

/// <summary>托盘应用主体：装配 服务 / 焦点检测 / 主窗口 / 托盘图标</summary>
internal sealed class App : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly WsServer _server;
    private readonly FocusWatcher _watcher = new();
    private readonly NotifyIcon _tray;
    private readonly MainForm _main;

    public App()
    {
        _instance = this;
        _settings = AppSettings.Load();

        _server = new WsServer
        {
            Port = _settings.Port,
            Token = _settings.Token,
            MappingEnabled = _settings.MappingEnabled,
            QueryFocus = _watcher.QueryNow,
        };

        _main = new MainForm(_settings, _server, _watcher);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (s, e) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "PC 输入桥接",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (s, e) => ShowMain();

        _watcher.Changed += fs => _ = _server.PushFocusAsync(fs);

        _watcher.Start();
        _server.Start();
        _main.Show();
    }

    private void ShowMain()
    {
        _main.Show();
        _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    internal static void ShowTrayBubble(string msg)
    {
        // 由 Program 持有的托盘实例显示；此处通过查找创建者传入（简化：直接由 App 静态引用）
        _instance?._tray.ShowBalloonTip(3000, "PC 输入桥接", msg, ToolTipIcon.Info);
    }

    private static App? _instance;

    internal static Icon MakeIcon()
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
        _watcher.Dispose();
        _server.Dispose();
        Application.Exit();
    }
}
