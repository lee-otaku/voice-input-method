namespace PcAgent;

/// <summary>
/// 文本注入：SendInput + KEYEVENTF_UNICODE（按 Unicode 码点注入，与键盘布局无关，天然支持中文）。
/// \n 映射为 Enter 键；覆盖模式先 Ctrl+A + Delete 清空目标控件。
/// </summary>
internal static class InputInjector
{
    private const int BatchEvents = 64;

    public static bool InjectText(string text, bool overwrite)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (overwrite)
        {
            if (!SendCombo(new[] { NativeMethods.VK_CONTROL }, NativeMethods.VK_A)) return false;
            if (!SendKey(NativeMethods.VK_DELETE)) return false;
            Thread.Sleep(20);
        }

        var batch = new List<NativeMethods.INPUT>(BatchEvents + 2);

        bool Flush()
        {
            if (batch.Count == 0) return true;
            var n = batch.Count;
            var sent = NativeMethods.SendInput((uint)n, batch.ToArray(), NativeMethods.INPUT.Size);
            batch.Clear();
            return sent == n;
        }

        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                batch.Add(Key(NativeMethods.VK_RETURN, down: true));
                batch.Add(Key(NativeMethods.VK_RETURN, down: false));
            }
            else
            {
                // 单个 KEYEVENTF_UNICODE 输入即注入一个字符（按下+释放合一）
                batch.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.InputUnion
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = ch,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero,
                        }
                    }
                });
            }

            if (batch.Count >= BatchEvents)
            {
                if (!Flush()) return false;
                Thread.Sleep(10); // 分批之间稍作停顿，给目标应用处理时间
            }
        }
        return Flush();
    }

    private static NativeMethods.INPUT Key(ushort vk, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = down ? 0 : NativeMethods.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    /// <summary>发送单个按键（按下+释放）</summary>
    private static bool SendKey(ushort vk)
    {
        var inputs = new[] { Key(vk, true), Key(vk, false) };
        return NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.INPUT.Size) == inputs.Length;
    }

    /// <summary>发送组合键：修饰键按下 → 主键按下/释放 → 修饰键释放</summary>
    private static bool SendCombo(ushort[] modifiers, ushort key)
    {
        var list = new List<NativeMethods.INPUT>();
        foreach (var m in modifiers) list.Add(Key(m, true));
        list.Add(Key(key, true));
        list.Add(Key(key, false));
        for (var i = modifiers.Length - 1; i >= 0; i--) list.Add(Key(modifiers[i], false));
        var arr = list.ToArray();
        return NativeMethods.SendInput((uint)arr.Length, arr, NativeMethods.INPUT.Size) == arr.Length;
    }
}
