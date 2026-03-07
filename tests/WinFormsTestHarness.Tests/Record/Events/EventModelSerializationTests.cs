using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Tests.Record.Events;

[TestFixture]
public class EventModelSerializationTests
{
    private static JsonElement SerializeAndParse(InputEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonHelper.Options);
        return JsonDocument.Parse(json).RootElement;
    }

    [Test]
    public void MouseEvent_シリアライズでcamelCaseになる()
    {
        var evt = new MouseEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 1,
            Action = "click",
            Button = "left",
            ScreenX = 100,
            ScreenY = 200,
            WindowX = 50,
            WindowY = 80,
            Hwnd = "0x00010001"
        };

        var root = SerializeAndParse(evt);

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("mouse"));
        Assert.That(root.GetProperty("action").GetString(), Is.EqualTo("click"));
        Assert.That(root.GetProperty("button").GetString(), Is.EqualTo("left"));
        Assert.That(root.GetProperty("screenX").GetInt32(), Is.EqualTo(100));
        Assert.That(root.GetProperty("screenY").GetInt32(), Is.EqualTo(200));
        Assert.That(root.GetProperty("windowX").GetInt32(), Is.EqualTo(50));
        Assert.That(root.GetProperty("windowY").GetInt32(), Is.EqualTo(80));
        Assert.That(root.GetProperty("hwnd").GetString(), Is.EqualTo("0x00010001"));
        Assert.That(root.GetProperty("seq").GetInt64(), Is.EqualTo(1));
    }

    [Test]
    public void MouseEvent_null値のプロパティは省略される()
    {
        var evt = new MouseEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 1,
            Action = "move",
            ScreenX = 100,
            ScreenY = 200,
        };

        var root = SerializeAndParse(evt);

        Assert.That(root.TryGetProperty("button", out _), Is.False);
        Assert.That(root.TryGetProperty("windowX", out _), Is.False);
        Assert.That(root.TryGetProperty("wheelDelta", out _), Is.False);
        Assert.That(root.TryGetProperty("hwnd", out _), Is.False);
    }

    [Test]
    public void KeyEvent_シリアライズが正しい()
    {
        var evt = new KeyEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 2,
            Action = "down",
            VkCode = 65,
            KeyName = "A",
            IsModifier = false,
            Hwnd = "0x00010001"
        };

        var root = SerializeAndParse(evt);

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("key"));
        Assert.That(root.GetProperty("action").GetString(), Is.EqualTo("down"));
        Assert.That(root.GetProperty("vkCode").GetInt32(), Is.EqualTo(65));
        Assert.That(root.GetProperty("keyName").GetString(), Is.EqualTo("A"));
        Assert.That(root.GetProperty("isModifier").GetBoolean(), Is.False);
    }

    [Test]
    public void WindowEvent_Rectを含むシリアライズが正しい()
    {
        var evt = new WindowEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 3,
            Action = "focus",
            Hwnd = "0x00010001",
            Title = "テストウィンドウ",
            Rect = new WindowRect(10, 20, 800, 600)
        };

        var root = SerializeAndParse(evt);

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("window"));
        Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("テストウィンドウ"));
        var rect = root.GetProperty("rect");
        Assert.That(rect.GetProperty("x").GetInt32(), Is.EqualTo(10));
        Assert.That(rect.GetProperty("y").GetInt32(), Is.EqualTo(20));
        Assert.That(rect.GetProperty("width").GetInt32(), Is.EqualTo(800));
        Assert.That(rect.GetProperty("height").GetInt32(), Is.EqualTo(600));
    }

    [Test]
    public void SystemEvent_シリアライズが正しい()
    {
        var evt = new SystemEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 4,
            Action = "hook_health",
            Message = "フック正常稼働中"
        };

        var root = SerializeAndParse(evt);

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("system"));
        Assert.That(root.GetProperty("action").GetString(), Is.EqualTo("hook_health"));
        Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("フック正常稼働中"));
        Assert.That(root.TryGetProperty("data", out _), Is.False);
    }

    [Test]
    public void ポリモーフィックシリアライズ_InputEvent型変数でも派生クラスの全プロパティが出力される()
    {
        InputEvent evt = new MouseEvent
        {
            Timestamp = "2025-01-01T00:00:00.000Z",
            Seq = 1,
            Action = "click",
            Button = "left",
            ScreenX = 100,
            ScreenY = 200,
        };

        var root = SerializeAndParse(evt);

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("mouse"));
        Assert.That(root.GetProperty("action").GetString(), Is.EqualTo("click"));
        Assert.That(root.GetProperty("screenX").GetInt32(), Is.EqualTo(100));
    }
}
