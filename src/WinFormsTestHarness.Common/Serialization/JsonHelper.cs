using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Common.Serialization;

/// <summary>
/// 全CLIツール共通のJSONシリアライズ設定。
/// camelCase + null省略で統一。
/// </summary>
public static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options);
}
