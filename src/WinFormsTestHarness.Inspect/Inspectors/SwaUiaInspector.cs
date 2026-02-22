using System.Diagnostics;
using SWA = System.Windows.Automation;
using WinFormsTestHarness.Inspect.Models;

namespace WinFormsTestHarness.Inspect.Inspectors;

public class SwaUiaInspector : IUiaInspector
{
    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var root = SWA.AutomationElement.RootElement;
        var children = root.FindAll(
            SWA.TreeScope.Children,
            SWA.Condition.TrueCondition);

        var result = new List<WindowInfo>();

        foreach (SWA.AutomationElement child in children)
        {
            try
            {
                var handle = new IntPtr(child.Current.NativeWindowHandle);
                if (handle == IntPtr.Zero)
                    continue;

                var title = child.Current.Name ?? "";
                var pid = child.Current.ProcessId;

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
        var element = SWA.AutomationElement.FromHandle(hwnd);
        return BuildNode(element, 0, maxDepth);
    }

    public UiaNode? GetElementAtPoint(IntPtr hwnd, int x, int y)
    {
        try
        {
            var point = new System.Windows.Point(x, y);
            var element = SWA.AutomationElement.FromPoint(point);
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

    private UiaNode BuildNode(SWA.AutomationElement element, int currentDepth, int? maxDepth)
    {
        var node = CreateNode(element);

        var controlType = element.Current.ControlType;
        if (controlType == SWA.ControlType.DataGrid || controlType == SWA.ControlType.Table)
        {
            node.ChildrenOmitted = true;
            node.Summary = GetDataGridSummary(element);
            return node;
        }

        if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
        {
            return node;
        }

        var walker = SWA.TreeWalker.RawViewWalker;
        var child = walker.GetFirstChild(element);
        if (child != null)
        {
            node.Children = new List<UiaNode>();
            while (child != null)
            {
                try
                {
                    node.Children.Add(BuildNode(child, currentDepth + 1, maxDepth));
                }
                catch
                {
                    // Skip elements that throw
                }
                child = walker.GetNextSibling(child);
            }
        }

        return node;
    }

    private static UiaNode CreateNode(SWA.AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        return new UiaNode
        {
            AutomationId = element.Current.AutomationId ?? "",
            Name = element.Current.Name ?? "",
            ControlType = element.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
            ClassName = element.Current.ClassName ?? "",
            Rect = rect.IsEmpty
                ? null
                : new UiaRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
        };
    }

    private static DataGridSummary GetDataGridSummary(SWA.AutomationElement element)
    {
        var walker = SWA.TreeWalker.RawViewWalker;
        var child = walker.GetFirstChild(element);
        int rows = 0;
        int columns = 0;

        while (child != null)
        {
            try
            {
                var ct = child.Current.ControlType;
                if (ct == SWA.ControlType.DataItem || ct == SWA.ControlType.Custom)
                    rows++;
                else if (ct == SWA.ControlType.Header || ct == SWA.ControlType.HeaderItem)
                {
                    var headerChild = walker.GetFirstChild(child);
                    int cols = 0;
                    while (headerChild != null)
                    {
                        cols++;
                        headerChild = walker.GetNextSibling(headerChild);
                    }
                    if (cols > 0)
                        columns = cols;
                }
            }
            catch
            {
                // Skip
            }
            child = walker.GetNextSibling(child);
        }

        return new DataGridSummary(rows, columns);
    }

    private string? BuildPathFromRoot(IntPtr hwnd, SWA.AutomationElement target)
    {
        try
        {
            var walker = SWA.TreeWalker.RawViewWalker;
            var parts = new List<string>();
            var current = target;

            while (current != null)
            {
                var handle = new IntPtr(current.Current.NativeWindowHandle);
                if (handle == hwnd)
                    break;

                var ct = current.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                var name = current.Current.Name ?? "";
                var automationId = current.Current.AutomationId ?? "";

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
        // No unmanaged resources to dispose for System.Windows.Automation
    }
}
