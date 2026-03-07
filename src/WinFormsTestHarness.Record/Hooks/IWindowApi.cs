namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// ウィンドウ操作の抽象化インターフェース。
/// テスト時に FakeWindowApi で差し替え可能。
/// </summary>
public interface IWindowApi
{
    /// <summary>ウィンドウが存在するか</summary>
    bool IsWindow(IntPtr hwnd);

    /// <summary>ウィンドウのプロセスIDを取得する</summary>
    uint GetProcessId(IntPtr hwnd);

    /// <summary>ウィンドウタイトルを取得する</summary>
    string GetWindowTitle(IntPtr hwnd);

    /// <summary>ウィンドウ矩形を取得する</summary>
    (int Left, int Top, int Width, int Height) GetWindowRect(IntPtr hwnd);

    /// <summary>ルートオーナーウィンドウを取得する</summary>
    IntPtr GetRootOwner(IntPtr hwnd);

    /// <summary>ウィンドウスタイルを取得する</summary>
    int GetWindowStyle(IntPtr hwnd);

    /// <summary>ウィンドウ拡張スタイルを取得する</summary>
    int GetWindowExStyle(IntPtr hwnd);

    /// <summary>フォアグラウンドウィンドウを取得する</summary>
    IntPtr GetForegroundWindow();

    /// <summary>指定座標にあるウィンドウを取得する</summary>
    IntPtr WindowFromPoint(int x, int y);
}

/// <summary>
/// Win32 API を使用する IWindowApi の実装。
/// </summary>
public class Win32WindowApi : IWindowApi
{
    public bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public uint GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    public string GetWindowTitle(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length == 0) return "";
        var sb = new System.Text.StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public (int Left, int Top, int Width, int Height) GetWindowRect(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public IntPtr GetRootOwner(IntPtr hwnd)
        => NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);

    public int GetWindowStyle(IntPtr hwnd)
        => NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);

    public int GetWindowExStyle(IntPtr hwnd)
        => NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public IntPtr WindowFromPoint(int x, int y)
        => NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
}
