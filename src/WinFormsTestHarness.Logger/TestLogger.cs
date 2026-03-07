using WinFormsTestHarness.Logger.Internal;
using WinFormsTestHarness.Logger.Models;
using WinFormsTestHarness.Logger.Sinks;

namespace WinFormsTestHarness.Logger;

/// <summary>
/// テストロガーのエントリーポイント。
/// 全メソッド本体は #if E2E_TEST で囲まれ、シンボル未定義ビルドでは空メソッドとなる。
/// </summary>
public static class TestLogger
{
#if E2E_TEST
    private static LogPipeline? s_pipeline;
    private static FormTracker? s_formTracker;
    private static ControlWatcher? s_controlWatcher;
    private static PreciseTimestamp? s_timestamp;
    private static bool s_attached;
#endif

    /// <summary>
    /// ロガーをアタッチする。2回目以降の呼び出しは no-op。
    /// </summary>
    public static void Attach(LoggerConfig? config = null)
    {
#if E2E_TEST
        if (s_attached) return;

        try
        {
            config ??= LoggerConfig.Default;
            s_timestamp = new PreciseTimestamp();

            ILogSink primarySink = new IpcLogSink(config);
            ILogSink fallbackSink = new JsonFileLogSink(config.FallbackFilePath, config.MaxFallbackFileSizeBytes);

            // IPC 未接続の場合はファイル Sink をプライマリに
            if (!primarySink.IsConnected)
            {
                primarySink.Dispose();
                primarySink = fallbackSink;
                fallbackSink = new JsonFileLogSink(null, config.MaxFallbackFileSizeBytes);
            }

            s_pipeline = new LogPipeline(primarySink, fallbackSink, config.MaxQueueSize, config.FlushIntervalMs);
            s_controlWatcher = new ControlWatcher(s_pipeline, s_timestamp, config.MaxControlDepth);

            if (config.TrackForms)
            {
                s_formTracker = new FormTracker(s_pipeline, s_controlWatcher, s_timestamp);
                s_formTracker.Start();
            }

            s_attached = true;
        }
        catch
        {
            // No-Throw 保証: ロガーはホストアプリをクラッシュさせない
        }
#endif
    }

    /// <summary>手動イベント記録</summary>
    public static void LogEvent(string controlName, string eventName, object? value = null)
    {
#if E2E_TEST
        if (!s_attached || s_pipeline == null || s_timestamp == null) return;
        try
        {
            var info = new ControlInfo(controlName, "Manual", null, false);
            var entry = LogEntry.EventEntry(info, eventName, s_timestamp.Now());
            if (value != null)
                entry.Text = LogEntry.Sanitize(value)?.ToString();
            s_pipeline.Enqueue(entry);
        }
        catch
        {
            // No-Throw
        }
#endif
    }

    /// <summary>手動プロパティ変更記録</summary>
    public static void LogPropertyChanged(string controlName, string propertyName, object? oldValue, object? newValue)
    {
#if E2E_TEST
        if (!s_attached || s_pipeline == null || s_timestamp == null) return;
        try
        {
            var info = new ControlInfo(controlName, "Manual", null, false);
            s_pipeline.Enqueue(LogEntry.PropertyChanged(info, propertyName, oldValue, newValue, false, s_timestamp.Now()));
        }
        catch
        {
            // No-Throw
        }
#endif
    }

    /// <summary>カスタムメッセージ記録</summary>
    public static void Log(string message)
    {
#if E2E_TEST
        if (!s_attached || s_pipeline == null || s_timestamp == null) return;
        try
        {
            s_pipeline.Enqueue(LogEntry.Custom(message, s_timestamp.Now()));
        }
        catch
        {
            // No-Throw
        }
#endif
    }

    /// <summary>ロガーをデタッチする</summary>
    public static void Detach()
    {
#if E2E_TEST
        if (!s_attached) return;
        try
        {
            s_formTracker?.Dispose();
            s_controlWatcher?.Dispose();
            s_pipeline?.Dispose();
        }
        catch
        {
            // No-Throw
        }
        finally
        {
            s_formTracker = null;
            s_controlWatcher = null;
            s_pipeline = null;
            s_timestamp = null;
            s_attached = false;
        }
#endif
    }
}
