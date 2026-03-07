using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// 対象ウィンドウの追跡と判定を行う。
/// プロセスID ベースで対象ウィンドウとそのモーダルダイアログを管理する。
/// </summary>
public class WindowTracker
{
    private readonly IWindowApi _windowApi;
    private readonly HashSet<uint> _targetPids = new();
    private readonly HashSet<IntPtr> _trackedWindows = new();
    private IntPtr _mainHwnd;

    public WindowTracker(IWindowApi windowApi, IntPtr mainHwnd, uint mainPid)
    {
        _windowApi = windowApi;
        _mainHwnd = mainHwnd;
        _targetPids.Add(mainPid);
        _trackedWindows.Add(mainHwnd);
    }

    /// <summary>
    /// 指定されたウィンドウが記録対象に属するか判定する。
    /// 対象プロセスのウィンドウ、またはそのモーダルダイアログを含む。
    /// </summary>
    public bool BelongsToTarget(IntPtr hwnd)
    {
        if (_trackedWindows.Contains(hwnd))
            return true;

        if (!_windowApi.IsWindow(hwnd))
            return false;

        var pid = _windowApi.GetProcessId(hwnd);
        if (_targetPids.Contains(pid))
        {
            _trackedWindows.Add(hwnd);
            return true;
        }

        // モーダルダイアログ: オーナーが対象ウィンドウのポップアップ
        var rootOwner = _windowApi.GetRootOwner(hwnd);
        if (_trackedWindows.Contains(rootOwner))
        {
            _trackedWindows.Add(hwnd);
            return true;
        }

        return false;
    }

    /// <summary>
    /// ウィンドウがモーダルダイアログか判定する。
    /// </summary>
    public bool IsModalDialog(IntPtr hwnd)
    {
        if (hwnd == _mainHwnd)
            return false;

        var style = (uint)_windowApi.GetWindowStyle(hwnd);
        var exStyle = (uint)_windowApi.GetWindowExStyle(hwnd);

        bool isPopup = (style & NativeMethods.WS_POPUP) != 0;
        bool isDialogFrame = (exStyle & NativeMethods.WS_EX_DLGMODALFRAME) != 0;

        return isPopup || isDialogFrame;
    }

    /// <summary>
    /// ウィンドウ情報を取得する。
    /// </summary>
    public WindowEvent CreateWindowEvent(IntPtr hwnd, string action)
    {
        var (left, top, width, height) = _windowApi.GetWindowRect(hwnd);
        return new WindowEvent
        {
            Action = action,
            Hwnd = $"0x{hwnd.ToInt64():X8}",
            Title = _windowApi.GetWindowTitle(hwnd),
            Rect = new WindowRect(left, top, width, height),
        };
    }

    /// <summary>追跡対象に追加する</summary>
    public bool TryTrack(IntPtr hwnd)
    {
        return _trackedWindows.Add(hwnd);
    }

    /// <summary>追跡対象から除外する</summary>
    public bool TryUntrack(IntPtr hwnd)
    {
        if (hwnd == _mainHwnd) return false;
        return _trackedWindows.Remove(hwnd);
    }

    /// <summary>メインウィンドウのハンドル</summary>
    public IntPtr MainHwnd => _mainHwnd;

    /// <summary>追跡中のウィンドウ数</summary>
    public int TrackedCount => _trackedWindows.Count;
}
