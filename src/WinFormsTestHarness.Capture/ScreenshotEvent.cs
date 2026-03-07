using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Capture;

/// <summary>
/// NDJSON 出力用スクリーンショットイベント POCO。
/// 短縮 JSON プロパティ名を使用する。
/// </summary>
public class ScreenshotEvent
{
    [JsonPropertyName("ts")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type => "screenshot";

    [JsonPropertyName("timing")]
    public string? Timing { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("w")]
    public int Width { get; set; }

    [JsonPropertyName("h")]
    public int Height { get; set; }

    [JsonPropertyName("size")]
    public long FileSize { get; set; }

    [JsonPropertyName("diff")]
    public double? DiffRatio { get; set; }

    [JsonPropertyName("skipped")]
    public bool? Skipped { get; set; }

    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    [JsonPropertyName("reuseFrom")]
    public string? ReuseFrom { get; set; }
}
