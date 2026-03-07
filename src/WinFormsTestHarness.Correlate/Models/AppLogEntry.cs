using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Correlate.Models;

public class AppLogEntry
{
    [JsonPropertyName("ts")]
    public string Ts { get; set; } = "";

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("control")]
    public string? Control { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("prop")]
    public string? Prop { get; set; }

    [JsonPropertyName("old")]
    public string? Old { get; set; }

    [JsonPropertyName("new")]
    public string? New { get; set; }

    [JsonPropertyName("form")]
    public string? Form { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
