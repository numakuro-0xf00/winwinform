namespace WinFormsTestHarness.Aggregate.Models;

/// <summary>
/// wfth-record 出力の生イベント共通フィールド。
/// type で mouse / key / window / session / system / screenshot を判別する。
/// </summary>
public class RawEvent
{
    public string? Ts { get; set; }
    public string? Type { get; set; }
}

/// <summary>
/// マウスイベント (type: "mouse")。
/// Action: LeftDown, LeftUp, RightDown, RightUp, MiddleDown, MiddleUp, Move, WheelUp, WheelDown
/// </summary>
public class RawMouseEvent : RawEvent
{
    public string? Action { get; set; }
    public int Sx { get; set; }
    public int Sy { get; set; }
    public int Rx { get; set; }
    public int Ry { get; set; }
    public bool Drag { get; set; }
    public int? Delta { get; set; }
    public int? Dpi { get; set; }
    public int? Monitor { get; set; }
}

/// <summary>
/// キーボードイベント (type: "key")。
/// Action: down, up
/// </summary>
public class RawKeyEvent : RawEvent
{
    public string? Action { get; set; }
    public int? Vk { get; set; }
    public string? Key { get; set; }
    public int? Scan { get; set; }
    public string? Char { get; set; }
    public string? Modifier { get; set; }
}
