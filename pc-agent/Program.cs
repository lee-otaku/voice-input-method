namespace PcAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogError(e.Exception);
                MessageBox.Show($"程序发生错误：\n\n{e.Exception.Message}\n\n详细信息已写入：\n{LogPath}",
                    "PC 输入桥接", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new App());
        }
        catch (Exception ex)
        {
            // 构造函数阶段崩溃（如 COM 初始化、端口占用等），弹窗告知用户
            LogError(ex);
            MessageBox.Show($"程序启动失败：\n\n{ex.Message}\n\n详细信息已写入：\n{LogPath}",
                "PC 输入桥接", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>错误日志（简单追加写文件，便于排查）</summary>
    public static string LogPath { get; } = Path.Combine(AppSettings.ConfigDir, "error.log");

    private static void LogError(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigDir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
