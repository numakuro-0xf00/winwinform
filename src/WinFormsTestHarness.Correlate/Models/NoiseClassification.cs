using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class NoiseClassification
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
