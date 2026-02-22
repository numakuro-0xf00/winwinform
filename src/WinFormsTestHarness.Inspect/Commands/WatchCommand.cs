using System.CommandLine;
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

        command.SetHandler(Execute, hwndOption, processOption, intervalOption, backendOption);

        return command;
    }

    private static async Task Execute(string? hwnd, string? process, int interval, string backend)
    {
        if (string.IsNullOrEmpty(hwnd) && string.IsNullOrEmpty(process))
        {
            Console.Error.WriteLine("Error: --hwnd または --process のいずれかを指定してください。");
            return;
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
        }
        catch (OperationCanceledException)
        {
            // Normal exit via Ctrl+C
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}
