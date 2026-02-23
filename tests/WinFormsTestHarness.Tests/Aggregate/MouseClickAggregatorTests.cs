using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class MouseClickAggregatorTests
{
    private MouseClickAggregator _aggregator = null!;

    [SetUp]
    public void SetUp()
    {
        _aggregator = new MouseClickAggregator(clickTimeoutMs: 300, dblclickTimeoutMs: 500);
    }

    private static string Ts(int ms) => $"2026-02-23T10:00:00.{ms:D3}Z";

    private static RawMouseEvent Mouse(string action, int ms, int sx = 100, int sy = 200, int rx = 50, int ry = 100, bool drag = false, int? delta = null) => new()
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

    [Test]
    public void LeftDown_LeftUp_100ms以内_Clickが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 100)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("Click"));
        Assert.That(results[0].Button, Is.EqualTo("Left"));
        Assert.That(results[0].Sx, Is.EqualTo(100));
        Assert.That(results[0].Sy, Is.EqualTo(200));
        Assert.That(results[0].Rx, Is.EqualTo(50));
        Assert.That(results[0].Ry, Is.EqualTo(100));
    }

    [Test]
    public void LeftDown_LeftUp_400ms超過_Clickにならない()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 400)));
        results.AddRange(_aggregator.Flush());

        // click-timeout(300ms) を超過しているので Click にならない
        Assert.That(results, Has.Count.EqualTo(0));
    }

    [Test]
    public void LeftDown_Move_Drag_LeftUp_DragAndDropが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0, sx: 100, sy: 200, rx: 50, ry: 100)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("Move", 50, sx: 150, sy: 250, rx: 100, ry: 150, drag: true)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("Move", 100, sx: 300, sy: 400, rx: 250, ry: 300, drag: true)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 150, sx: 300, sy: 400, rx: 250, ry: 300)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("DragAndDrop"));
        Assert.That(results[0].StartSx, Is.EqualTo(100));
        Assert.That(results[0].StartSy, Is.EqualTo(200));
        Assert.That(results[0].EndSx, Is.EqualTo(300));
        Assert.That(results[0].EndSy, Is.EqualTo(400));
        Assert.That(results[0].StartRx, Is.EqualTo(50));
        Assert.That(results[0].StartRy, Is.EqualTo(100));
        Assert.That(results[0].EndRx, Is.EqualTo(250));
        Assert.That(results[0].EndRy, Is.EqualTo(300));
    }

    [Test]
    public void 連続2回Click_300ms間隔_DoubleClickが生成される()
    {
        var results = new List<AggregatedAction>();

        // 1回目のクリック
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        // 2回目のクリック（300ms 間隔）
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 300)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 350)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("DoubleClick"));
        Assert.That(results[0].Button, Is.EqualTo("Left"));
    }

    [Test]
    public void 連続2回Click_600ms間隔_個別のClickが2つ生成される()
    {
        var results = new List<AggregatedAction>();

        // 1回目のクリック
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        // 2回目のクリック（600ms 間隔 > dblclick-timeout 500ms）
        // DoubleClick 待ちタイムアウトは次イベントの CheckTimeouts で発動
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 600)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 650)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Type, Is.EqualTo("Click"));
        Assert.That(results[1].Type, Is.EqualTo("Click"));
    }

    [Test]
    public void RightDown_RightUp_RightClickが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("RightDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("RightUp", 50)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("RightClick"));
        Assert.That(results[0].Sx, Is.EqualTo(100));
    }

    [Test]
    public void WheelUp_WheelScrollが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("WheelUp", 0, delta: 120)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("WheelScroll"));
        Assert.That(results[0].Direction, Is.EqualTo("Up"));
        Assert.That(results[0].Delta, Is.EqualTo(120));
        Assert.That(results[0].Count, Is.EqualTo(1));
    }

    [Test]
    public void WheelDown_WheelScrollが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("WheelDown", 0, delta: -120)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("WheelScroll"));
        Assert.That(results[0].Direction, Is.EqualTo("Down"));
    }

    [Test]
    public void Click後にタイムスタンプが離れたイベントで_Click確定される()
    {
        var results = new List<AggregatedAction>();

        // クリック
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        // 十分に離れた次のイベント（DoubleClick 待ちがタイムアウト）
        results.AddRange(_aggregator.ProcessEvent(Mouse("WheelUp", 900)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Type, Is.EqualTo("Click"));
        Assert.That(results[1].Type, Is.EqualTo("WheelScroll"));
    }

    [Test]
    public void Flush_保留中のClickが出力される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        // Flush で pendingClick が出力される
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("Click"));
    }

    [Test]
    public void LeftDown座標がClickの座標になる()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0, sx: 450, sy: 320, rx: 230, ry: 180)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50, sx: 452, sy: 322, rx: 232, ry: 182)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        // Down 時の座標がアクション座標
        Assert.That(results[0].Sx, Is.EqualTo(450));
        Assert.That(results[0].Sy, Is.EqualTo(320));
        Assert.That(results[0].Rx, Is.EqualTo(230));
        Assert.That(results[0].Ry, Is.EqualTo(180));
    }
}
