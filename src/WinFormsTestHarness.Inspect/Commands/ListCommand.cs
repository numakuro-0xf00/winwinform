using System.CommandLine;
using System.CommandLine.Invocation;
using WinFormsTestHarness.Common.Cli;
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

        command.SetHandler((InvocationContext ctx) =>
        {
            var backend = ctx.ParseResult.GetValueForOption(backendOption)!;
            ctx.ExitCode = Execute(backend);
        });

        return command;
    }

    private static int Execute(string backend)
    {
        try
        {
            using var inspector = InspectorFactory.Create(backend);
            var windows = inspector.ListWindows();

            foreach (var window in windows)
            {
                Console.Out.WriteLine(JsonHelper.Serialize(window));
            }

            return ExitCodes.Success;
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
