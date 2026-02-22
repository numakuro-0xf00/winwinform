using System.CommandLine;
using System.CommandLine.Invocation;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Inspect.Helpers;

namespace WinFormsTestHarness.Inspect.Commands;

public static class WatchCommand
{
    public static Command Create()
    {
        var command = new Command("watch", "UIAツリーの変化を監視");

        var hwndOption = new Option<string?>(
            "--hwnd",
            description: "ウィンドウハンドル (0x形式)");

        var processOption = new Option<string?>(
            "--process",
            description: "プロセス名 (部分一致)");

        var intervalOption = new Option<int>(
            "--interval",
            getDefaultValue: () => 1000,
            description: "ポーリング間隔 (ミリ秒)");

        var backendOption = new Option<string>(
            "--backend",
            getDefaultValue: () => "flaui",
            description: "UIAバックエンド (flaui | swa)");

        command.AddOption(hwndOption);
        command.AddOption(processOption);
        command.AddOption(intervalOption);
        command.AddOption(backendOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var hwnd = ctx.ParseResult.GetValueForOption(hwndOption);
            var process = ctx.ParseResult.GetValueForOption(processOption);
            var interval = ctx.ParseResult.GetValueForOption(intervalOption);
            var backend = ctx.ParseResult.GetValueForOption(backendOption)!;
            ctx.ExitCode = await ExecuteAsync(hwnd, process, interval, backend);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(string? hwnd, string? process, int interval, string backend)
    {
        if (string.IsNullOrEmpty(hwnd) && string.IsNullOrEmpty(process))
        {
            Console.Error.WriteLine("Error: --hwnd または --process のいずれかを指定してください。");
            return ExitCodes.ArgumentError;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            using var inspector = InspectorFactory.Create(backend);
            var handle = HwndHelper.Resolve(hwnd, process, inspector);

            string? previousJson = null;

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var tree = inspector.GetTree(handle);
                    var currentJson = JsonHelper.Serialize(tree);

                    if (currentJson != previousJson)
                    {
                        Console.Out.WriteLine(currentJson);
                        previousJson = currentJson;
                    }

                    await Task.Delay(interval, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: {ex.Message}");
                    await Task.Delay(interval, cts.Token);
                }
            }

            return ExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.Success;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No window found"))
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.TargetNotFound;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.ArgumentError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.RuntimeError;
        }
    }
}
