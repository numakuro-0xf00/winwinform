namespace WinFormsTestHarness.Logger;

/// <summary>
/// Logger の動作設定。全プロパティにデフォルト値を持つ。
/// </summary>
public sealed class LoggerConfig
{
    /// <summary>Recording Engine の PID（IPC 接続先）。null の場合は環境変数から取得</summary>
    public int? RecordingEnginePid { get; set; }

    /// <summary>パイプ接続タイムアウト (ms)</summary>
    public int PipeConnectTimeoutMs { get; set; } = 3000;

    /// <summary>バックグラウンドキューの最大サイズ</summary>
    public int MaxQueueSize { get; set; } = 10000;

    /// <summary>フラッシュ間隔 (ms)</summary>
    public int FlushIntervalMs { get; set; } = 100;

    /// <summary>フォールバックファイルパス。null の場合は %TEMP% に自動生成</summary>
    public string? FallbackFilePath { get; set; }

    /// <summary>フォールバックファイルの最大サイズ (bytes)</summary>
    public long MaxFallbackFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB

    /// <summary>IPC 再接続間隔 (ms)</summary>
    public int ReconnectIntervalMs { get; set; } = 5000;

    /// <summary>IPC 最大再接続試行回数</summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>コントロールの自動監視を有効にするか</summary>
    public bool AutoWatchControls { get; set; } = true;

    /// <summary>フォームの自動追跡を有効にするか</summary>
    public bool TrackForms { get; set; } = true;

    /// <summary>コントロールツリー走査の最大深度</summary>
    public int MaxControlDepth { get; set; } = 20;

    /// <summary>デフォルト設定</summary>
    public static LoggerConfig Default => new();
}
