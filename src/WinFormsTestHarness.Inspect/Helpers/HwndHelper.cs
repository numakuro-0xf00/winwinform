using WinFormsTestHarness.Inspect.Inspectors;
using CommonHwnd = WinFormsTestHarness.Common.Windows.HwndHelper;

namespace WinFormsTestHarness.Inspect.Helpers;

/// <summary>
/// IUiaInspector を受け取る convenience overload。
/// 実際のロジックは Common.Windows.HwndHelper に委譲する。
/// </summary>
public static class HwndHelper
{
    public static IntPtr Resolve(string? hwndHex, string? processName, IUiaInspector inspector)
    {
        if (!string.IsNullOrEmpty(hwndHex))
        {
            return CommonHwnd.ParseHwnd(hwndHex);
        }

        return CommonHwnd.Resolve(hwndHex, processName, inspector.ListWindows());
    }
}
