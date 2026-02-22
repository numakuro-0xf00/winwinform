using System.CommandLine;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Inspect.Commands;

var rootCommand = new RootCommand("wfth-inspect -- UIA ツリー偵察CLI");

var debugOption = CommonOptions.Debug();
var quietOption = CommonOptions.Quiet();
rootCommand.AddGlobalOption(debugOption);
rootCommand.AddGlobalOption(quietOption);

rootCommand.AddCommand(ListCommand.Create());
rootCommand.AddCommand(TreeCommand.Create());
rootCommand.AddCommand(PointCommand.Create());
rootCommand.AddCommand(WatchCommand.Create());

return await rootCommand.InvokeAsync(args);
