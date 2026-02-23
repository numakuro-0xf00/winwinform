using System.CommandLine;
using System.CommandLine.Invocation;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Common.Cli;

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

var rootCommand = new RootCommand("wfth-aggregate — 生イベント集約（MouseDown+Up → Click, キー列 → TextInput 等）")
{
    textTimeoutOption,
    clickTimeoutOption,
    dblclickTimeoutOption,
    debugOption,
    quietOption,
};

rootCommand.SetHandler((InvocationContext context) =>
{
    var textTimeout = context.ParseResult.GetValueForOption(textTimeoutOption);
    var clickTimeout = context.ParseResult.GetValueForOption(clickTimeoutOption);
    var dblclickTimeout = context.ParseResult.GetValueForOption(dblclickTimeoutOption);
    var debug = context.ParseResult.GetValueForOption(debugOption);
    var quiet = context.ParseResult.GetValueForOption(quietOption);

    var diag = new DiagnosticContext(debug, quiet);
    diag.DebugLog($"text-timeout={textTimeout}ms, click-timeout={clickTimeout}ms, dblclick-timeout={dblclickTimeout}ms");

    if (Console.Out is StreamWriter sw)
        sw.AutoFlush = true;

    var builder = new ActionBuilder(
        clickTimeoutMs: clickTimeout,
        dblclickTimeoutMs: dblclickTimeout,
        textTimeoutMs: textTimeout,
        diag: diag);

    try
    {
        builder.Process(Console.In, Console.Out);
        context.ExitCode = ExitCodes.Success;
    }
    catch (Exception ex)
    {
        DiagnosticContext.Error($"wfth-aggregate: {ex.Message}");
        diag.DebugLog(ex.ToString());
        context.ExitCode = ExitCodes.RuntimeError;
    }
});

return await rootCommand.InvokeAsync(args);
