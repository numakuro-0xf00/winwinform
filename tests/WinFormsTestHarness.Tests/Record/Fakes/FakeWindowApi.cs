using WinFormsTestHarness.Record.Hooks;

namespace WinFormsTestHarness.Tests.Record.Fakes;

/// <summary>
/// テスト用 IWindowApi Fake 実装。
/// ウィンドウの状態を辞書で管理する。
/// </summary>
public class FakeWindowApi : IWindowApi
{
    public class WindowState
    {
        public uint ProcessId { get; set; }
        public string Title { get; set; } = "";
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public IntPtr RootOwner { get; set; }
        public int Style { get; set; }
        public int ExStyle { get; set; }
    }

    private readonly Dictionary<IntPtr, WindowState> _windows = new();
    public IntPtr ForegroundWindow { get; set; }

    public void AddWindow(IntPtr hwnd, WindowState state)
    {
        _windows[hwnd] = state;
    }

    public bool IsWindow(IntPtr hwnd) => _windows.ContainsKey(hwnd);

    public uint GetProcessId(IntPtr hwnd)
        => _windows.TryGetValue(hwnd, out var w) ? w.ProcessId : 0;

    public string GetWindowTitle(IntPtr hwnd)
        => _windows.TryGetValue(hwnd, out var w) ? w.Title : "";

    public (int Left, int Top, int Width, int Height) GetWindowRect(IntPtr hwnd)
        => _windows.TryGetValue(hwnd, out var w) ? (w.Left, w.Top, w.Width, w.Height) : (0, 0, 0, 0);

    public IntPtr GetRootOwner(IntPtr hwnd)
        => _windows.TryGetValue(hwnd, out var w) ? w.RootOwner : IntPtr.Zero;

    public int GetWindowStyle(IntPtr hwnd)
        => _windows.TryGetValue(hwnd, out var w) ? w.Style : 0;

    public int GetWindowExStyle(IntPtr hwnd)
        => _windows.TryGetValue(hwnd, out var w) ? w.ExStyle : 0;

    public IntPtr GetForegroundWindow() => ForegroundWindow;

    public IntPtr WindowFromPoint(int x, int y)
    {
        foreach (var (hwnd, w) in _windows)
        {
            if (x >= w.Left && x < w.Left + w.Width && y >= w.Top && y < w.Top + w.Height)
                return hwnd;
        }
        return IntPtr.Zero;
    }
}
