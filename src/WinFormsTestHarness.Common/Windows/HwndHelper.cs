using WinFormsTestHarness.Common.Models;

namespace WinFormsTestHarness.Common.Windows;

/// <summary>
/// ウィンドウハンドル解決ヘルパー。
/// --hwnd (0x形式) または --process (部分一致) からウィンドウを特定する。
/// wfth-inspect, wfth-record, wfth-capture 等で共有。
/// </summary>
public static class HwndHelper
{
    /// <summary>
    /// 0x形式の16進数文字列を IntPtr に変換する。
    /// </summary>
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

    /// <summary>
    /// --hwnd または --process からウィンドウハンドルを解決する。
    /// いずれも未指定の場合は InvalidOperationException をスローする。
    /// </summary>
    public static IntPtr Resolve(string? hwndHex, string? processName, IReadOnlyList<WindowInfo> windows)
    {
        if (!string.IsNullOrEmpty(hwndHex))
        {
            return ParseHwnd(hwndHex);
        }

        if (!string.IsNullOrEmpty(processName))
        {
            return FindByProcess(processName, windows);
        }

        throw new InvalidOperationException("Either --hwnd or --process must be specified.");
    }

    /// <summary>
    /// プロセス名（部分一致、大文字小文字無視）でウィンドウを検索し、ハンドルを返す。
    /// </summary>
    public static IntPtr FindByProcess(string processName, IReadOnlyList<WindowInfo> windows)
    {
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
