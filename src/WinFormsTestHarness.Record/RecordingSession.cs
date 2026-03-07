using System.Diagnostics;
using System.Text.Json;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Common.Timing;
using WinFormsTestHarness.Record.Events;
using WinFormsTestHarness.Record.Hooks;
using WinFormsTestHarness.Record.Monitoring;
using WinFormsTestHarness.Record.Queue;

namespace WinFormsTestHarness.Record;

/// <summary>
/// 記録セッションのオーケストレータ。
/// フック設定→イベントキュー→NDJSON 出力の全体フローを管理する。
/// </summary>
public class RecordingSession : IDisposable
{
    private readonly IntPtr _targetHwnd;
    private readonly uint _targetPid;
    private readonly string? _targetProcess;
    private readonly NdJsonWriter _writer;
    private readonly DiagnosticContext _diag;
    private readonly PreciseTimestamp _timestamp;

    private readonly IWindowApi _windowApi;
    private readonly WindowTracker _tracker;
    private readonly EventQueue _queue;
    private readonly IMouseHook _mouseHook;
    private readonly IKeyboardHook _keyboardHook;
    private readonly HookHealthMonitor _hookHealth;
    private readonly AppHealthMonitor _appHealth;

    private readonly CaptureStrategy? _captureStrategy;
    private readonly ScreenCapturer? _screenCapturer;

    private long _seq;
    private CancellationTokenSource? _cts;
    private Task? _writerTask;

    public RecordingSession(
        IntPtr targetHwnd,
        uint targetPid,
        string? targetProcess,
        NdJsonWriter writer,
        DiagnosticContext diag,
        CaptureSettings? captureSettings = null)
    {
        _targetHwnd = targetHwnd;
        _targetPid = targetPid;
        _targetProcess = targetProcess;
        _writer = writer;
        _diag = diag;
        _timestamp = new PreciseTimestamp();

        _windowApi = new Win32WindowApi();
        _tracker = new WindowTracker(_windowApi, targetHwnd, targetPid);
        _queue = new EventQueue();

        _mouseHook = new MouseHook(_tracker, _windowApi, _timestamp);
        _keyboardHook = new KeyboardHook(_tracker, _windowApi, _timestamp);

        var clock = new SystemClock();
        var probe = new ProbeInput();
        _hookHealth = new HookHealthMonitor(clock, probe);
        _appHealth = new AppHealthMonitor(
            new Win32AppHealthApi(),
            targetPid,
            targetHwnd);

        if (captureSettings != null && captureSettings.Level != CaptureLevel.None)
        {
            _screenCapturer = new ScreenCapturer(targetHwnd, captureSettings.Options);
            var diffDetector = new DiffDetector(captureSettings.DiffThreshold);
            var fileWriter = new CaptureFileWriter(captureSettings.OutputDir);
            _captureStrategy = new CaptureStrategy(
                _screenCapturer, diffDetector, fileWriter,
                captureSettings.Level, captureSettings.AfterDelayMs);
        }
    }

    /// <summary>
    /// 記録セッションを開始する。
    /// Ctrl+C で停止するまでイベントを記録し続ける。
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // セッション開始イベント
            EmitSessionStart();

            // フック設定
            _mouseHook.OnMouseEvent += OnMouseEvent;
            _keyboardHook.OnKeyEvent += OnKeyEvent;

            _mouseHook.Install();
            _diag.DebugLog("マウスフック設定完了");

            _keyboardHook.Install();
            _diag.DebugLog("キーボードフック設定完了");

            _diag.Info("記録開始... (Ctrl+C で停止)");

            // ライタースレッド起動
            _writerTask = Task.Run(() => WriterLoop(_cts.Token), _cts.Token);

            // メッセージポンプ（フック維持に必要）
            var appContext = new System.Windows.Forms.ApplicationContext();

            // Ctrl+C ハンドリング: メッセージポンプを終了
            _cts.Token.Register(() =>
            {
                appContext.ExitThread();
            });

            // 定期監視タスク
            _ = Task.Run(() => MonitorLoop(_cts.Token), _cts.Token);

            System.Windows.Forms.Application.Run(appContext);

            // 停止処理
            return await StopAsync("user_cancel");
        }
        catch (Exception ex)
        {
            _diag.DebugLog($"記録エラー: {ex.Message}");
            try { return await StopAsync("error"); }
            catch { return ExitCodes.RuntimeError; }
        }
    }

    private async Task<int> StopAsync(string reason)
    {
        _mouseHook.Uninstall();
        _keyboardHook.Uninstall();
        _diag.DebugLog("フック解除完了");

        _queue.Complete();

        if (_writerTask != null)
        {
            try { await _writerTask; }
            catch (OperationCanceledException) { }
        }

        EmitSessionStop(reason);

        _diag.Info($"記録停止 (理由: {reason}, イベント数: {_seq})");
        return ExitCodes.Success;
    }

    private void OnMouseEvent(MouseEvent evt)
    {
        evt.Seq = Interlocked.Increment(ref _seq);
        _queue.TryWrite(evt);
        _hookHealth.RecordActivity();
    }

    private void OnKeyEvent(KeyEvent evt)
    {
        evt.Seq = Interlocked.Increment(ref _seq);
        _queue.TryWrite(evt);
        _hookHealth.RecordActivity();
    }

    private async Task WriterLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _queue.ReadAllAsync(ct))
            {
                var isTrigger = IsCaptureTriggeredBy(evt);
                var triggerDesc = GetTriggerDescription(evt);

                // before キャプチャ
                if (isTrigger && _captureStrategy != null)
                {
                    var before = _captureStrategy.CaptureBeforeInput(triggerDesc);
                    if (before != null)
                    {
                        EmitScreenshotEvent(before, "before", triggerDesc);
                        before.Dispose();
                    }
                }

                // 入力イベント出力
                _writer.Write(JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options));

                // after キャプチャ
                if (isTrigger && _captureStrategy != null)
                {
                    var after = await _captureStrategy.CaptureAfterInputAsync(triggerDesc);
                    if (after != null)
                    {
                        EmitScreenshotEvent(after, "after", triggerDesc);
                        after.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool IsCaptureTriggeredBy(InputEvent evt)
    {
        return evt switch
        {
            MouseEvent m => m.Action is "down" or "up",
            KeyEvent k => k.Action == "down",
            _ => false,
        };
    }

    private static string GetTriggerDescription(InputEvent evt)
    {
        return evt switch
        {
            MouseEvent m => $"mouse_{m.Action}_{m.Button}",
            KeyEvent k => $"key_{k.Action}_{k.KeyName}",
            _ => "unknown",
        };
    }

    private void EmitScreenshotEvent(CaptureResult result, string timing, string trigger)
    {
        var ssEvt = new ScreenshotEvent
        {
            Timestamp = result.Timestamp,
            Timing = timing,
            File = result.Skipped ? null : result.FilePath,
            Width = result.Width,
            Height = result.Height,
            FileSize = result.FileSize,
            DiffRatio = result.DiffRatio > 0 ? result.DiffRatio : null,
            Skipped = result.Skipped ? true : null,
            Trigger = trigger,
            ReuseFrom = result.ReuseFrom,
        };
        _writer.Write(ssEvt);
    }

    private async Task MonitorLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);

                // アプリ状態チェック
                var appStatus = _appHealth.Check();
                if (appStatus == AppStatus.Exited)
                {
                    _diag.Info("対象プロセスが終了しました");
                    _cts?.Cancel();
                    return;
                }
                if (appStatus == AppStatus.Hung)
                {
                    _diag.Warn("対象アプリが応答していません");
                }

                // フック状態チェック
                var hookStatus = _hookHealth.Check();
                if (hookStatus == HookStatus.PossiblyDead)
                {
                    _diag.Warn("フックが応答していません。再設定を試みます...");
                    try
                    {
                        _mouseHook.Uninstall();
                        _mouseHook.Install();
                        _keyboardHook.Uninstall();
                        _keyboardHook.Install();
                        _diag.Info("フック再設定完了");
                    }
                    catch (Exception ex)
                    {
                        _diag.DebugLog($"フック再設定失敗: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void EmitSessionStart()
    {
        var evt = new SessionEvent
        {
            Timestamp = _timestamp.Now(),
            Seq = 0,
            Action = "start",
            TargetProcess = _targetProcess,
            TargetHwnd = $"0x{_targetHwnd.ToInt64():X8}",
            Monitors = GetMonitorConfigs(),
        };
        _writer.Write(JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options));
    }

    private void EmitSessionStop(string reason)
    {
        var dropStats = _queue.GetAndResetDropStats();
        var evt = new SessionEvent
        {
            Timestamp = _timestamp.Now(),
            Seq = Interlocked.Increment(ref _seq),
            Action = "stop",
            TotalEvents = _seq,
            Dropped = dropStats,
            Reason = reason,
        };
        _writer.Write(JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options));
    }

    private static List<MonitorConfig> GetMonitorConfigs()
    {
        var monitors = new List<MonitorConfig>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            monitors.Add(new MonitorConfig
            {
                Name = screen.DeviceName,
                IsPrimary = screen.Primary,
                Bounds = new WindowRect(
                    screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height),
            });
        }
        return monitors;
    }

    public void Dispose()
    {
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        _screenCapturer?.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>
/// Win32 API を使用する IAppHealthApi の実装。
/// </summary>
internal class Win32AppHealthApi : IAppHealthApi
{
    public bool IsProcessAlive(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public bool IsWindowResponsive(IntPtr hwnd)
    {
        return Hooks.NativeMethods.SendMessageTimeout(
            hwnd,
            Hooks.NativeMethods.WM_NULL,
            IntPtr.Zero,
            IntPtr.Zero,
            Hooks.NativeMethods.SMTO_ABORTIFHUNG,
            1000,
            out _);
    }
}
