using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class UiaSnapshot
{
    [JsonPropertyName("ts")]
    public string Ts { get; set; } = "";

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("controlType")]
    public string? ControlType { get; set; }

    [JsonPropertyName("className")]
    public string? ClassName { get; set; }

    [JsonPropertyName("rect")]
    public UiaRectModel? Rect { get; set; }

    [JsonPropertyName("children")]
    public List<UiaNodeModel>? Children { get; set; }

    [JsonPropertyName("summary")]
    public UiaSummaryModel? Summary { get; set; }
}

public class UiaNodeModel
{
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("controlType")]
    public string? ControlType { get; set; }

    [JsonPropertyName("className")]
    public string? ClassName { get; set; }

    [JsonPropertyName("rect")]
    public UiaRectModel? Rect { get; set; }

    [JsonPropertyName("children")]
    public List<UiaNodeModel>? Children { get; set; }

    [JsonPropertyName("summary")]
    public UiaSummaryModel? Summary { get; set; }
}

public record UiaRectModel(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int W,
    [property: JsonPropertyName("h")] int H);

public class UiaSummaryModel
{
    [JsonPropertyName("rows")]
    public int? Rows { get; set; }

    [JsonPropertyName("columns")]
    public int? Columns { get; set; }
}
