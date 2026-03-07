using System.Runtime.InteropServices;
using WinFormsTestHarness.Record.Events;
using WinFormsTestHarness.Common.Timing;

namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// Win32 低レベルマウスフック（WH_MOUSE_LL）。
/// SetWindowsHookEx でグローバルフックを設定し、マウスイベントをキャプチャする。
/// </summary>
public class MouseHook : IMouseHook
{
    private readonly WindowTracker _tracker;
    private readonly IWindowApi _windowApi;
    private readonly PreciseTimestamp _timestamp;

    // GC 防止: デリゲートをフィールドに保持
    private NativeMethods.HookProc? _hookProc;
    private IntPtr _hookHandle;

    public event Action<MouseEvent>? OnMouseEvent;

    public MouseHook(WindowTracker tracker, IWindowApi windowApi, PreciseTimestamp timestamp)
    {
        _tracker = tracker;
        _windowApi = windowApi;
        _timestamp = timestamp;
    }

    public void Install()
    {
        _hookProc = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"マウスフックの設定に失敗しました (Error: {Marshal.GetLastWin32Error()})");
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Uninstall();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();

            var (action, button) = ClassifyMouseMessage(msg);
            if (action != null)
            {
                int? wheelDelta = msg == NativeMethods.WM_MOUSEWHEEL
                    ? (short)(info.mouseData >> 16)
                    : null;

                var evt = new MouseEvent
                {
                    Timestamp = _timestamp.Now(),
                    Action = action,
                    Button = button,
                    ScreenX = info.pt.X,
                    ScreenY = info.pt.Y,
                    WheelDelta = wheelDelta,
                };

                // ウィンドウ相対座標の計算
                var hwnd = _windowApi.WindowFromPoint(info.pt.X, info.pt.Y);
                if (hwnd != IntPtr.Zero && _tracker.BelongsToTarget(hwnd))
                {
                    var (left, top, _, _) = _windowApi.GetWindowRect(hwnd);
                    var (wx, wy) = CoordinateConverter.ToWindowRelative(info.pt.X, info.pt.Y, left, top);
                    evt.WindowX = wx;
                    evt.WindowY = wy;
                    evt.Hwnd = $"0x{hwnd.ToInt64():X8}";
                }

                OnMouseEvent?.Invoke(evt);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static (string? Action, string? Button) ClassifyMouseMessage(int msg)
    {
        return msg switch
        {
            NativeMethods.WM_MOUSEMOVE => ("move", null),
            NativeMethods.WM_LBUTTONDOWN => ("down", "left"),
            NativeMethods.WM_LBUTTONUP => ("up", "left"),
            NativeMethods.WM_LBUTTONDBLCLK => ("dblclick", "left"),
            NativeMethods.WM_RBUTTONDOWN => ("down", "right"),
            NativeMethods.WM_RBUTTONUP => ("up", "right"),
            NativeMethods.WM_RBUTTONDBLCLK => ("dblclick", "right"),
            NativeMethods.WM_MBUTTONDOWN => ("down", "middle"),
            NativeMethods.WM_MBUTTONUP => ("up", "middle"),
            NativeMethods.WM_MBUTTONDBLCLK => ("dblclick", "middle"),
            NativeMethods.WM_MOUSEWHEEL => ("wheel", null),
            _ => (null, null),
        };
    }
}
