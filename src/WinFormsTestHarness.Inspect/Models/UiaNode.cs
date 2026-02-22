namespace WinFormsTestHarness.Inspect.Models;

public class UiaNode
{
    public string AutomationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ControlType { get; set; } = "";
    public string ClassName { get; set; } = "";
    public UiaRect? Rect { get; set; }
    public List<UiaNode>? Children { get; set; }

    // DataGridView summary (when childrenOmitted)
    public DataGridSummary? Summary { get; set; }
    public bool? ChildrenOmitted { get; set; }

    // For point command
    public string? Path { get; set; }
}

public record UiaRect(int X, int Y, int W, int H);

public record DataGridSummary(int Rows, int Columns);
