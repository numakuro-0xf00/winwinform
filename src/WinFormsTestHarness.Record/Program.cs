using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Windows;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Record;
using WinFormsTestHarness.Record.Hooks;

var rootCommand = new RootCommand("wfth-record -- 入力イベント記録CLI");

var processOption = CommonOptions.Process();
var hwndOption = CommonOptions.Hwnd();
var outOption = CommonOptions.Output();
var debugOption = CommonOptions.Debug();
var quietOption = CommonOptions.Quiet();

// Capture options
var captureOption = new Option<bool>("--capture", "キャプチャを有効にする");
var captureLevelOption = new Option<int>("--capture-level", () => 1, "キャプチャレベル (0:None, 1:AfterOnly, 2:BeforeAfter, 3:All)");
var captureQualityOption = new Option<string>("--capture-quality", () => "medium", "キャプチャ画質 (low|medium|high|full)");
var captureDirOption = new Option<string>("--capture-dir", () => "./screenshots", "スクリーンショット保存先");
var captureDelayOption = new Option<int>("--capture-delay", () => 300, "after 撮影待機時間 (ms)");
var diffThresholdOption = new Option<double>("--diff-threshold", () => 2.0, "差分検知閾値 (%)");

rootCommand.AddOption(processOption);
rootCommand.AddOption(hwndOption);
rootCommand.AddOption(outOption);
rootCommand.AddOption(captureOption);
rootCommand.AddOption(captureLevelOption);
rootCommand.AddOption(captureQualityOption);
rootCommand.AddOption(captureDirOption);
rootCommand.AddOption(captureDelayOption);
rootCommand.AddOption(diffThresholdOption);
rootCommand.AddGlobalOption(debugOption);
rootCommand.AddGlobalOption(quietOption);

rootCommand.SetHandler(async (InvocationContext ctx) =>
{
    var processName = ctx.ParseResult.GetValueForOption(processOption);
    var hwndHex = ctx.ParseResult.GetValueForOption(hwndOption);
    var outPath = ctx.ParseResult.GetValueForOption(outOption);
    var debug = ctx.ParseResult.GetValueForOption(debugOption);
    var quiet = ctx.ParseResult.GetValueForOption(quietOption);
    var captureEnabled = ctx.ParseResult.GetValueForOption(captureOption);
    var captureLevel = ctx.ParseResult.GetValueForOption(captureLevelOption);
    var captureQuality = ctx.ParseResult.GetValueForOption(captureQualityOption)!;
    var captureDir = ctx.ParseResult.GetValueForOption(captureDirOption)!;
    var captureDelay = ctx.ParseResult.GetValueForOption(captureDelayOption);
    var diffThreshold = ctx.ParseResult.GetValueForOption(diffThresholdOption);

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
        uint targetPid;

        if (hwndHex != null)
        {
            targetHwnd = HwndHelper.ParseHwnd(hwndHex);
            WinFormsTestHarness.Record.Hooks.NativeMethods.GetWindowThreadProcessId(targetHwnd, out targetPid);
            diag.DebugLog($"HWND 指定: {hwndHex} (PID: {targetPid})");
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
            targetPid = (uint)proc.Id;

            if (targetHwnd == IntPtr.Zero)
            {
                DiagnosticContext.Error($"メインウィンドウが見つかりません: {processName} (PID: {proc.Id})");
                ctx.ExitCode = ExitCodes.TargetNotFound;
                return;
            }

            diag.DebugLog($"プロセス指定: {processName} (PID: {targetPid}, HWND: 0x{targetHwnd.ToInt64():X8})");
        }

        // 出力先
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

        // キャプチャ設定
        CaptureSettings? captureSettings = null;
        if (captureEnabled)
        {
            var quality = Enum.TryParse<CaptureQuality>(captureQuality, ignoreCase: true, out var q)
                ? q
                : CaptureQuality.Medium;

            captureSettings = new CaptureSettings
            {
                Level = (CaptureLevel)captureLevel,
                Options = new CaptureOptions { Quality = quality },
                OutputDir = captureDir,
                AfterDelayMs = captureDelay,
                DiffThreshold = diffThreshold / 100.0,
            };
        }

        // 記録セッション実行
        using var session = new RecordingSession(
            targetHwnd, targetPid, processName, writer, diag,
            captureSettings: captureSettings);

        ctx.ExitCode = await session.RunAsync(cts.Token);
    }
    catch (ArgumentException ex)
    {
        DiagnosticContext.Error(ex.Message);
        ctx.ExitCode = ExitCodes.ArgumentError;
    }
    catch (InvalidOperationException ex)
    {
        DiagnosticContext.Error(ex.Message);
        ctx.ExitCode = ExitCodes.RuntimeError;
    }
    catch (Exception ex)
    {
        DiagnosticContext.Error($"予期しないエラー: {ex.Message}");
        diag.DebugLog(ex.ToString());
        ctx.ExitCode = ExitCodes.RuntimeError;
    }
});

return await rootCommand.InvokeAsync(args);
