namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// 全入力イベントの抽象基底クラス。
/// NDJSON 出力時に type フィールドでイベント種別を識別する。
/// </summary>
public abstract class InputEvent
{
    /// <summary>イベント種別（"mouse", "key", "window", "session", "system"）</summary>
    public abstract string Type { get; }

    /// <summary>ISO 8601 UTC タイムスタンプ</summary>
    public string Timestamp { get; set; } = "";

    /// <summary>セッション内の連番</summary>
    public long Seq { get; set; }
}
