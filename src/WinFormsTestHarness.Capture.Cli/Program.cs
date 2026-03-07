using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Common.Windows;
using WinFormsTestHarness.Capture;

namespace WinFormsTestHarness.Capture.Cli;

/// <summary>
/// wfth-capture CLI エントリーポイント。
/// スクリーンショットキャプチャのスタンドアロンCLIラッパー。
/// </summary>
internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("wfth-capture — スクリーンショットキャプチャCLI");

        // Target options
        var processOption = CommonOptions.Process();
        var hwndOption = CommonOptions.Hwnd();

        // Mode options
        var onceOption = new Option<bool>("--once", "1回だけ撮影して終了");
        var intervalOption = new Option<int?>("--interval", "定期撮影間隔（ms）");

        // Capture options
        var qualityOption = new Option<string>("--quality", () => "medium", "low|medium|high|full");
        var diffThresholdOption = new Option<double>("--diff-threshold", () => 2.0, "差分検知閾値パーセント");
        var noDiffOption = new Option<bool>("--no-diff", "差分検知を無効にする");

        // Output options
        var outDirOption = new Option<string>("--out-dir", () => "./screenshots", "スクリーンショット保存先");
        var outOption = CommonOptions.Output();

        // Debug options
        var debugOption = CommonOptions.Debug();
        var quietOption = CommonOptions.Quiet();

        rootCommand.AddOption(processOption);
        rootCommand.AddOption(hwndOption);
        rootCommand.AddOption(onceOption);
        rootCommand.AddOption(intervalOption);
        rootCommand.AddOption(qualityOption);
        rootCommand.AddOption(diffThresholdOption);
        rootCommand.AddOption(noDiffOption);
        rootCommand.AddOption(outDirOption);
        rootCommand.AddOption(outOption);
        rootCommand.AddGlobalOption(debugOption);
        rootCommand.AddGlobalOption(quietOption);

        rootCommand.SetHandler(async (InvocationContext ctx) =>
        {
            var processName = ctx.ParseResult.GetValueForOption(processOption);
            var hwndHex = ctx.ParseResult.GetValueForOption(hwndOption);
            var once = ctx.ParseResult.GetValueForOption(onceOption);
            var interval = ctx.ParseResult.GetValueForOption(intervalOption);
            var quality = ctx.ParseResult.GetValueForOption(qualityOption)!;
            var diffThreshold = ctx.ParseResult.GetValueForOption(diffThresholdOption);
            var noDiff = ctx.ParseResult.GetValueForOption(noDiffOption);
            var outDir = ctx.ParseResult.GetValueForOption(outDirOption)!;
            var outPath = ctx.ParseResult.GetValueForOption(outOption);
            var debug = ctx.ParseResult.GetValueForOption(debugOption);
            var quiet = ctx.ParseResult.GetValueForOption(quietOption);

            var diag = new DiagnosticContext(debug, quiet);

            if (processName == null && hwndHex == null)
            {
                DiagnosticContext.Error("--process または --hwnd のいずれかを指定してください");
                ctx.ExitCode = ExitCodes.ArgumentError;
                return;
            }

            try
            {
                // ウィンドウ解決
                IntPtr targetHwnd;
                if (hwndHex != null)
                {
                    targetHwnd = HwndHelper.ParseHwnd(hwndHex);
                    diag.DebugLog($"HWND 指定: {hwndHex}");
                }
                else
                {
                    var processes = Process.GetProcessesByName(processName!);
                    if (processes.Length == 0)
                    {
                        DiagnosticContext.Error($"プロセスが見つかりません: {processName}");
                        ctx.ExitCode = ExitCodes.TargetNotFound;
                        return;
                    }

                    var proc = processes[0];
                    targetHwnd = proc.MainWindowHandle;

                    if (targetHwnd == IntPtr.Zero)
                    {
                        DiagnosticContext.Error($"メインウィンドウが見つかりません: {processName} (PID: {proc.Id})");
                        ctx.ExitCode = ExitCodes.TargetNotFound;
                        return;
                    }

                    diag.DebugLog($"プロセス指定: {processName} (HWND: 0x{targetHwnd.ToInt64():X8})");
                }

                // キャプチャ設定
                var captureQuality = Enum.TryParse<CaptureQuality>(quality, ignoreCase: true, out var q)
                    ? q
                    : CaptureQuality.Medium;

                var options = new CaptureOptions { Quality = captureQuality };
                var capturer = new ScreenCapturer(targetHwnd, options);
                var diffDetector = new DiffDetector(diffThreshold / 100.0);
                var fileWriter = new CaptureFileWriter(outDir);

                using var writer = outPath != null
                    ? NdJsonWriter.ToFile(outPath)
                    : NdJsonWriter.ToStdout();

                // Ctrl+C ハンドリング
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                if (once)
                {
                    // 1回キャプチャモード
                    var result = capturer.Capture("cli");
                    if (result.Bitmap != null)
                    {
                        if (!noDiff)
                        {
                            var diffRatio = diffDetector.CalculateDiffRatio(result.Bitmap);
                            result.DiffRatio = diffRatio < 0 ? 0.0 : diffRatio;
                        }
                        fileWriter.Save(result, "once");
                    }

                    var evt = ToScreenshotEvent(result, "once", null);
                    writer.Write(evt);
                    result.Dispose();

                    diag.Info($"キャプチャ完了: {result.FilePath}");
                    ctx.ExitCode = ExitCodes.Success;
                }
                else if (interval.HasValue)
                {
                    // 定期撮影モード
                    diag.Info($"定期撮影開始 (間隔: {interval.Value}ms, Ctrl+C で停止)");
                    var seq = 0;

                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            var result = capturer.Capture("interval");
                            var skipped = false;

                            if (result.Bitmap != null && !noDiff)
                            {
                                var diffRatio = diffDetector.CalculateDiffRatio(result.Bitmap);
                                result.DiffRatio = diffRatio < 0 ? 0.0 : diffRatio;

                                if (!diffDetector.IsChanged(diffRatio))
                                {
                                    skipped = true;
                                    result.Skipped = true;
                                }
                            }

                            if (!skipped && result.Bitmap != null)
                            {
                                fileWriter.Save(result, $"interval_{seq:D4}");
                                seq++;
                            }

                            var evt = ToScreenshotEvent(result, "interval", null);
                            writer.Write(evt);
                            result.Dispose();

                            await Task.Delay(interval.Value, cts.Token);
                        }
                    }
                    catch (OperationCanceledException) { }

                    diag.Info($"定期撮影停止 (撮影数: {seq})");
                    ctx.ExitCode = ExitCodes.Success;
                }
                else
                {
                    DiagnosticContext.Error("--once または --interval のいずれかを指定してください");
                    ctx.ExitCode = ExitCodes.ArgumentError;
                }

                capturer.Dispose();
            }
            catch (ArgumentException ex)
            {
                DiagnosticContext.Error(ex.Message);
                ctx.ExitCode = ExitCodes.ArgumentError;
            }
            catch (Exception ex)
            {
                DiagnosticContext.Error($"予期しないエラー: {ex.Message}");
                diag.DebugLog(ex.ToString());
                ctx.ExitCode = ExitCodes.RuntimeError;
            }
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static ScreenshotEvent ToScreenshotEvent(CaptureResult result, string timing, string? trigger)
    {
        return new ScreenshotEvent
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
    }
}
