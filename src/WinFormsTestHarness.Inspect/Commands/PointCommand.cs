using System.CommandLine;
using WinFormsTestHarness.Inspect.Helpers;

namespace WinFormsTestHarness.Inspect.Commands;

public static class PointCommand
{
    public static Command Create()
    {
        var command = new Command("point", "指定座標のUI要素を取得");

        var hwndOption = new Option<string?>(
            "--hwnd",
            description: "ウィンドウハンドル (0x形式)");

        var processOption = new Option<string?>(
            "--process",
            description: "プロセス名 (部分一致)");

        var xOption = new Option<int>(
            "--x",
            description: "X座標") { IsRequired = true };

        var yOption = new Option<int>(
            "--y",
            description: "Y座標") { IsRequired = true };

        var backendOption = new Option<string>(
            "--backend",
            getDefaultValue: () => "flaui",
            description: "UIAバックエンド (flaui | swa)");

        command.AddOption(hwndOption);
        command.AddOption(processOption);
        command.AddOption(xOption);
        command.AddOption(yOption);
        command.AddOption(backendOption);

        command.SetHandler(Execute, hwndOption, processOption, xOption, yOption, backendOption);

        return command;
    }

    private static void Execute(string? hwnd, string? process, int x, int y, string backend)
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
            var element = inspector.GetElementAtPoint(handle, x, y);

            if (element == null)
            {
                Console.Error.WriteLine("Error: 指定座標にUI要素が見つかりませんでした。");
                return;
            }

            Console.Out.WriteLine(JsonHelper.Serialize(element));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}
