namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// ウィンドウ状態変化イベント。
/// ウィンドウのフォーカス変化、モーダルダイアログ出現等を記録する。
/// </summary>
public class WindowEvent : InputEvent
{
    public override string Type => "window";

    /// <summary>ウィンドウアクション（"focus", "blur", "modal_open", "modal_close", "move", "resize"）</summary>
    public string Action { get; set; } = "";

    /// <summary>対象ウィンドウハンドル（0xHHHH形式）</summary>
    public string Hwnd { get; set; } = "";

    /// <summary>ウィンドウタイトル</summary>
    public string? Title { get; set; }

    /// <summary>ウィンドウ矩形（X, Y, Width, Height）</summary>
    public WindowRect? Rect { get; set; }
}

/// <summary>ウィンドウ矩形</summary>
public record WindowRect(int X, int Y, int Width, int Height);
