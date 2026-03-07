using System.CommandLine;
using System.CommandLine.Invocation;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;

var rootCommand = new RootCommand("wfth-aggregate -- 生イベント集約CLI");

var textTimeoutOption = new Option<int>(
    "--text-timeout",
    getDefaultValue: () => 500,
    description: "キー入力集約タイムアウト（ミリ秒）");

var clickTimeoutOption = new Option<int>(
    "--click-timeout",
    getDefaultValue: () => 300,
    description: "Click判定タイムアウト（ミリ秒）");

var dblclickTimeoutOption = new Option<int>(
    "--dblclick-timeout",
    getDefaultValue: () => 500,
    description: "DoubleClick判定タイムアウト（ミリ秒）");

var debugOption = CommonOptions.Debug();
var quietOption = CommonOptions.Quiet();

rootCommand.AddOption(textTimeoutOption);
rootCommand.AddOption(clickTimeoutOption);
rootCommand.AddOption(dblclickTimeoutOption);
rootCommand.AddGlobalOption(debugOption);
rootCommand.AddGlobalOption(quietOption);

rootCommand.SetHandler((InvocationContext ctx) =>
{
    var textTimeout = ctx.ParseResult.GetValueForOption(textTimeoutOption);
    var clickTimeout = ctx.ParseResult.GetValueForOption(clickTimeoutOption);
    var dblclickTimeout = ctx.ParseResult.GetValueForOption(dblclickTimeoutOption);
    var debug = ctx.ParseResult.GetValueForOption(debugOption);
    var quiet = ctx.ParseResult.GetValueForOption(quietOption);

    var diag = new DiagnosticContext(debug, quiet);

    try
    {
        using var writer = NdJsonWriter.ToStdout();

        var builder = new ActionBuilder(
            Console.In, writer, diag,
            clickTimeoutMs: clickTimeout,
            dblclickTimeoutMs: dblclickTimeout,
            textTimeoutMs: textTimeout);

        ctx.ExitCode = builder.Run();
    }
    catch (Exception ex)
    {
        DiagnosticContext.Error($"予期しないエラー: {ex.Message}");
        diag.DebugLog(ex.ToString());
        ctx.ExitCode = ExitCodes.RuntimeError;
    }
});

return await rootCommand.InvokeAsync(args);
