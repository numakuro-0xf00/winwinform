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
}
