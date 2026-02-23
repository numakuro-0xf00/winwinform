using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Logger.Models;
using WinFormsTestHarness.Logger.Sinks;

namespace WinFormsTestHarness.Tests.Logger;

[TestFixture]
public class JsonFileLogSinkTests
{
    private string _testDir = null!;
    private const string TestTimestamp = "2026-02-23T12:00:00.000000Z";

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "WinFormsTestHarness_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Write_NDJSONフォーマットでファイルに出力される()
    {
        var filePath = Path.Combine(_testDir, "test.ndjson");
        using (var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024))
        {
            sink.Write(LogEntry.Custom("message1", TestTimestamp));
            sink.Write(LogEntry.EventEntry(new ControlInfo("btn", "Button", "Form1", false), "Click", TestTimestamp));
        }

        var lines = File.ReadAllLines(filePath);
        Assert.That(lines.Length, Is.EqualTo(2));

        // 各行が有効な JSON であることを確認
        var doc1 = JsonDocument.Parse(lines[0]);
        Assert.That(doc1.RootElement.GetProperty("type").GetString(), Is.EqualTo("custom"));
        Assert.That(doc1.RootElement.GetProperty("message").GetString(), Is.EqualTo("message1"));

        var doc2 = JsonDocument.Parse(lines[1]);
        Assert.That(doc2.RootElement.GetProperty("type").GetString(), Is.EqualTo("event"));
        Assert.That(doc2.RootElement.GetProperty("control").GetString(), Is.EqualTo("btn"));
    }

    [Test]
    public void ファイルローテーション_最大サイズ超過で新ファイルに切り替わる()
    {
        var filePath = Path.Combine(_testDir, "rotate.ndjson");
        // 非常に小さい maxFileSize を設定してローテーションを発生させる
        using (var sink = new JsonFileLogSink(filePath, maxFileSize: 50))
        {
            sink.Write(LogEntry.Custom("first message that is long enough", TestTimestamp));
            sink.Write(LogEntry.Custom("second message after rotation", TestTimestamp));
        }

        // 元のファイルとローテーションされたファイルが存在するか確認
        var files = Directory.GetFiles(_testDir, "rotate*.ndjson");
        Assert.That(files.Length, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void IsConnected_作成直後はtrueでDispose後は接続なし()
    {
        var filePath = Path.Combine(_testDir, "connected.ndjson");
        var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024);

        Assert.That(sink.IsConnected, Is.True);

        sink.Dispose();
        // Dispose 後は writer が null なので IsConnected は false
        Assert.That(sink.IsConnected, Is.False);
    }

    [Test]
    public void デフォルトパス_指定なしの場合自動生成される()
    {
        // null パスで作成 → %TEMP% に自動生成されること
        using var sink = new JsonFileLogSink(null, maxFileSize: 1024 * 1024);

        Assert.That(sink.IsConnected, Is.True);
        sink.Write(LogEntry.Custom("auto_path_test", TestTimestamp));
    }

    [Test]
    public void NullフィールドがJSONに含まれない()
    {
        var filePath = Path.Combine(_testDir, "null_fields.ndjson");
        using (var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024))
        {
            sink.Write(LogEntry.Custom("test", TestTimestamp));
        }

        var json = File.ReadAllText(filePath).Trim();
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("control", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("event", out _), Is.False);
    }
}
