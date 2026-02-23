using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.Serialization;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class ActionBuilderTests
{
    private ActionBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _builder = new ActionBuilder(
            clickTimeoutMs: 300,
            dblclickTimeoutMs: 500,
            textTimeoutMs: 500);
    }

    private static string Ts(int ms) => $"2026-02-23T10:00:00.{ms:D3}Z";

    private static string MouseLine(string action, int ms, int sx = 100, int sy = 200, int rx = 50, int ry = 100, bool drag = false, int? delta = null)
    {
        var e = new RawMouseEvent
        {
            Ts = Ts(ms),
            Type = "mouse",
            Action = action,
            Sx = sx,
            Sy = sy,
            Rx = rx,
            Ry = ry,
            Drag = drag,
            Delta = delta,
        };
        return JsonHelper.Serialize(e);
    }

    private static string KeyLine(string action, int ms, string key, string? ch = null)
    {
        var e = new RawKeyEvent
        {
            Ts = Ts(ms),
            Type = "key",
            Action = action,
            Key = key,
            Char = ch,
        };
        return JsonHelper.Serialize(e);
    }

    private static string PassthroughLine(string type, int ms, string extra = "")
    {
        return $"{{\"ts\":\"{Ts(ms)}\",\"type\":\"{type}\"{(extra.Length > 0 ? "," + extra : "")}}}";
    }

    private List<JsonElement> RunBuilder(params string[] lines)
    {
        var input = new StringReader(string.Join("\n", lines));
        var output = new StringWriter();
        _builder.Process(input, output);

        var resultLines = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return resultLines.Select(line =>
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }).ToList();
    }

    [Test]
    public void マウスClick_集約されたClickが出力される()
    {
        var results = RunBuilder(
            MouseLine("LeftDown", 0, sx: 450, sy: 320, rx: 230, ry: 180),
            MouseLine("LeftUp", 100, sx: 450, sy: 320, rx: 230, ry: 180)
        );

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("Click"));
        Assert.That(results[0].GetProperty("button").GetString(), Is.EqualTo("Left"));
        Assert.That(results[0].GetProperty("sx").GetInt32(), Is.EqualTo(450));
    }

    [Test]
    public void キー入力_TextInputが出力される()
    {
        var results = RunBuilder(
            KeyLine("down", 0, "T", "T"),
            KeyLine("down", 50, "A", "a"),
            KeyLine("down", 100, "N", "n")
        );

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(results[0].GetProperty("text").GetString(), Is.EqualTo("Tan"));
    }

    [Test]
    public void screenshotイベントがパススルーされる()
    {
        var screenshotLine = PassthroughLine("screenshot", 50,
            "\"timing\":\"before\",\"file\":\"0001_before.png\"");

        var results = RunBuilder(screenshotLine);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("screenshot"));
        Assert.That(results[0].GetProperty("timing").GetString(), Is.EqualTo("before"));
    }

    [Test]
    public void sessionイベントがパススルーされる()
    {
        var sessionLine = PassthroughLine("session", 0,
            "\"action\":\"start\",\"process\":\"SampleApp\"");

        var results = RunBuilder(sessionLine);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("session"));
        Assert.That(results[0].GetProperty("action").GetString(), Is.EqualTo("start"));
    }

    [Test]
    public void systemイベントがパススルーされる()
    {
        var systemLine = PassthroughLine("system", 100,
            "\"action\":\"hook_lost\"");

        var results = RunBuilder(systemLine);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("system"));
    }

    [Test]
    public void windowイベントがパススルーされる()
    {
        var windowLine = PassthroughLine("window", 0,
            "\"action\":\"activated\",\"title\":\"Form1\"");

        var results = RunBuilder(windowLine);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("window"));
        Assert.That(results[0].GetProperty("action").GetString(), Is.EqualTo("activated"));
    }

    [Test]
    public void Click後にキー入力_ClickとTextInputが出力される()
    {
        var results = RunBuilder(
            MouseLine("LeftDown", 0, sx: 450, sy: 320, rx: 230, ry: 180),
            MouseLine("LeftUp", 50, sx: 450, sy: 320, rx: 230, ry: 180),
            KeyLine("down", 600, "A", "a"),
            KeyLine("down", 650, "B", "b")
        );

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("Click"));
        Assert.That(results[1].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(results[1].GetProperty("text").GetString(), Is.EqualTo("ab"));
        // キーアグリゲーターにクリック座標が伝播
        Assert.That(results[1].GetProperty("sx").GetInt32(), Is.EqualTo(450));
    }

    [Test]
    public void 混在イベント_時系列順に出力される()
    {
        var results = RunBuilder(
            PassthroughLine("session", 0, "\"action\":\"start\""),
            MouseLine("LeftDown", 100, sx: 450, sy: 320, rx: 230, ry: 180),
            MouseLine("LeftUp", 150, sx: 450, sy: 320, rx: 230, ry: 180),
            KeyLine("down", 700, "H", "h"),
            KeyLine("down", 750, "I", "i"),
            KeyLine("down", 800, "Enter"),
            PassthroughLine("session", 900, "\"action\":\"stop\"")
        );

        Assert.That(results.Count, Is.GreaterThanOrEqualTo(4));
        // session start
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("session"));
        // Click (dblclick-timeout 後に確定)
        Assert.That(results[1].GetProperty("type").GetString(), Is.EqualTo("Click"));
        // TextInput "hi"
        Assert.That(results[2].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(results[2].GetProperty("text").GetString(), Is.EqualTo("hi"));
        // SpecialKey Enter
        Assert.That(results[3].GetProperty("type").GetString(), Is.EqualTo("SpecialKey"));
    }

    [Test]
    public void 不正なNDJSON行はスキップされる()
    {
        var results = RunBuilder(
            "this is not json",
            MouseLine("LeftDown", 0),
            MouseLine("LeftUp", 50)
        );

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("Click"));
    }

    [Test]
    public void 空行はスキップされる()
    {
        var results = RunBuilder(
            "",
            MouseLine("LeftDown", 0),
            "   ",
            MouseLine("LeftUp", 50)
        );

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("Click"));
    }

    [Test]
    public void DragAndDrop_統合テスト()
    {
        var results = RunBuilder(
            MouseLine("LeftDown", 0, sx: 100, sy: 200, rx: 50, ry: 100),
            MouseLine("Move", 50, sx: 150, sy: 250, rx: 100, ry: 150, drag: true),
            MouseLine("Move", 100, sx: 300, sy: 400, rx: 250, ry: 300, drag: true),
            MouseLine("LeftUp", 150, sx: 300, sy: 400, rx: 250, ry: 300)
        );

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("DragAndDrop"));
        Assert.That(results[0].GetProperty("startSx").GetInt32(), Is.EqualTo(100));
        Assert.That(results[0].GetProperty("endSx").GetInt32(), Is.EqualTo(300));
    }

    [Test]
    public void RightClick_統合テスト()
    {
        var results = RunBuilder(
            MouseLine("RightDown", 0, sx: 200, sy: 300, rx: 100, ry: 150),
            MouseLine("RightUp", 50, sx: 200, sy: 300, rx: 100, ry: 150)
        );

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("type").GetString(), Is.EqualTo("RightClick"));
        Assert.That(results[0].GetProperty("sx").GetInt32(), Is.EqualTo(200));
    }
}
