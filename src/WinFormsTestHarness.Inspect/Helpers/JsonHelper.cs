using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Inspect.Helpers;

public static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Options);
}
