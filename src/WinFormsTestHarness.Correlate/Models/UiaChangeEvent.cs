namespace WinFormsTestHarness.Correlate.Models;

/// <summary>
/// wfth-inspect watch の出力行。UIA ツリースナップショットにタイムスタンプを付加した形式。
/// </summary>
public class UiaChangeEvent
{
    public string? Ts { get; set; }
    public string AutomationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ControlType { get; set; } = "";
    public string ClassName { get; set; } = "";
    public UiaRect? Rect { get; set; }
    public List<UiaChangeEvent>? Children { get; set; }
}

public record UiaRect(int X, int Y, int W, int H);
