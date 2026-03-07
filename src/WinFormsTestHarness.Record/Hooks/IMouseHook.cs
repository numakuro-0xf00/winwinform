using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// マウスフックの抽象化インターフェース。
/// </summary>
public interface IMouseHook : IDisposable
{
    /// <summary>マウスイベント発生時のコールバック</summary>
    event Action<MouseEvent>? OnMouseEvent;

    /// <summary>フックを設定する</summary>
    void Install();

    /// <summary>フックを解除する</summary>
    void Uninstall();
}
