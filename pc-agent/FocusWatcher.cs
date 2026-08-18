using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcAgent;

/// <summary>当前焦点状态快照</summary>
internal sealed class FocusState
{
    public bool Ready { get; init; }
    public string App { get; init; } = "";
    public string Field { get; init; } = "";
}

/// <summary>
/// 焦点检测：WinEvent 钩子（EVENT_OBJECT_FOCUS / EVENT_SYSTEM_FOREGROUND）事件驱动，
/// 150ms 去抖合并，1 秒轮询兜底；UI Automation 判断焦点控件是否可编辑。
/// </summary>
internal sealed class FocusWatcher : IDisposable
{
    public event Action<FocusState>? Changed;

    private readonly object _stateLock = new();
    private FocusState _last = new();
    private System.Threading.Timer? _debounce;
    private System.Threading.Timer? _poll;
    private Thread? _thread;
    private IntPtr _hookFocus;
    private IntPtr _hookFg;
    private NativeMethods.WinEventDelegate? _proc;
    private uint _hookThreadId;
    private volatile bool _running;
    private readonly object _uiaLock = new();
    private IUIAutomation? _uia;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _uia ??= (IUIAutomation)new CUIAutomation();
        _debounce = new System.Threading.Timer(_ => QueryAndPublish(), null, 300, Timeout.Infinite);
        _poll = new System.Threading.Timer(_ => QueryAndPublish(), null, 1000, 1000);
        _thread = new Thread(HookThreadProc) { IsBackground = true, Name = "FocusHook" };
        _thread.Start();
    }

    /// <summary>钩子线程：注册 WinEvent 钩子并泵消息（OUTOFCONTEXT 回调依赖本线程消息循环）</summary>
    private void HookThreadProc()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _proc = WinEventProc;
        _hookFocus = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_FOCUS, NativeMethods.EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _hookFg = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
    }

    private void WinEventProc(IntPtr hHook, uint evt, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW && idObject != NativeMethods.OBJID_CLIENT) return;
        // 事件风暴中只处理最后一段（150ms 去抖）
        _debounce?.Change(150, Timeout.Infinite);
    }

    /// <summary>立即查询当前前台窗口焦点控件的编辑能力</summary>
    public FocusState QueryNow()
    {
        var state = new FocusState();
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return state;

            var tid = NativeMethods.GetWindowThreadProcessId(fg, out var pid);
            var target = fg;
            var gti = new NativeMethods.GUITHREADINFO { cb = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (tid != 0 && NativeMethods.GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero)
                target = gti.hwndFocus; // 取前台窗口内部真正持有焦点的子控件

            string app = "?";
            try { app = Process.GetProcessById((int)pid).ProcessName; } catch { }

            bool isText = false, isValue = false;
            int controlType = 0;
            string? name = null;
            lock (_uiaLock)
            {
                _uia!.ElementFromHandle(target, out var el);
                isText = GetBool(el, UiaPropertyIds.IsTextPatternAvailable);
                isValue = GetBool(el, UiaPropertyIds.IsValuePatternAvailable);
                controlType = GetInt(el, UiaPropertyIds.ControlType);
                name = GetString(el, UiaPropertyIds.Name);
            }

            var ready = isText || isValue || controlType is UiaControlTypes.Edit or UiaControlTypes.Document;
            return new FocusState
            {
                Ready = ready,
                App = app,
                Field = Describe(controlType, name, isText, isValue),
            };
        }
        catch
        {
            return state;
        }
    }

    private static bool GetBool(IUIAutomationElement el, int prop)
    {
        try { el.GetCurrentPropertyValue(prop, out var v); return v is bool b && b; }
        catch { return false; }
    }

    private static int GetInt(IUIAutomationElement el, int prop)
    {
        try { el.GetCurrentPropertyValue(prop, out var v); return v is int i ? i : 0; }
        catch { return 0; }
    }

    private static string? GetString(IUIAutomationElement el, int prop)
    {
        try { el.GetCurrentPropertyValue(prop, out var v); return v as string; }
        catch { return null; }
    }

    private static string Describe(int controlType, string? name, bool isText, bool isValue)
    {
        var typeName = controlType switch
        {
            UiaControlTypes.Edit => "编辑框",
            UiaControlTypes.Document => "文档",
            _ => isText ? "文本区" : isValue ? "输入框" : ""
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Length > 30 ? name[..30] + "…" : name;
            return typeName.Length > 0 ? $"{typeName}「{n}」" : n;
        }
        return typeName;
    }

    private void QueryAndPublish()
    {
        try
        {
            var s = QueryNow();
            bool changed;
            lock (_stateLock)
            {
                changed = s.Ready != _last.Ready || s.App != _last.App || s.Field != _last.Field;
                if (changed) _last = s;
            }
            if (changed) Changed?.Invoke(s);
        }
        catch { }
    }

    public void Dispose()
    {
        _running = false;
        _debounce?.Dispose();
        _debounce = null;
        _poll?.Dispose();
        _poll = null;
        if (_hookFocus != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hookFocus);
        if (_hookFg != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hookFg);
        if (_hookThreadId != 0)
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
