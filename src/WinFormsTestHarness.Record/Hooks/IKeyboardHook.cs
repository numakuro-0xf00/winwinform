using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// キーボードフックの抽象化インターフェース。
/// </summary>
public interface IKeyboardHook : IDisposable
{
    /// <summary>キーイベント発生時のコールバック</summary>
    event Action<KeyEvent>? OnKeyEvent;

    /// <summary>フックを設定する</summary>
    void Install();

    /// <summary>フックを解除する</summary>
    void Uninstall();
}
