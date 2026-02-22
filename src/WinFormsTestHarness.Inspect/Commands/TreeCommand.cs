using System.CommandLine;
using System.CommandLine.Invocation;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Inspect.Helpers;

namespace WinFormsTestHarness.Inspect.Commands;

public static class TreeCommand
{
    public static Command Create()
    {
        var command = new Command("tree", "ウィンドウのUIAツリーを表示");

        var hwndOption = new Option<string?>(
            "--hwnd",
            description: "ウィンドウハンドル (0x形式)");

        var processOption = new Option<string?>(
            "--process",
            description: "プロセス名 (部分一致)");

        var depthOption = new Option<int?>(
            "--depth",
            description: "最大探索深度");

        var backendOption = new Option<string>(
            "--backend",
            getDefaultValue: () => "flaui",
            description: "UIAバックエンド (flaui | swa)");

        command.AddOption(hwndOption);
        command.AddOption(processOption);
        command.AddOption(depthOption);
        command.AddOption(backendOption);

        command.SetHandler((InvocationContext ctx) =>
        {
            var hwnd = ctx.ParseResult.GetValueForOption(hwndOption);
            var process = ctx.ParseResult.GetValueForOption(processOption);
            var depth = ctx.ParseResult.GetValueForOption(depthOption);
            var backend = ctx.ParseResult.GetValueForOption(backendOption)!;
            ctx.ExitCode = Execute(hwnd, process, depth, backend);
        });

        return command;
    }

    private static int Execute(string? hwnd, string? process, int? depth, string backend)
    {
        if (string.IsNullOrEmpty(hwnd) && string.IsNullOrEmpty(process))
        {
            Console.Error.WriteLine("Error: --hwnd または --process のいずれかを指定してください。");
            return ExitCodes.ArgumentError;
        }

        try
        {
            using var inspector = InspectorFactory.Create(backend);
            var handle = HwndHelper.Resolve(hwnd, process, inspector);
            var tree = inspector.GetTree(handle, depth);
            Console.Out.WriteLine(JsonHelper.Serialize(tree));
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
