using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class CorrelationSummary
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "summary";

    [JsonPropertyName("summaryType")]
    public string SummaryType { get; set; } = "";

    [JsonPropertyName("metrics")]
    public CorrelationMetrics Metrics { get; set; } = new();
}

public class CorrelationMetrics
{
    [JsonPropertyName("totalActions")]
    public int TotalActions { get; set; }

    [JsonPropertyName("correlatedActions")]
    public int CorrelatedActions { get; set; }

    [JsonPropertyName("noiseActions")]
    public int NoiseActions { get; set; }

    [JsonPropertyName("uiaMatchRate")]
    public double UiaMatchRate { get; set; }

    [JsonPropertyName("screenshotMatchRate")]
    public double ScreenshotMatchRate { get; set; }

    [JsonPropertyName("appLogMatchRate")]
    public double AppLogMatchRate { get; set; }
}
