using System.CommandLine;

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
        var processOption = new Option<string?>("--process", "プロセス名で対象指定");
        var hwndOption = new Option<string?>("--hwnd", "ウィンドウハンドル（0xHHHH形式）");

        // Mode options
        var onceOption = new Option<bool>("--once", "1回だけ撮影して終了");
        var intervalOption = new Option<int?>("--interval", "定期撮影間隔（ms）");
        var watchFileOption = new Option<string?>("--watch-file", "NDJSONファイルを監視してトリガー");
        var watchStdinOption = new Option<bool>("--watch-stdin", "stdinからのイベント行でトリガー");

        // Capture options
        var qualityOption = new Option<string>("--quality", () => "medium", "low|medium|high|full");
        var diffThresholdOption = new Option<double>("--diff-threshold", () => 2.0, "差分検知閾値パーセント");
        var noDiffOption = new Option<bool>("--no-diff", "差分検知を無効にする");

        // Output options
        var outDirOption = new Option<string>("--out-dir", () => "./screenshots", "スクリーンショット保存先");
        var outOption = new Option<string?>("--out", "メタデータNDJSON出力先（デフォルト: stdout）");

        rootCommand.AddOption(processOption);
        rootCommand.AddOption(hwndOption);
        rootCommand.AddOption(onceOption);
        rootCommand.AddOption(intervalOption);
        rootCommand.AddOption(watchFileOption);
        rootCommand.AddOption(watchStdinOption);
        rootCommand.AddOption(qualityOption);
        rootCommand.AddOption(diffThresholdOption);
        rootCommand.AddOption(noDiffOption);
        rootCommand.AddOption(outDirOption);
        rootCommand.AddOption(outOption);

        rootCommand.SetHandler(() =>
        {
            Console.Error.WriteLine("wfth-capture: 未実装（スタブ）");
            return Task.FromResult(0);
        });

        return await rootCommand.InvokeAsync(args);
    }
}
