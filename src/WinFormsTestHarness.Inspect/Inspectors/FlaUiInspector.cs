using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using WinFormsTestHarness.Inspect.Models;

namespace WinFormsTestHarness.Inspect.Inspectors;

public class FlaUiInspector : IUiaInspector
{
    private readonly UIA3Automation _automation;

    public FlaUiInspector()
    {
        _automation = new UIA3Automation();
    }

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var desktop = _automation.GetDesktop();
        var children = desktop.FindAllChildren();
        var result = new List<WindowInfo>();

        foreach (var child in children)
        {
            try
            {
                var handle = child.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle == IntPtr.Zero)
                    continue;

                var title = child.Properties.Name.ValueOrDefault ?? "";
                var pid = child.Properties.ProcessId.ValueOrDefault;

                string processName = "";
                try
                {
                    var proc = Process.GetProcessById(pid);
                    processName = proc.ProcessName;
                }
                catch
                {
                    // Process may have exited
                }

                result.Add(new WindowInfo(
                    Hwnd: $"0x{handle.ToInt64():X8}",
                    Title: title,
                    Process: processName,
                    Pid: pid
                ));
            }
            catch
            {
                // Skip elements that throw
            }
        }

        return result;
    }

    public UiaNode GetTree(IntPtr hwnd, int? maxDepth = null)
    {
        var element = _automation.FromHandle(hwnd);
        return BuildNode(element, 0, maxDepth);
    }

    public UiaNode? GetElementAtPoint(IntPtr hwnd, int x, int y)
    {
        try
        {
            var point = new System.Drawing.Point(x, y);
            var element = _automation.FromPoint(point);
            if (element == null)
                return null;

            var node = CreateNode(element);
            node.Path = BuildPathFromRoot(hwnd, element);
            return node;
        }
        catch
        {
            return null;
        }
    }

    private UiaNode BuildNode(AutomationElement element, int currentDepth, int? maxDepth)
    {
        var node = CreateNode(element);

        var controlType = element.Properties.ControlType.ValueOrDefault;
        if (controlType == ControlType.DataGrid || controlType == ControlType.Table)
        {
            node.ChildrenOmitted = true;
            node.Summary = GetDataGridSummary(element);
            return node;
        }

        if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
        {
            return node;
        }

        var children = element.FindAllChildren();
        if (children.Length > 0)
        {
            node.Children = new List<UiaNode>();
            foreach (var child in children)
            {
                try
                {
                    node.Children.Add(BuildNode(child, currentDepth + 1, maxDepth));
                }
                catch
                {
                    // Skip elements that throw
                }
            }
        }

        return node;
    }

    private static UiaNode CreateNode(AutomationElement element)
    {
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return new UiaNode
        {
            AutomationId = element.Properties.AutomationId.ValueOrDefault ?? "",
            Name = element.Properties.Name.ValueOrDefault ?? "",
            ControlType = element.Properties.ControlType.ValueOrDefault.ToString(),
            ClassName = element.Properties.ClassName.ValueOrDefault ?? "",
            Rect = rect.IsEmpty
                ? null
                : new UiaRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
        };
    }

    private static DataGridSummary GetDataGridSummary(AutomationElement element)
    {
        var children = element.FindAllChildren();
        int rows = 0;
        int columns = 0;

        foreach (var child in children)
        {
            try
            {
                var ct = child.Properties.ControlType.ValueOrDefault;
                if (ct == ControlType.DataItem || ct == ControlType.Custom)
                    rows++;
                else if (ct == ControlType.Header || ct == ControlType.HeaderItem)
                {
                    var headerChildren = child.FindAllChildren();
                    if (headerChildren.Length > 0)
                        columns = headerChildren.Length;
                }
            }
            catch
            {
                // Skip
            }
        }

        return new DataGridSummary(rows, columns);
    }

    private string? BuildPathFromRoot(IntPtr hwnd, AutomationElement target)
    {
        try
        {
            var walker = _automation.TreeWalkerFactory.GetRawViewWalker();
            var parts = new List<string>();
            var current = target;

            while (current != null)
            {
                var handle = current.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle == hwnd)
                    break;

                var ct = current.Properties.ControlType.ValueOrDefault.ToString();
                var name = current.Properties.Name.ValueOrDefault ?? "";
                var automationId = current.Properties.AutomationId.ValueOrDefault ?? "";

                var part = !string.IsNullOrEmpty(automationId)
                    ? $"{ct}[{automationId}]"
                    : !string.IsNullOrEmpty(name)
                        ? $"{ct}[\"{name}\"]"
                        : ct;

                parts.Add(part);
                current = walker.GetParent(current);
            }

            parts.Reverse();
            return string.Join(" > ", parts);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _automation.Dispose();
    }
}
