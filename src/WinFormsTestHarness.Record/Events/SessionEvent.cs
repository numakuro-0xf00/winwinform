namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// セッション制御イベント。
/// 記録セッションの開始・終了を示す。
/// </summary>
public class SessionEvent : InputEvent
{
    public override string Type => "session";

    /// <summary>セッションアクション（"start", "stop"）</summary>
    public string Action { get; set; } = "";

    /// <summary>セッション開始時のみ: 記録対象プロセス名</summary>
    public string? TargetProcess { get; set; }

    /// <summary>セッション開始時のみ: 記録対象ウィンドウハンドル</summary>
    public string? TargetHwnd { get; set; }

    /// <summary>セッション開始時のみ: モニター構成</summary>
    public List<MonitorConfig>? Monitors { get; set; }

    /// <summary>セッション終了時のみ: 記録したイベント総数</summary>
    public long? TotalEvents { get; set; }

    /// <summary>セッション終了時のみ: ドロップ統計</summary>
    public DropStats? Dropped { get; set; }

    /// <summary>セッション終了時のみ: 終了理由（"user_cancel", "target_exit", "error"）</summary>
    public string? Reason { get; set; }
}

/// <summary>ドロップ統計</summary>
public record DropStats(long Mouse, long Key, long Window, long System);
