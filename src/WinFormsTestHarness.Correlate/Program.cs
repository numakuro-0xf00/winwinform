using System.CommandLine;
using System.CommandLine.Invocation;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Correlate.Correlation;
using WinFormsTestHarness.Correlate.Readers;

var rootCommand = new RootCommand("wfth-correlate -- 時間窓相関CLI");

var uiaOption = new Option<string>(
    "--uia",
    description: "UIA変化 NDJSON（wfth-inspect watch 出力）")
{ IsRequired = true };

var appLogOption = new Option<string?>(
    "--app-log",
    description: "アプリ内ロガー NDJSON");

var screenshotsOption = new Option<string?>(
    "--screenshots",
    description: "スクリーンショットディレクトリ");

var windowOption = new Option<int>(
    "--window",
    getDefaultValue: () => 2000,
    description: "相関時間窓（ミリ秒）");

var includeNoiseOption = new Option<bool>(
    "--include-noise",
    getDefaultValue: () => false,
    description: "ノイズ判定された操作も出力");

var noiseThresholdOption = new Option<double>(
    "--noise-threshold",
    getDefaultValue: () => 0.7,
    description: "confidence >= n をノイズと判定");

var explainOption = new Option<bool>(
    "--explain",
    getDefaultValue: () => false,
    description: "各相関の判定根拠を注釈");

var debugOption = CommonOptions.Debug();
var quietOption = CommonOptions.Quiet();

rootCommand.AddOption(uiaOption);
rootCommand.AddOption(appLogOption);
rootCommand.AddOption(screenshotsOption);
rootCommand.AddOption(windowOption);
rootCommand.AddOption(includeNoiseOption);
rootCommand.AddOption(noiseThresholdOption);
rootCommand.AddOption(explainOption);
rootCommand.AddGlobalOption(debugOption);
rootCommand.AddGlobalOption(quietOption);

rootCommand.SetHandler((InvocationContext ctx) =>
{
    var uiaPath = ctx.ParseResult.GetValueForOption(uiaOption)!;
    var appLogPath = ctx.ParseResult.GetValueForOption(appLogOption);
    var screenshotsDir = ctx.ParseResult.GetValueForOption(screenshotsOption);
    var windowMs = ctx.ParseResult.GetValueForOption(windowOption);
    var includeNoise = ctx.ParseResult.GetValueForOption(includeNoiseOption);
    var noiseThreshold = ctx.ParseResult.GetValueForOption(noiseThresholdOption);
    var explain = ctx.ParseResult.GetValueForOption(explainOption);
    var debug = ctx.ParseResult.GetValueForOption(debugOption);
    var quiet = ctx.ParseResult.GetValueForOption(quietOption);

    var diag = new DiagnosticContext(debug, quiet);

    // Validate file existence
    if (!File.Exists(uiaPath))
    {
        DiagnosticContext.Error($"UIA ファイルが見つかりません: {uiaPath}");
        ctx.ExitCode = ExitCodes.ArgumentError;
        return;
    }

    if (appLogPath != null && !File.Exists(appLogPath))
    {
        DiagnosticContext.Error($"アプリログファイルが見つかりません: {appLogPath}");
        ctx.ExitCode = ExitCodes.ArgumentError;
        return;
    }

    if (screenshotsDir != null && !Directory.Exists(screenshotsDir))
    {
        DiagnosticContext.Error($"スクリーンショットディレクトリが見つかりません: {screenshotsDir}");
        ctx.ExitCode = ExitCodes.ArgumentError;
        return;
    }

    try
    {
        // Load data
        diag.DebugLog($"UIA スナップショット読み込み: {uiaPath}");
        var uiaSnapshots = UiaSnapshotReader.Read(uiaPath);
        diag.DebugLog($"UIA スナップショット数: {uiaSnapshots.Count}");

        var appLogEntries = appLogPath != null ? AppLogReader.Read(appLogPath) : null;
        if (appLogEntries != null)
            diag.DebugLog($"アプリログエントリ数: {appLogEntries.Count}");

        var screenshotIndex = screenshotsDir != null ? new ScreenshotIndex(screenshotsDir) : null;
        if (screenshotIndex != null)
            diag.DebugLog($"スクリーンショット数: {screenshotIndex.Count}");

        // Execute correlation
        var correlator = new TimeWindowCorrelator(
            uiaSnapshots, appLogEntries, screenshotIndex,
            windowMs, includeNoise, noiseThreshold, explain, diag);

        var stdin = NdJsonReader.FromStdin();
        using var stdout = NdJsonWriter.ToStdout();
        correlator.Execute(stdin, stdout);

        ctx.ExitCode = ExitCodes.Success;
    }
    catch (Exception ex)
    {
        DiagnosticContext.Error($"予期しないエラー: {ex.Message}");
        diag.DebugLog(ex.ToString());
        ctx.ExitCode = ExitCodes.RuntimeError;
    }
});

return await rootCommand.InvokeAsync(args);
