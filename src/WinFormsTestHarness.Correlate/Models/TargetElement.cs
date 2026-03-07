using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class TargetElement
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("controlType")]
    public string? ControlType { get; set; }

    [JsonPropertyName("rect")]
    public UiaRectModel? Rect { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
