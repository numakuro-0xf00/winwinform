using CommonJson = WinFormsTestHarness.Common.Serialization.JsonHelper;

namespace WinFormsTestHarness.Inspect.Helpers;

/// <summary>
/// Common.Serialization.JsonHelper に委譲。
/// 後方互換のため薄いラッパーとして残す。
/// </summary>
public static class JsonHelper
{
    public static string Serialize<T>(T value)
        => CommonJson.Serialize(value);
}
