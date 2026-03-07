using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.IO;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class KeySequenceAggregatorTests
{
    private StringWriter _outputBuffer = null!;
    private NdJsonWriter _writer = null!;

    [SetUp]
    public void SetUp()
    {
        _outputBuffer = new StringWriter();
        _writer = new NdJsonWriter(_outputBuffer);
    }

    [TearDown]
    public void TearDown()
    {
        _writer.Dispose();
        _outputBuffer.Dispose();
    }

    private List<JsonElement> GetOutputLines()
    {
        var text = _outputBuffer.ToString().TrimEnd();
        if (string.IsNullOrEmpty(text))
            return new List<JsonElement>();

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private static RawEvent KeyDown(string ts, int vk, string key, string? ch = null)
    {
        var charPart = ch != null ? $",\"char\":\"{ch}\"" : "";
        var json = $"{{\"ts\":\"{ts}\",\"type\":\"key\",\"action\":\"down\",\"vk\":{vk},\"key\":\"{key}\",\"scan\":0{charPart}}}";
        return RawEvent.Parse(json)!;
    }

    private static RawEvent KeyUp(string ts, int vk, string key)
    {
        var json = $"{{\"ts\":\"{ts}\",\"type\":\"key\",\"action\":\"up\",\"vk\":{vk},\"key\":\"{key}\",\"scan\":0}}";
        return RawEvent.Parse(json)!;
    }

    [Test]
    public void 連続した文字キー_50ms間隔_TextInputに集約()
    {
        var agg = new KeySequenceAggregator(_writer, textTimeoutMs: 500);

        agg.Process(KeyDown("2026-01-01T00:00:00.000Z", 84, "T", "T"));
        agg.Process(KeyDown("2026-01-01T00:00:00.050Z", 65, "A", "a"));
        agg.Process(KeyDown("2026-01-01T00:00:00.100Z", 78, "N", "n"));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(lines[0].GetProperty("text").GetString(), Is.EqualTo("Tan"));
    }

    [Test]
    public void 文字キー_その後Enter_TextInputとSpecialKeyを出力()
    {
        var agg = new KeySequenceAggregator(_writer, textTimeoutMs: 500);

        agg.Process(KeyDown("2026-01-01T00:00:00.000Z", 84, "T", "T"));
        agg.Process(KeyDown("2026-01-01T00:00:00.050Z", 65, "A", "a"));
        agg.Process(KeyDown("2026-01-01T00:00:00.200Z", 13, "Enter"));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(lines[0].GetProperty("text").GetString(), Is.EqualTo("Ta"));
        Assert.That(lines[1].GetProperty("type").GetString(), Is.EqualTo("SpecialKey"));
        Assert.That(lines[1].GetProperty("key").GetString(), Is.EqualTo("Enter"));
    }

    [Test]
    public void 修飾キーは無視される()
    {
        var agg = new KeySequenceAggregator(_writer, textTimeoutMs: 500);

        agg.Process(KeyDown("2026-01-01T00:00:00.000Z", 16, "Shift")); // Shift
        agg.Process(KeyDown("2026-01-01T00:00:00.050Z", 84, "T", "T"));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(lines[0].GetProperty("text").GetString(), Is.EqualTo("T"));
    }

    [Test]
    public void KeyUpイベントは無視される()
    {
        var agg = new KeySequenceAggregator(_writer, textTimeoutMs: 500);

        agg.Process(KeyDown("2026-01-01T00:00:00.000Z", 84, "T", "T"));
        agg.Process(KeyUp("2026-01-01T00:00:00.050Z", 84, "T"));
        agg.Process(KeyDown("2026-01-01T00:00:00.100Z", 65, "A", "a"));
        agg.Process(KeyUp("2026-01-01T00:00:00.150Z", 65, "A"));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("text").GetString(), Is.EqualTo("Ta"));
    }

    [Test]
    public void Flush_バッファ残りをTextInputとして出力()
    {
        var agg = new KeySequenceAggregator(_writer, textTimeoutMs: 500);

        agg.Process(KeyDown("2026-01-01T00:00:00.000Z", 72, "H", "H"));
        agg.Process(KeyDown("2026-01-01T00:00:00.050Z", 73, "I", "i"));

        Assert.That(GetOutputLines(), Has.Count.EqualTo(0));

        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("text").GetString(), Is.EqualTo("Hi"));
    }

    [Test]
    public void テキストタイムアウト超過でTextInputを分割出力()
    {
        var agg = new KeySequenceAggregator(_writer, textTimeoutMs: 500);

        agg.Process(KeyDown("2026-01-01T00:00:00.000Z", 72, "H", "H"));
        agg.Process(KeyDown("2026-01-01T00:00:00.050Z", 73, "I", "i"));

        // 600ms 後 → タイムアウト
        agg.CheckTimeout(DateTimeOffset.Parse("2026-01-01T00:00:00.650Z"));

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("text").GetString(), Is.EqualTo("Hi"));
    }
}
