namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// マウス入力イベント。
/// スクリーン座標とウィンドウ相対座標の両方を保持する。
/// </summary>
public class MouseEvent : InputEvent
{
    public override string Type => "mouse";

    /// <summary>マウスアクション（"move", "click", "dblclick", "down", "up", "wheel"）</summary>
    public string Action { get; set; } = "";

    /// <summary>マウスボタン（"left", "right", "middle", null for move/wheel）</summary>
    public string? Button { get; set; }

    /// <summary>スクリーンX座標</summary>
    public int ScreenX { get; set; }

    /// <summary>スクリーンY座標</summary>
    public int ScreenY { get; set; }

    /// <summary>ウィンドウ相対X座標</summary>
    public int? WindowX { get; set; }

    /// <summary>ウィンドウ相対Y座標</summary>
    public int? WindowY { get; set; }

    /// <summary>ホイールデルタ（wheel アクション時のみ）</summary>
    public int? WheelDelta { get; set; }

    /// <summary>対象ウィンドウハンドル（0xHHHH形式）</summary>
    public string? Hwnd { get; set; }
}
