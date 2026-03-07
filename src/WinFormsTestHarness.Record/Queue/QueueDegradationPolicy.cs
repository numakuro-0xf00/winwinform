using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Record.Queue;

/// <summary>
/// キュー劣化ポリシー。キュー使用率に基づいてイベントドロップを制御する。
/// </summary>
public class QueueDegradationPolicy
{
    private readonly int _capacity;

    public QueueDegradationPolicy(int capacity)
    {
        _capacity = capacity;
    }

    /// <summary>
    /// 現在のキュー使用率から劣化モードを判定する。
    /// </summary>
    public DegradationMode Evaluate(int currentCount)
    {
        var ratio = (double)currentCount / _capacity;
        return ratio switch
        {
            < 0.5 => DegradationMode.Normal,
            < 0.75 => DegradationMode.DropMouseMove,
            < 0.9 => DegradationMode.DropMouse,
            _ => DegradationMode.CriticalOnly,
        };
    }

    /// <summary>
    /// イベントのカテゴリを分類する。
    /// </summary>
    public static EventCategory Classify(InputEvent evt)
    {
        return evt switch
        {
            MouseEvent m when m.Action == "move" => EventCategory.MouseMove,
            MouseEvent => EventCategory.MouseAction,
            KeyEvent => EventCategory.Key,
            WindowEvent => EventCategory.Window,
            SessionEvent => EventCategory.Session,
            SystemEvent => EventCategory.System,
            _ => EventCategory.System,
        };
    }

    /// <summary>
    /// 現在の劣化モードでイベントを受け入れるか判定する。
    /// </summary>
    public bool ShouldAccept(InputEvent evt, int currentCount)
    {
        var mode = Evaluate(currentCount);
        var category = Classify(evt);

        return mode switch
        {
            DegradationMode.Normal => true,
            DegradationMode.DropMouseMove => category != EventCategory.MouseMove,
            DegradationMode.DropMouse => category != EventCategory.MouseMove && category != EventCategory.MouseAction,
            DegradationMode.CriticalOnly => category is EventCategory.Session or EventCategory.Key or EventCategory.Window,
            _ => true,
        };
    }
}

/// <summary>キュー劣化モード</summary>
public enum DegradationMode
{
    /// <summary>通常（すべてのイベントを受け入れ）</summary>
    Normal,

    /// <summary>マウス移動をドロップ</summary>
    DropMouseMove,

    /// <summary>マウスイベント全般をドロップ</summary>
    DropMouse,

    /// <summary>重要イベントのみ（Session, Key, Window）</summary>
    CriticalOnly,
}
