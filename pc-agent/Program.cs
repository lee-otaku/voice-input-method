namespace PcAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.ThreadException += (s, e) => LogError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);
        Application.Run(new App());
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
