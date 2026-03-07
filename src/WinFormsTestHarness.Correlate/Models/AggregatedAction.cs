using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class AggregatedAction
{
    [JsonPropertyName("ts")]
    public string Ts { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("button")]
    public string? Button { get; set; }

    [JsonPropertyName("sx")]
    public int? Sx { get; set; }

    [JsonPropertyName("sy")]
    public int? Sy { get; set; }

    [JsonPropertyName("rx")]
    public int? Rx { get; set; }

    [JsonPropertyName("ry")]
    public int? Ry { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("startTs")]
    public string? StartTs { get; set; }

    [JsonPropertyName("endTs")]
    public string? EndTs { get; set; }

    [JsonPropertyName("endRx")]
    public int? EndRx { get; set; }

    [JsonPropertyName("endRy")]
    public int? EndRy { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
