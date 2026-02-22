using WinFormsTestHarness.Inspect.Inspectors;
using CommonHwnd = WinFormsTestHarness.Common.Windows.HwndHelper;

namespace WinFormsTestHarness.Inspect.Helpers;

public static class HwndHelper
{
    /// <summary>
    /// --hwnd または --process からウィンドウハンドルを解決する。
    /// </summary>
    public static IntPtr Resolve(string? hwndHex, string? processName, IUiaInspector inspector)
    {
        if (!string.IsNullOrEmpty(hwndHex))
        {
            return CommonHwnd.ParseHwnd(hwndHex);
        }

        if (!string.IsNullOrEmpty(processName))
        {
            return FindByProcess(processName, inspector);
        }

        throw new InvalidOperationException("Either --hwnd or --process must be specified.");
    }

    public static IntPtr FindByProcess(string processName, IUiaInspector inspector)
    {
        var windows = inspector.ListWindows();
        var match = windows.FirstOrDefault(w =>
            w.Process.Contains(processName, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            throw new InvalidOperationException(
                $"No window found for process matching '{processName}'. " +
                $"Available: {string.Join(", ", windows.Select(w => w.Process).Distinct())}");
        }

        return CommonHwnd.ParseHwnd(match.Hwnd);
    }
}
