namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// システムイベント。
/// フック状態変化、キュー劣化、対象アプリ状態等の診断情報を記録する。
/// </summary>
public class SystemEvent : InputEvent
{
    public override string Type => "system";

    /// <summary>システムイベント種別（"hook_health", "queue_degradation", "app_health"）</summary>
    public string Action { get; set; } = "";

    /// <summary>詳細メッセージ</summary>
    public string? Message { get; set; }

    /// <summary>追加データ（JSON シリアライズ可能な任意オブジェクト）</summary>
    public object? Data { get; set; }
}
