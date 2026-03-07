namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// キーボード入力イベント。
/// </summary>
public class KeyEvent : InputEvent
{
    public override string Type => "key";

    /// <summary>キーアクション（"down", "up"）</summary>
    public string Action { get; set; } = "";

    /// <summary>仮想キーコード</summary>
    public int VkCode { get; set; }

    /// <summary>キー名（"A", "Enter", "Ctrl" 等）</summary>
    public string KeyName { get; set; } = "";

    /// <summary>修飾キーが押されているか</summary>
    public bool IsModifier { get; set; }

    /// <summary>対象ウィンドウハンドル（0xHHHH形式）</summary>
    public string? Hwnd { get; set; }
}
