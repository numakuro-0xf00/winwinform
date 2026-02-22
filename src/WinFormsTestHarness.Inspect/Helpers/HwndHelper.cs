using WinFormsTestHarness.Inspect.Inspectors;
using WinFormsTestHarness.Inspect.Models;

namespace WinFormsTestHarness.Inspect.Helpers;

public static class HwndHelper
{
    /// <summary>
    /// Resolves an IntPtr hwnd from either a hex string or a process name.
    /// </summary>
    public static IntPtr Resolve(string? hwndHex, string? processName, IUiaInspector inspector)
    {
        if (!string.IsNullOrEmpty(hwndHex))
        {
            return ParseHwnd(hwndHex);
        }

        if (!string.IsNullOrEmpty(processName))
        {
            return FindByProcess(processName, inspector);
        }

        throw new InvalidOperationException("Either --hwnd or --process must be specified.");
    }

    public static IntPtr ParseHwnd(string hwndHex)
    {
        var hex = hwndHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hwndHex[2..]
            : hwndHex;

        if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            return new IntPtr(value);
        }

        throw new ArgumentException($"Invalid hwnd format: '{hwndHex}'. Expected hex string like '0x001A0F32'.");
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

        return ParseHwnd(match.Hwnd);
    }
}
