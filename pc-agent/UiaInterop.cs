using System.Runtime.InteropServices;

namespace PcAgent;

/// <summary>UIA 属性 ID（见 UIAutomationClient.h）</summary>
internal static class UiaPropertyIds
{
    public const int ProcessId = 30002;
    public const int ControlType = 30003;
    public const int Name = 30005;
    public const int IsValuePatternAvailable = 30045;
    public const int IsTextPatternAvailable = 30046;
}

internal static class UiaControlTypes
{
    public const int Edit = 50004;
    public const int Document = 50030;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

/// <summary>
/// 手工声明的 UIA COM Interop。
/// 只声明 vtable 顶部且实际会用到的方法；未用的方法以占位签名保持槽位顺序正确。
/// </summary>
[ComImport]
[Guid("30cbe57d-d9d0-452a-ab13-7ac5ac8625ea")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    void CompareElements(IntPtr el1, IntPtr el2, out int areSame);            // 占位
    void CompareRuntimeIds(IntPtr id1, IntPtr id2, out int areSame);          // 占位
    void GetRootElement(out IUIAutomationElement element);                    // 占位
    void ElementFromHandle(IntPtr hwnd, out IUIAutomationElement element);    // ★ 使用
    void ElementFromPoint(POINT pt, out IUIAutomationElement element);        // 占位
    void GetFocusedElement(out IUIAutomationElement element);                 // 占位
}

[ComImport]
[Guid("929de380-6b6c-4b0f-a63e-1d956252f730")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void SetFocus();                                                                            // 占位
    void GetRuntimeId(out IntPtr runtimeId);                                                    // 占位
    void FindFirst(int scope, IntPtr condition, out IUIAutomationElement found);                // 占位
    void FindAll(int scope, IntPtr condition, out IntPtr found);                                // 占位
    void FindFirstBuildCache(int scope, IntPtr condition, IntPtr cache, out IUIAutomationElement found); // 占位
    void FindAllBuildCache(int scope, IntPtr condition, IntPtr cache, out IntPtr found);        // 占位
    void BuildUpdatedCache(IntPtr cacheRequest, out IUIAutomationElement element);              // 占位
    void GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object retVal); // ★ 使用
}

/// <summary>CUIAutomation 组件类（系统自带 UIA 客户端实现）</summary>
[ComImport]
[Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
internal class CUIAutomation
{
}
