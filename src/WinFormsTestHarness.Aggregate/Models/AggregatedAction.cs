namespace WinFormsTestHarness.Aggregate.Models;

/// <summary>
/// 集約済みアクション DTO。
/// Click, DoubleClick, RightClick, DragAndDrop, TextInput, SpecialKey, WheelScroll の
/// 全フィールドをカバーし、不要なフィールドは null（JSON 出力で省略）。
/// </summary>
public class AggregatedAction
{
    public string? Ts { get; set; }
    public string? Type { get; set; }

    // Click / DoubleClick / RightClick
    public string? Button { get; set; }

    // 座標（Click, RightClick, DoubleClick, TextInput, SpecialKey, WheelScroll）
    public int? Sx { get; set; }
    public int? Sy { get; set; }
    public int? Rx { get; set; }
    public int? Ry { get; set; }

    // TextInput
    public string? Text { get; set; }
    public string? StartTs { get; set; }
    public string? EndTs { get; set; }

    // SpecialKey
    public string? Key { get; set; }

    // DragAndDrop
    public int? StartSx { get; set; }
    public int? StartSy { get; set; }
    public int? EndSx { get; set; }
    public int? EndSy { get; set; }
    public int? StartRx { get; set; }
    public int? StartRy { get; set; }
    public int? EndRx { get; set; }
    public int? EndRy { get; set; }

    // WheelScroll
    public string? Direction { get; set; }
    public int? Delta { get; set; }
    public int? Count { get; set; }
}
