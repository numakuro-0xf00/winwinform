using System.CommandLine;
using WinFormsTestHarness.Inspect.Commands;

var rootCommand = new RootCommand("wfth-inspect -- UIA ツリー偵察CLI");
rootCommand.AddCommand(ListCommand.Create());
rootCommand.AddCommand(TreeCommand.Create());
rootCommand.AddCommand(PointCommand.Create());
rootCommand.AddCommand(WatchCommand.Create());

return await rootCommand.InvokeAsync(args);
