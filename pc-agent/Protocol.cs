using System.Text.Json;

namespace PcAgent;

/// <summary>PC → 手机 / 手机 → PC 的协议消息构造（JSON 文本帧）</summary>
internal static class Protocol
{
    public static string AuthOk(string pcName) =>
        JsonSerializer.Serialize(new { type = "auth_ok", pc = pcName });

    public static string Focus(bool ready, string app, string field) =>
        JsonSerializer.Serialize(new { type = "focus", ready, app, field });

    public static string Ack(string msgId) =>
        JsonSerializer.Serialize(new { type = "ack", msgId });

    public static string Nack(string msgId, string reason) =>
        JsonSerializer.Serialize(new { type = "nack", msgId, reason });

    public static string Pong() => """{"type":"pong"}""";
}
