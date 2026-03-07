using System.Text.Json;

namespace WinFormsTestHarness.Aggregate.Models;

/// <summary>
/// 生イベント1行を JsonDocument でパースし、ルーティング用プロパティを公開する。
/// 設計ドキュメント準拠の短いフィールド名（ts, sx, sy 等）で処理する。
/// </summary>
public class RawEvent
{
    private readonly JsonElement _root;

    public string Type { get; }
    public string Action { get; }
    public DateTimeOffset Ts { get; }
    public string TsString { get; }
    public string RawJson { get; }

    // Mouse accessors
    public int? Sx => GetInt("sx");
    public int? Sy => GetInt("sy");
    public int? Rx => GetInt("rx");
    public int? Ry => GetInt("ry");

    // Key accessors
    public int? Vk => GetInt("vk");
    public string? Key => GetString("key");
    public string? Char => GetString("char");

    private RawEvent(JsonElement root, string rawJson)
    {
        _root = root;
        RawJson = rawJson;
        Type = GetString("type") ?? "";
        Action = GetString("action") ?? "";

        TsString = GetString("ts") ?? "";
        Ts = TsString.Length > 0 ? DateTimeOffset.Parse(TsString) : default;
    }

    public static RawEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            return new RawEvent(doc.RootElement.Clone(), line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? GetString(string property)
    {
        return _root.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }

    private int? GetInt(string property)
    {
        return _root.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number
            ? val.GetInt32()
            : null;
    }
}
