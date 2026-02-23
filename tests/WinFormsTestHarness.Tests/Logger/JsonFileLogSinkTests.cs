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
        // Arrange
        var filePath = Path.Combine(_testDir, "test.ndjson");

        // Act
        using (var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024))
        {
            sink.Write(LogEntry.Custom("message1", TestTimestamp));
            sink.Write(LogEntry.EventEntry(new ControlInfo("btn", "Button", "Form1", false), "Click", TestTimestamp));
        }

        // Assert
        var lines = File.ReadAllLines(filePath);
        Assert.That(lines.Length, Is.EqualTo(2));

        var doc1 = JsonDocument.Parse(lines[0]);
        Assert.That(doc1.RootElement.GetProperty("type").GetString(), Is.EqualTo("custom"));
        Assert.That(doc1.RootElement.GetProperty("message").GetString(), Is.EqualTo("message1"));

        var doc2 = JsonDocument.Parse(lines[1]);
        Assert.That(doc2.RootElement.GetProperty("type").GetString(), Is.EqualTo("event"));
        Assert.That(doc2.RootElement.GetProperty("control").GetString(), Is.EqualTo("btn"));
    }

    [Test]
    public void ファイルローテーション_最大サイズ超過でデータロストなく新ファイルに切り替わる()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "rotate.ndjson");

        // Act
        using (var sink = new JsonFileLogSink(filePath, maxFileSize: 50))
        {
            sink.Write(LogEntry.Custom("first", TestTimestamp));
            sink.Write(LogEntry.Custom("second", TestTimestamp));
        }

        // Assert
        var files = Directory.GetFiles(_testDir, "rotate*.ndjson").OrderBy(f => f).ToArray();
        Assert.That(files.Length, Is.GreaterThanOrEqualTo(2),
            "ローテーションにより複数ファイルが存在すべき");

        var totalLines = files.Sum(f => File.ReadAllLines(f).Length);
        Assert.That(totalLines, Is.EqualTo(2),
            "ローテーション後もデータが失われてはならない");
    }

    [Test]
    public void IsConnected_作成直後はtrueでDispose後はfalse()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "connected.ndjson");
        var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024);

        // Assert
        Assert.That(sink.IsConnected, Is.True);

        // Act
        sink.Dispose();

        // Assert
        Assert.That(sink.IsConnected, Is.False);
    }

    [Test]
    public void デフォルトパス_指定なしの場合自動生成される()
    {
        // Arrange & Act
        string? generatedPath;
        using (var sink = new JsonFileLogSink(null, maxFileSize: 1024 * 1024))
        {
            Assert.That(sink.IsConnected, Is.True);
            sink.Write(LogEntry.Custom("auto_path_test", TestTimestamp));
            generatedPath = sink.CurrentFilePath;
        }

        // TearDown: 自動生成されたファイルをクリーンアップ
        if (generatedPath != null && File.Exists(generatedPath))
        {
            var dir = Path.GetDirectoryName(generatedPath);
            File.Delete(generatedPath);
            if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }

    [Test]
    public void NullフィールドがJSONに含まれない()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "null_fields.ndjson");

        // Act
        using (var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024))
        {
            sink.Write(LogEntry.Custom("test", TestTimestamp));
        }

        // Assert
        var json = File.ReadAllText(filePath).Trim();
        var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("control", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("event", out _), Is.False);
    }

    [Test]
    public void Dispose後のWriteは例外をスローしない()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "disposed.ndjson");
        var sink = new JsonFileLogSink(filePath, maxFileSize: 1024 * 1024);
        sink.Dispose();

        // Act & Assert
        Assert.DoesNotThrow(() => sink.Write(LogEntry.Custom("after_dispose", TestTimestamp)));
    }
}
