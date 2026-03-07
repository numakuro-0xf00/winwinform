using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class CorrelatedAction
{
    [JsonPropertyName("seq")]
    public int Seq { get; set; }

    [JsonPropertyName("ts")]
    public string Ts { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("input")]
    public JsonElement Input { get; set; }

    [JsonPropertyName("target")]
    public TargetElement? Target { get; set; }

    [JsonPropertyName("screenshots")]
    public ScreenshotPaths? Screenshots { get; set; }

    [JsonPropertyName("uiaDiff")]
    public UiaDiff? UiaDiff { get; set; }

    [JsonPropertyName("appLog")]
    public List<AppLogEntry>? AppLog { get; set; }

    [JsonPropertyName("noise")]
    public NoiseClassification? Noise { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("_explain")]
    public ExplainInfo? Explain { get; set; }
}

public class ScreenshotPaths
{
    [JsonPropertyName("before")]
    public string? Before { get; set; }

    [JsonPropertyName("after")]
    public string? After { get; set; }
}

public class UiaDiff
{
    [JsonPropertyName("added")]
    public List<UiaDiffEntry> Added { get; set; } = new();

    [JsonPropertyName("removed")]
    public List<UiaDiffEntry> Removed { get; set; } = new();

    [JsonPropertyName("changed")]
    public List<UiaDiffChange> Changed { get; set; } = new();
}

public class UiaDiffEntry
{
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("controlType")]
    public string? ControlType { get; set; }
}

public class UiaDiffChange
{
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("property")]
    public string Property { get; set; } = "";

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }
}

public class ExplainInfo
{
    [JsonPropertyName("uiaMatch")]
    public string? UiaMatch { get; set; }

    [JsonPropertyName("screenshotMatch")]
    public string? ScreenshotMatch { get; set; }

    [JsonPropertyName("targetSource")]
    public string? TargetSource { get; set; }

    [JsonPropertyName("appLogMatch")]
    public string? AppLogMatch { get; set; }

    [JsonPropertyName("noiseReason")]
    public string? NoiseReason { get; set; }
}
