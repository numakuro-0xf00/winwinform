using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Common.Serialization;

namespace WinFormsTestHarness.Tests.Capture;

[TestFixture]
public class ScreenshotEventSerializationTests
{
    [Test]
    public void JSONフィールド名が短縮名で出力される()
    {
        var evt = new ScreenshotEvent
        {
            Timestamp = "2026-03-07T12:00:00.000Z",
            Timing = "after",
            File = "0001_after.png",
            Width = 800,
            Height = 600,
            FileSize = 12345,
            DiffRatio = 0.05,
            Trigger = "mouse_down_left",
        };

        var json = JsonHelper.Serialize(evt);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("ts").GetString(), Is.EqualTo("2026-03-07T12:00:00.000Z"));
            Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("screenshot"));
            Assert.That(root.GetProperty("timing").GetString(), Is.EqualTo("after"));
            Assert.That(root.GetProperty("file").GetString(), Is.EqualTo("0001_after.png"));
            Assert.That(root.GetProperty("w").GetInt32(), Is.EqualTo(800));
            Assert.That(root.GetProperty("h").GetInt32(), Is.EqualTo(600));
            Assert.That(root.GetProperty("size").GetInt64(), Is.EqualTo(12345));
            Assert.That(root.GetProperty("diff").GetDouble(), Is.EqualTo(0.05));
            Assert.That(root.GetProperty("trigger").GetString(), Is.EqualTo("mouse_down_left"));
        });
    }

    [Test]
    public void null値は省略される()
    {
        var evt = new ScreenshotEvent
        {
            Timestamp = "2026-03-07T12:00:00.000Z",
            Width = 800,
            Height = 600,
        };

        var json = JsonHelper.Serialize(evt);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("file", out _), Is.False, "null の file は省略されるべき");
        Assert.That(root.TryGetProperty("diff", out _), Is.False, "null の diff は省略されるべき");
        Assert.That(root.TryGetProperty("skipped", out _), Is.False, "null の skipped は省略されるべき");
        Assert.That(root.TryGetProperty("trigger", out _), Is.False, "null の trigger は省略されるべき");
        Assert.That(root.TryGetProperty("reuseFrom", out _), Is.False, "null の reuseFrom は省略されるべき");
    }

    [Test]
    public void skipped時はfileが省略される()
    {
        var evt = new ScreenshotEvent
        {
            Timestamp = "2026-03-07T12:00:00.000Z",
            Width = 800,
            Height = 600,
            Skipped = true,
            // File は null のまま
        };

        var json = JsonHelper.Serialize(evt);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("skipped").GetBoolean(), Is.True);
        Assert.That(root.TryGetProperty("file", out _), Is.False);
    }
}
