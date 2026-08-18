using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcAgent;

/// <summary>一个已连接（或连接中）的手机会话</summary>
internal sealed class ClientSession
{
    public required WebSocket Ws { get; init; }
    public string Device { get; set; } = "?";
}

/// <summary>
/// WebSocket 服务：TcpListener + 手写 HTTP Upgrade 握手（免 URLACL / 管理员权限），
/// 单个已认证客户端，新设备接入会顶掉旧设备。
/// </summary>
internal sealed class WsServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource _cts = new();
    private ClientSession? _session;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _injectLock = new(1, 1);
    private const string WsMagic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public int Port { get; set; }
    public string Token { get; set; } = "";
    public string PcName { get; set; } = Environment.MachineName;
    public bool MappingEnabled { get; set; } = true;

    /// <summary>由外部提供：即时查询当前焦点状态</summary>
    public required Func<FocusState> QueryFocus { get; init; }

    public event Action<string>? Log;
    public event Action? ClientChanged;

    public string? CurrentDevice => Volatile.Read(ref _session)?.Device;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            Log?.Invoke($"服务已启动：0.0.0.0:{Port}");
            _ = AcceptLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"服务启动失败：{ex.Message}（端口被占用？）");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log?.Invoke($"接受连接失败：{ex.Message}"); continue; }
            _ = HandleClientAsync(tcp, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var peer = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        ClientSession? session = null;
        try
        {
            tcp.NoDelay = true;
            var stream = tcp.GetStream();
            var (_, headers) = await ReadHandshakeAsync(stream, ct);

            if (!headers.TryGetValue("sec-websocket-key", out var key) ||
                !headers.TryGetValue("upgrade", out var up) ||
                !up.Contains("websocket", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(key))
            {
                await WriteHttpRejectAsync(stream, "expected websocket upgrade");
                return;
            }

            var acceptKey = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WsMagic)));
            var resp = "HTTP/1.1 101 Switching Protocols\r\n" +
                       "Upgrade: websocket\r\n" +
                       "Connection: Upgrade\r\n" +
                       $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), ct);

            session = new ClientSession { Ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30)) };
            await ReceiveLoopAsync(session, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log?.Invoke($"连接 {peer} 异常：{ex.Message}");
        }
        finally
        {
            try { tcp.Close(); } catch { }
            if (session != null &&
                ReferenceEquals(Interlocked.CompareExchange(ref _session, null, session), session))
            {
                Log?.Invoke($"设备 {session.Device} 已断开");
                ClientChanged?.Invoke();
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientSession session, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (session.Ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await session.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await session.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                break;
            }
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var msg = sb.ToString();
            sb.Clear();
            var keepGoing = await HandleMessageAsync(session, msg, ct);
            if (!keepGoing) break;
        }
    }

    private async Task<bool> HandleMessageAsync(ClientSession session, string json, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return true; } // 忽略坏包

        using (doc)
        {
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "hello":
                {
                    var token = root.TryGetProperty("token", out var tk) ? tk.GetString() : null;
                    var device = root.TryGetProperty("device", out var dv) ? dv.GetString() : "?";
                    if (!string.Equals(token, Token, StringComparison.Ordinal))
                    {
                        Log?.Invoke($"设备 {device} Token 校验失败，断开");
                        return false;
                    }
                    session.Device = device ?? "?";
                    var old = Interlocked.Exchange(ref _session, session);
                    if (old != null && !ReferenceEquals(old, session))
                    {
                        Log?.Invoke($"新设备 {session.Device} 接入，断开旧设备 {old.Device}");
                        _ = SafeCloseAsync(old);
                    }
                    Log?.Invoke($"设备已连接：{session.Device}");
                    await SendAsync(session, Protocol.AuthOk(PcName), ct);
                    var fs = QueryFocus();
                    await SendAsync(session, Protocol.Focus(fs.Ready, fs.App, fs.Field), ct);
                    ClientChanged?.Invoke();
                    return true;
                }
                case "text":
                {
                    if (!ReferenceEquals(Volatile.Read(ref _session), session)) return true; // 未认证会话发文本，忽略
                    var msgId = root.TryGetProperty("msgId", out var mi) ? mi.GetString() ?? "" : "";
                    var text = root.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                    var overwrite = root.TryGetProperty("overwrite", out var ow) &&
                                    ow.ValueKind == JsonValueKind.True;
                    _ = Task.Run(() => HandleTextAsync(session, msgId, text, overwrite));
                    return true;
                }
                case "ping":
                    await SendAsync(session, Protocol.Pong(), ct);
                    return true;
                default:
                    return true;
            }
        }
    }

    /// <summary>处理一条待注入文本：焦点检查 → SendInput 注入 → ack / nack</summary>
    private async Task HandleTextAsync(ClientSession session, string msgId, string text, bool overwrite)
    {
        try
        {
            if (!MappingEnabled)
            {
                await SendAsync(session, Protocol.Nack(msgId, "not_enabled"), CancellationToken.None);
                return;
            }
            if (!QueryFocus().Ready)
            {
                await SendAsync(session, Protocol.Nack(msgId, "no_focus"), CancellationToken.None);
                return;
            }

            await _injectLock.WaitAsync();
            try
            {
                // 注入前再确认一次焦点仍可用（注入期间占用真实焦点）
                if (!QueryFocus().Ready)
                {
                    await SendAsync(session, Protocol.Nack(msgId, "no_focus"), CancellationToken.None);
                    return;
                }
                var ok = InputInjector.InjectText(text, overwrite);
                await SendAsync(session,
                    ok ? Protocol.Ack(msgId) : Protocol.Nack(msgId, "inject_failed"),
                    CancellationToken.None);
            }
            finally
            {
                _injectLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"处理文本消息失败：{ex.Message}");
            try { await SendAsync(session, Protocol.Nack(msgId, "inject_failed"), CancellationToken.None); }
            catch { }
        }
    }

    /// <summary>焦点状态变化时推送给已连接的手机</summary>
    public Task PushFocusAsync(FocusState fs)
    {
        var s = Volatile.Read(ref _session);
        return s == null ? Task.CompletedTask : SendAsync(s, Protocol.Focus(fs.Ready, fs.App, fs.Field), CancellationToken.None);
    }

    public void DisconnectClient()
    {
        var s = Interlocked.Exchange(ref _session, null);
        if (s != null)
        {
            _ = SafeCloseAsync(s);
            ClientChanged?.Invoke();
        }
    }

    private async Task SendAsync(ClientSession session, string json, CancellationToken ct)
    {
        if (session.Ws.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync(ct);
        try
        {
            if (session.Ws.State == WebSocketState.Open)
                await session.Ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                    WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task SafeCloseAsync(ClientSession s)
    {
        try { if (s.Ws.State == WebSocketState.Open) await s.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None); }
        catch { }
        try { s.Ws.Dispose(); }
        catch { }
    }

    /// <summary>逐字节读到 \r\n\r\n 为止（头部很小；不越界读取，WebSocket 帧数据留在 socket 中）</summary>
    private static async Task<(string Path, Dictionary<string, string> Headers)> ReadHandshakeAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var bytes = new List<byte>(1024);
        var one = new byte[1];
        while (bytes.Count < 16 * 1024)
        {
            var n = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0) throw new IOException("对端在握手完成前断开");
            bytes.Add(one[0]);
            var c = bytes.Count;
            if (c >= 4 && bytes[^1] == (byte)'\n' && bytes[^2] == (byte)'\r' &&
                bytes[^3] == (byte)'\n' && bytes[^4] == (byte)'\r')
                break;
        }
        var text = Encoding.ASCII.GetString(bytes.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var path = "/";
        if (lines.Length > 0)
        {
            var parts = lines[0].Split(' ');
            if (parts.Length > 1) path = parts[1];
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx > 0) headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return (path, headers);
    }

    private static async Task WriteHttpRejectAsync(NetworkStream stream, string reason)
    {
        var body = Encoding.UTF8.GetBytes(reason);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 401 Unauthorized\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        var s = Interlocked.Exchange(ref _session, null);
        if (s != null) _ = SafeCloseAsync(s);
        _cts.Dispose();
        _sendLock.Dispose();
        _injectLock.Dispose();
    }
}
