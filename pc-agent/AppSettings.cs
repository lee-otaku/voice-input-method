using System.Text.Json;

namespace PcAgent;

/// <summary>持久化配置（%APPDATA%\PcAgent\config.json）</summary>
internal sealed class AppSettings
{
    public int Port { get; set; } = 53818;
    public string Token { get; set; } = "";
    public bool MappingEnabled { get; set; } = true;
    public bool AutoStart { get; set; }

    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcAgent");

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AppSettings Load()
    {
        AppSettings s;
        try
        {
            s = File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            s = new AppSettings();
        }
        s.EnsureToken();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>确保存在 32 位十六进制配对 Token</summary>
    public void EnsureToken()
    {
        if (Token.Length != 32)
        {
            Token = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            Save();
        }
    }
}
