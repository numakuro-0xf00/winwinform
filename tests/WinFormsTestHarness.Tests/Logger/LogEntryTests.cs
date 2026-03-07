using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Logger.Models;
using WinFormsTestHarness.Logger.Sinks;

namespace WinFormsTestHarness.Tests.Logger;

[TestFixture]
public class LogEntryTests
{
    // プロダクションコードと同じ JsonSerializerOptions を使用
    private static readonly JsonSerializerOptions s_jsonOptions = JsonFileLogSink.JsonOptions;

    private const string TestTimestamp = "2026-02-23T12:00:00.000000Z";

    [Test]
    public void EventEntry_イベントログのJSON形式が正しい()
    {
        // Arrange
        var info = new ControlInfo("btnOK", "Button", "MainForm", false);

        // Act
        var entry = LogEntry.EventEntry(info, "Click", TestTimestamp);
        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.That(doc.RootElement.GetProperty("ts").GetString(), Is.EqualTo(TestTimestamp));
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("event"));
        Assert.That(doc.RootElement.GetProperty("control").GetString(), Is.EqualTo("btnOK"));
        Assert.That(doc.RootElement.GetProperty("event").GetString(), Is.EqualTo("Click"));
        Assert.That(doc.RootElement.TryGetProperty("message", out _), Is.False);
    }

    [Test]
    public void PropertyChanged_プロパティ変更ログのJSON形式が正しい()
    {
        // Arrange
        var info = new ControlInfo("txtName", "TextBox", "MainForm", false);

        // Act
        var entry = LogEntry.PropertyChanged(info, "Text", "old", "new", false, TestTimestamp);
        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("prop"));
        Assert.That(doc.RootElement.GetProperty("control").GetString(), Is.EqualTo("txtName"));
        Assert.That(doc.RootElement.GetProperty("prop").GetString(), Is.EqualTo("Text"));
        Assert.That(doc.RootElement.GetProperty("old").ToString(), Is.EqualTo("old"));
        Assert.That(doc.RootElement.GetProperty("new").ToString(), Is.EqualTo("new"));
        Assert.That(doc.RootElement.TryGetProperty("masked", out _), Is.False);
    }

    [Test]
    public void PropertyChanged_マスク有効時に値がマスクされる()
    {
        // Arrange
        var info = new ControlInfo("txtPassword", "TextBox", "MainForm", true);

        // Act
        var entry = LogEntry.PropertyChanged(info, "Text", "secret", "newsecret", true, TestTimestamp);
        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.That(doc.RootElement.GetProperty("old").ToString(), Is.EqualTo("***"));
        Assert.That(doc.RootElement.GetProperty("new").ToString(), Is.EqualTo("***"));
        Assert.That(doc.RootElement.GetProperty("masked").GetBoolean(), Is.True);
    }

    [Test]
    public void PropertyChanged_マスク無効時は値がそのまま出力される()
    {
        // Arrange: IsPasswordField=true でも masked=false なら値はマスクされない
        var info = new ControlInfo("txtPassword", "TextBox", "MainForm", true);

        // Act
        var entry = LogEntry.PropertyChanged(info, "Text", "old", "new", false, TestTimestamp);
        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        Assert.That(doc.RootElement.GetProperty("old").ToString(), Is.EqualTo("old"));
        Assert.That(doc.RootElement.GetProperty("new").ToString(), Is.EqualTo("new"));
        Assert.That(doc.RootElement.TryGetProperty("masked", out _), Is.False);
    }

    [Test]
    public void FormOpen_フォームオープンログのJSON形式が正しい()
    {
        var entry = LogEntry.FormOpen("SearchForm", "MainForm", true, TestTimestamp);

        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("form_open"));
        Assert.That(doc.RootElement.GetProperty("form").GetString(), Is.EqualTo("SearchForm"));
        Assert.That(doc.RootElement.GetProperty("owner").GetString(), Is.EqualTo("MainForm"));
        Assert.That(doc.RootElement.GetProperty("modal").GetBoolean(), Is.True);
    }

    [Test]
    public void FormClose_フォームクローズログのJSON形式が正しい()
    {
        var entry = LogEntry.FormClose("SearchForm", "OK", TestTimestamp);

        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("form_close"));
        Assert.That(doc.RootElement.GetProperty("form").GetString(), Is.EqualTo("SearchForm"));
        Assert.That(doc.RootElement.GetProperty("result").GetString(), Is.EqualTo("OK"));
    }

    [Test]
    public void Custom_カスタムメッセージログのJSON形式が正しい()
    {
        var entry = LogEntry.Custom("テスト開始", TestTimestamp);

        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("custom"));
        Assert.That(doc.RootElement.GetProperty("message").GetString(), Is.EqualTo("テスト開始"));
    }

    // --- Sanitize テスト ---

    [Test]
    public void Sanitize_nullはnullを返す()
    {
        Assert.That(LogEntry.Sanitize(null), Is.Null);
    }

    [Test]
    public void Sanitize_Delegateは型名文字列に変換される()
    {
        Action action = () => { };

        var result = LogEntry.Sanitize(action);

        Assert.That(result, Does.StartWith("<").And.EndsWith(">"));
    }

    [Test]
    public void Sanitize_Typeはフル名に変換される()
    {
        var result = LogEntry.Sanitize(typeof(string));

        Assert.That(result, Is.EqualTo("System.String"));
    }

    [Test]
    public void Sanitize_500文字ちょうどはトランケートされない()
    {
        var str500 = new string('a', 500);

        var result = LogEntry.Sanitize(str500) as string;

        Assert.That(result, Is.EqualTo(str500));
        Assert.That(result!.Length, Is.EqualTo(500));
    }

    [Test]
    public void Sanitize_501文字はトランケートされる()
    {
        var str501 = new string('a', 501);

        var result = LogEntry.Sanitize(str501) as string;

        Assert.That(result!.Length, Is.EqualTo(503)); // 500 + "..."
        Assert.That(result, Does.EndWith("..."));
    }

    [Test]
    public void Sanitize_長い文字列は500文字でトランケートされる()
    {
        var longString = new string('a', 600);

        var result = LogEntry.Sanitize(longString) as string;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Length, Is.EqualTo(503)); // 500 + "..."
        Assert.That(result, Does.EndWith("..."));
    }

    [Test]
    public void Sanitize_短い文字列はそのまま返る()
    {
        var result = LogEntry.Sanitize("hello");

        Assert.That(result, Is.EqualTo("hello"));
    }

    private sealed class NullToStringObject
    {
        public override string? ToString() => null;
    }

    [Test]
    public void Sanitize_ToStringがnullを返すオブジェクトではnullを返す()
    {
        var result = LogEntry.Sanitize(new NullToStringObject());

        Assert.That(result, Is.Null);
    }

    // --- MaskValue テスト ---

    [Test]
    public void MaskValue_nullはnullを返す()
    {
        Assert.That(LogEntry.MaskValue(null), Is.Null);
    }

    [Test]
    public void MaskValue_値はマスク文字列に変換される()
    {
        Assert.That(LogEntry.MaskValue("secret"), Is.EqualTo("***"));
        Assert.That(LogEntry.MaskValue(12345), Is.EqualTo("***"));
    }

    // --- null フィールド省略テスト ---

    [Test]
    public void FormOpen_オーナーなしの場合ownerフィールドが省略される()
    {
        var entry = LogEntry.FormOpen("MainForm", null, false, TestTimestamp);

        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("owner", out _), Is.False);
        Assert.That(doc.RootElement.GetProperty("modal").GetBoolean(), Is.False);
    }

    [Test]
    public void NullフィールドはJSONに含まれない()
    {
        var entry = LogEntry.Custom("msg", TestTimestamp);

        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("control", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("event", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("prop", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("old", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("new", out _), Is.False);
    }
}
