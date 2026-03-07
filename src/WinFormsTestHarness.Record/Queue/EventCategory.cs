namespace WinFormsTestHarness.Record.Queue;

/// <summary>
/// イベントカテゴリ。キュー劣化時のドロップ優先順位に使用。
/// </summary>
public enum EventCategory
{
    /// <summary>マウス移動（最低優先度、最初にドロップ）</summary>
    MouseMove,

    /// <summary>マウスクリック・ホイール</summary>
    MouseAction,

    /// <summary>キーボード入力</summary>
    Key,

    /// <summary>ウィンドウ状態変化</summary>
    Window,

    /// <summary>セッション制御（最高優先度、絶対ドロップしない）</summary>
    Session,

    /// <summary>システム診断</summary>
    System,
}
