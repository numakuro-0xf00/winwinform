using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Windows;
using WinFormsTestHarness.Record;
using WinFormsTestHarness.Record.Hooks;

var rootCommand = new RootCommand("wfth-record -- 入力イベント記録CLI");

var processOption = CommonOptions.Process();
var hwndOption = CommonOptions.Hwnd();
var outOption = CommonOptions.Output();
var debugOption = CommonOptions.Debug();
var quietOption = CommonOptions.Quiet();

rootCommand.AddOption(processOption);
rootCommand.AddOption(hwndOption);
rootCommand.AddOption(outOption);
rootCommand.AddGlobalOption(debugOption);
rootCommand.AddGlobalOption(quietOption);

rootCommand.SetHandler(async (InvocationContext ctx) =>
{
    var processName = ctx.ParseResult.GetValueForOption(processOption);
    var hwndHex = ctx.ParseResult.GetValueForOption(hwndOption);
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

        // 記録セッション実行
        using var session = new RecordingSession(
            targetHwnd, targetPid, processName, writer, diag);

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
