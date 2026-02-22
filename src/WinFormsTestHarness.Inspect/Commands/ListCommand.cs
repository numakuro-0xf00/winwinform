using System.CommandLine;
using WinFormsTestHarness.Inspect.Helpers;

namespace WinFormsTestHarness.Inspect.Commands;

public static class ListCommand
{
    public static Command Create()
    {
        var command = new Command("list", "トップレベルウィンドウ一覧を表示");

        var backendOption = new Option<string>(
            "--backend",
            getDefaultValue: () => "flaui",
            description: "UIAバックエンド (flaui | swa)");

        command.AddOption(backendOption);

        command.SetHandler(Execute, backendOption);

        return command;
    }

    private static void Execute(string backend)
    {
        try
        {
            using var inspector = InspectorFactory.Create(backend);
            var windows = inspector.ListWindows();

            foreach (var window in windows)
            {
                Console.Out.WriteLine(JsonHelper.Serialize(window));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}
