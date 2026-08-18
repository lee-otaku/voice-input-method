using System.Net.WebSockets;
using System.Text;
using PcAgent;

// —— 协议集成测试（Linux 可跑）：验证握手 / 鉴权 / focus 推送 / ping-pong / text-ack-nack ——
// FocusState 与 InputInjector 在此以桩实现（真机版在 FocusWatcher.cs / InputInjector.cs）

var readyState = new FocusState { Ready = false, App = "", Field = "" };
InputInjector.InjectResult = true;
var server = new WsServer
{
    Port = 53901,
    Token = new string('a', 32),
    PcName = "TestPC",
    QueryFocus = () => readyState,
};
server.Log += m => Console.WriteLine("[srv] " + m);
server.Start();
await Task.Delay(300);

var failures = new List<string>();
void Check(string name, bool cond, string actual = "")
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {name}  {actual}");
    if (!cond) failures.Add(name);
}

// 1. 错误 Token → 服务器应断开且不回 auth_ok
using (var ws = new ClientWebSocket())
{
    await ws.ConnectAsync(new Uri("ws://127.0.0.1:53901/"), CancellationToken.None);
    await Send(ws, """{"type":"hello","token":"wrong","device":"BadPhone"}""");
    var resp = await ReceiveOne(ws);
    Check("1 错误Token被拒绝(无auth_ok)", resp == null, $"resp={resp ?? "(连接关闭)"}");
}

// 2. 正确 Token → auth_ok + focus 快照
using (var ws = new ClientWebSocket())
{
    await ws.ConnectAsync(new Uri("ws://127.0.0.1:53901/"), CancellationToken.None);
    await Send(ws, $$"""{"type":"hello","token":"{{new string('a', 32)}}","device":"TestPhone"}""");
    var auth = await ReceiveOne(ws);
    var focus = await ReceiveOne(ws);
    Check("2a auth_ok", auth != null && auth.Contains("auth_ok") && auth.Contains("TestPC"), auth ?? "(null)");
    Check("2b focus快照", focus != null && focus.Contains("\"ready\":false"), focus ?? "(null)");

    // ping → pong
    await Send(ws, """{"type":"ping"}""");
    var pong = await ReceiveOne(ws);
    Check("2c ping→pong", pong != null && pong.Contains("pong"), pong ?? "(null)");

    // text 且焦点不可用 → nack no_focus
    await Send(ws, """{"type":"text","msgId":"m1","text":"你好","overwrite":false}""");
    var nack = await ReceiveOne(ws);
    Check("2d 无焦点→nack", nack != null && nack.Contains("nack") && nack.Contains("no_focus"), nack ?? "(null)");

    // 焦点就绪 + 注入成功 → ack
    readyState = new FocusState { Ready = true, App = "notepad", Field = "编辑框" };
    await Send(ws, """{"type":"text","msgId":"m2","text":"你好\nworld","overwrite":true}""");
    var ack = await ReceiveOne(ws);
    Check("2e 有焦点→ack", ack != null && ack.Contains("\"type\":\"ack\"") && ack.Contains("m2"), ack ?? "(null)");

    // 焦点就绪 + 注入失败 → nack inject_failed
    InputInjector.InjectResult = false;
    await Send(ws, """{"type":"text","msgId":"m3","text":"x","overwrite":false}""");
    var nack2 = await ReceiveOne(ws);
    Check("2f 注入失败→nack", nack2 != null && nack2.Contains("inject_failed"), nack2 ?? "(null)");

    // 焦点变化推送（由外部 FocusWatcher 触发）
    readyState = new FocusState { Ready = true, App = "chrome", Field = "文档" };
    await server.PushFocusAsync(readyState);
    var push = await ReceiveOne(ws);
    Check("2g focus推送", push != null && push.Contains("chrome"), push ?? "(null)");
}

server.Dispose();
Console.WriteLine(failures.Count == 0 ? "\n全部通过" : $"\n失败 {failures.Count} 项: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

static async Task Send(ClientWebSocket ws, string json)
{
    await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
}

static async Task<string?> ReceiveOne(ClientWebSocket ws)
{
    try
    {
        var buf = new byte[8192];
        var sb = new StringBuilder();
        while (true)
        {
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            if (r.EndOfMessage) return sb.ToString();
        }
    }
    catch (WebSocketException)
    {
        return null; // 被服务器断开
    }
}

// —— 桩实现（与 PC 端真实签名一致） ——

internal sealed class FocusState
{
    public bool Ready { get; init; }
    public string App { get; init; } = "";
    public string Field { get; init; } = "";
}

internal static class InputInjector
{
    public static bool InjectResult { get; set; } = true;
    public static bool InjectText(string text, bool overwrite) => InjectResult;
}
