using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Logger.Models;

/// <summary>
/// NDJSON ログエントリ。全フィールドを JSON プロパティ名付きで定義。
/// null フィールドはシリアライズ時に省略される。
/// </summary>
internal sealed class LogEntry
{
    [JsonPropertyName("ts")]
    public string Ts { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("control")]
    public string? Control { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("prop")]
    public string? Prop { get; set; }

    [JsonPropertyName("old")]
    public object? Old { get; set; }

    [JsonPropertyName("new")]
    public object? New { get; set; }

    [JsonPropertyName("form")]
    public string? Form { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("modal")]
    public bool? Modal { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("masked")]
    public bool? Masked { get; set; }

    [JsonPropertyName("row")]
    public int? Row { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>イベントログエントリを生成</summary>
    internal static LogEntry EventEntry(ControlInfo info, string eventName, string ts)
    {
        return new LogEntry
        {
            Ts = ts,
            Type = "event",
            Control = info.Name,
            Event = eventName,
        };
    }

    /// <summary>プロパティ変更ログエントリを生成</summary>
    internal static LogEntry PropertyChanged(ControlInfo info, string prop, object? old, object? @new, bool masked, string ts)
    {
        return new LogEntry
        {
            Ts = ts,
            Type = "prop",
            Control = info.Name,
            Prop = prop,
            Old = masked ? MaskValue(old) : Sanitize(old),
            New = masked ? MaskValue(@new) : Sanitize(@new),
            Masked = masked ? true : null,
        };
    }

    /// <summary>フォームオープンログエントリを生成</summary>
    internal static LogEntry FormOpen(string formName, string? ownerName, bool modal, string ts)
    {
        return new LogEntry
        {
            Ts = ts,
            Type = "form_open",
            Form = formName,
            Owner = ownerName,
            Modal = modal,
        };
    }

    /// <summary>フォームクローズログエントリを生成</summary>
    internal static LogEntry FormClose(string formName, string? dialogResult, string ts)
    {
        return new LogEntry
        {
            Ts = ts,
            Type = "form_close",
            Form = formName,
            Result = dialogResult,
        };
    }

    /// <summary>カスタムメッセージログエントリを生成</summary>
    internal static LogEntry Custom(string message, string ts)
    {
        return new LogEntry
        {
            Ts = ts,
            Type = "custom",
            Message = message,
        };
    }

    /// <summary>
    /// 値の安全変換。Delegate/Type は文字列に、長い文字列は 500 文字でトランケート。
    /// </summary>
    internal static object? Sanitize(object? value)
    {
        if (value is null) return null;
        if (value is Delegate d) return $"<{d.GetType().Name}>";
        if (value is Type t) return t.FullName;

        var str = value.ToString();
        if (str != null && str.Length > 500)
            return str[..500] + "...";

        return str;
    }

    /// <summary>値を *** でマスク</summary>
    internal static object? MaskValue(object? value)
    {
        if (value is null) return null;
        return "***";
    }
}
