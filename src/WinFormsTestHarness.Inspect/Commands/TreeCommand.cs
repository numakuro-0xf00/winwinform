using System.CommandLine;
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

        command.SetHandler(Execute, hwndOption, processOption, depthOption, backendOption);

        return command;
    }

    private static void Execute(string? hwnd, string? process, int? depth, string backend)
    {
        if (string.IsNullOrEmpty(hwnd) && string.IsNullOrEmpty(process))
        {
            Console.Error.WriteLine("Error: --hwnd または --process のいずれかを指定してください。");
            return;
        }

        try
        {
            using var inspector = InspectorFactory.Create(backend);
            var handle = HwndHelper.Resolve(hwnd, process, inspector);
            var tree = inspector.GetTree(handle, depth);
            Console.Out.WriteLine(JsonHelper.Serialize(tree));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}
