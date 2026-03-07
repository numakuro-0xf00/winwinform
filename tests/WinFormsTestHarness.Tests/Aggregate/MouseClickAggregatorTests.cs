using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.IO;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class MouseClickAggregatorTests
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

    private static RawEvent MouseEvent(string action, string ts,
        int sx = 100, int sy = 200, int rx = 50, int ry = 100)
    {
        var json = $"{{\"ts\":\"{ts}\",\"type\":\"mouse\",\"action\":\"{action}\",\"sx\":{sx},\"sy\":{sy},\"rx\":{rx},\"ry\":{ry}}}";
        return RawEvent.Parse(json)!;
    }

    [Test]
    public void LeftDown_LeftUp_100ms間隔_Clickを出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z"));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.100Z"));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("Click"));
        Assert.That(lines[0].GetProperty("button").GetString(), Is.EqualTo("Left"));
        Assert.That(lines[0].GetProperty("sx").GetInt32(), Is.EqualTo(100));
    }

    [Test]
    public void LeftDown_LeftUp_400ms間隔_タイムアウトで分離出力()
    {
        var agg = new MouseClickAggregator(_writer, clickTimeoutMs: 300);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z"));
        // タイムアウト超過後に次のイベントでチェック
        agg.CheckTimeout(DateTimeOffset.Parse("2026-01-01T00:00:00.400Z"));

        var lines = GetOutputLines();
        // タイムアウトで生イベントが出力される
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("mouse"));
        Assert.That(lines[0].GetProperty("action").GetString(), Is.EqualTo("LeftDown"));
    }

    [Test]
    public void 二回Click_300ms間隔_DoubleClickを出力()
    {
        var agg = new MouseClickAggregator(_writer, dblclickTimeoutMs: 500);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z"));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.050Z"));
        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.300Z"));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.350Z"));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("DoubleClick"));
        Assert.That(lines[0].GetProperty("button").GetString(), Is.EqualTo("Left"));
    }

    [Test]
    public void LeftDown_Move_LeftUp_DragAndDropを出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z", sx: 100, sy: 200));
        agg.Process(MouseEvent("Move", "2026-01-01T00:00:00.050Z", sx: 150, sy: 250));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.200Z", sx: 300, sy: 400, rx: 200, ry: 300));

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("DragAndDrop"));
        Assert.That(lines[0].GetProperty("startSx").GetInt32(), Is.EqualTo(100));
        Assert.That(lines[0].GetProperty("endSx").GetInt32(), Is.EqualTo(300));
    }

    [Test]
    public void RightDown_RightUp_RightClickを出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("RightDown", "2026-01-01T00:00:00.000Z"));
        agg.Process(MouseEvent("RightUp", "2026-01-01T00:00:00.050Z"));

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("RightClick"));
        Assert.That(lines[0].GetProperty("button").GetString(), Is.EqualTo("Right"));
    }

    [Test]
    public void WheelUp_WheelScrollを出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("WheelUp", "2026-01-01T00:00:00.000Z"));

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("WheelScroll"));
        Assert.That(lines[0].GetProperty("direction").GetString(), Is.EqualTo("Up"));
    }

    [Test]
    public void WheelDown_WheelScrollを出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("WheelDown", "2026-01-01T00:00:00.000Z"));

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("WheelScroll"));
        Assert.That(lines[0].GetProperty("direction").GetString(), Is.EqualTo("Down"));
    }

    [Test]
    public void Flush_PendingClick状態でClick出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z"));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.050Z"));

        // Flush前は出力なし（DoubleClick待ち）
        Assert.That(GetOutputLines(), Has.Count.EqualTo(0));

        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("Click"));
    }

    [Test]
    public void Flush_PendingUp状態でLeftDownを生イベントとして出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z"));

        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("action").GetString(), Is.EqualTo("LeftDown"));
    }

    [Test]
    public void Flush_Dragging状態でLeftDownを生イベントとして出力()
    {
        var agg = new MouseClickAggregator(_writer);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z"));
        agg.Process(MouseEvent("Move", "2026-01-01T00:00:00.050Z", sx: 150, sy: 250));

        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("action").GetString(), Is.EqualTo("LeftDown"));
    }

    [Test]
    public void 二回Click_座標がずれていてもDoubleClickと判定()
    {
        var agg = new MouseClickAggregator(_writer, dblclickTimeoutMs: 500);

        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.000Z", sx: 100, sy: 200));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.050Z", sx: 100, sy: 200));
        agg.Process(MouseEvent("LeftDown", "2026-01-01T00:00:00.300Z", sx: 105, sy: 203));
        agg.Process(MouseEvent("LeftUp", "2026-01-01T00:00:00.350Z", sx: 105, sy: 203));
        agg.Flush();

        var lines = GetOutputLines();
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].GetProperty("type").GetString(), Is.EqualTo("DoubleClick"));
    }
}
