using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Tests.Record.Events;

[TestFixture]
public class SessionEventTests
{
    [Test]
    public void SessionStart_モニター構成を含むシリアライズが正しい()
    {
        var evt = new SessionEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 0,
            Action = "start",
            TargetProcess = "notepad",
            TargetHwnd = "0x00010001",
            Monitors = new List<MonitorConfig>
            {
                new()
                {
                    Name = "DISPLAY1",
                    IsPrimary = true,
                    Bounds = new WindowRect(0, 0, 1920, 1080),
                    DpiScale = 100
                },
                new()
                {
                    Name = "DISPLAY2",
                    IsPrimary = false,
                    Bounds = new WindowRect(1920, 0, 2560, 1440),
                    DpiScale = 150
                }
            }
        };

        var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("session"));
        Assert.That(root.GetProperty("action").GetString(), Is.EqualTo("start"));
        Assert.That(root.GetProperty("targetProcess").GetString(), Is.EqualTo("notepad"));

        var monitors = root.GetProperty("monitors");
        Assert.That(monitors.GetArrayLength(), Is.EqualTo(2));
        Assert.That(monitors[0].GetProperty("name").GetString(), Is.EqualTo("DISPLAY1"));
        Assert.That(monitors[0].GetProperty("isPrimary").GetBoolean(), Is.True);
        Assert.That(monitors[1].GetProperty("dpiScale").GetInt32(), Is.EqualTo(150));
    }

    [Test]
    public void SessionStop_ドロップ統計を含むシリアライズが正しい()
    {
        var evt = new SessionEvent
        {
            Timestamp = "2025-01-01T01:00:00.000Z",
            Seq = 100,
            Action = "stop",
            TotalEvents = 99,
            Dropped = new DropStats(5, 2, 0, 0),
            Reason = "user_cancel"
        };

        var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("action").GetString(), Is.EqualTo("stop"));
        Assert.That(root.GetProperty("totalEvents").GetInt64(), Is.EqualTo(99));
        Assert.That(root.GetProperty("reason").GetString(), Is.EqualTo("user_cancel"));

        var dropped = root.GetProperty("dropped");
        Assert.That(dropped.GetProperty("mouse").GetInt64(), Is.EqualTo(5));
        Assert.That(dropped.GetProperty("key").GetInt64(), Is.EqualTo(2));
    }

    [Test]
    public void SessionStart_終了時専用フィールドはnullで省略される()
    {
        var evt = new SessionEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 0,
            Action = "start",
            TargetProcess = "notepad",
        };

        var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("totalEvents", out _), Is.False);
        Assert.That(root.TryGetProperty("dropped", out _), Is.False);
        Assert.That(root.TryGetProperty("reason", out _), Is.False);
    }
}
