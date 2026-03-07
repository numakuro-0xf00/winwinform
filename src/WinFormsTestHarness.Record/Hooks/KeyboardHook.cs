using System.Runtime.InteropServices;
using WinFormsTestHarness.Record.Events;
using WinFormsTestHarness.Common.Timing;

namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// Win32 低レベルキーボードフック（WH_KEYBOARD_LL）。
/// SetWindowsHookEx でグローバルフックを設定し、キーイベントをキャプチャする。
/// </summary>
public class KeyboardHook : IKeyboardHook
{
    private readonly WindowTracker _tracker;
    private readonly IWindowApi _windowApi;
    private readonly PreciseTimestamp _timestamp;

    // GC 防止: デリゲートをフィールドに保持
    private NativeMethods.HookProc? _hookProc;
    private IntPtr _hookHandle;

    public event Action<KeyEvent>? OnKeyEvent;

    public KeyboardHook(WindowTracker tracker, IWindowApi windowApi, PreciseTimestamp timestamp)
    {
        _tracker = tracker;
        _windowApi = windowApi;
        _timestamp = timestamp;
    }

    public void Install()
    {
        _hookProc = HookCallback;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"キーボードフックの設定に失敗しました (Error: {Marshal.GetLastWin32Error()})");
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
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();

            var action = msg switch
            {
                NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN => "down",
                NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP => "up",
                _ => null,
            };

            if (action != null)
            {
                var vkCode = (int)info.vkCode;
                var evt = new KeyEvent
                {
                    Timestamp = _timestamp.Now(),
                    Action = action,
                    VkCode = vkCode,
                    KeyName = KeyNameResolver.Resolve(vkCode),
                    IsModifier = KeyNameResolver.IsModifier(vkCode),
                };

                // フォアグラウンドウィンドウが対象の場合のみ Hwnd を設定
                var fgHwnd = _windowApi.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero && _tracker.BelongsToTarget(fgHwnd))
                {
                    evt.Hwnd = $"0x{fgHwnd.ToInt64():X8}";
                }

                OnKeyEvent?.Invoke(evt);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
