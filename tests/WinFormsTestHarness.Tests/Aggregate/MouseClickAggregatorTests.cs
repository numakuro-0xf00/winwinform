using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class MouseClickAggregatorTests
{
    private const int ClickTimeoutMs = 300;
    private const int DblClickTimeoutMs = 500;

    private MouseClickAggregator _aggregator = null!;

    [SetUp]
    public void SetUp()
    {
        _aggregator = new MouseClickAggregator(
            clickTimeoutMs: ClickTimeoutMs,
            dblclickTimeoutMs: DblClickTimeoutMs);
    }

    private static string Ts(int ms)
    {
        var dt = new DateTimeOffset(2026, 2, 23, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(ms);
        return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

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
        var click = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(click.Type, Is.EqualTo("Click"));
            Assert.That(click.Button, Is.EqualTo("Left"));
            Assert.That(click.Sx, Is.EqualTo(100));
            Assert.That(click.Sy, Is.EqualTo(200));
            Assert.That(click.Rx, Is.EqualTo(50));
            Assert.That(click.Ry, Is.EqualTo(100));
        });
    }

    [Test]
    public void LeftDown_LeftUp_ちょうどClickTimeout_Clickが生成される()
    {
        // 境界値: ちょうど ClickTimeoutMs (300ms) → Click になる（<= 判定）
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", ClickTimeoutMs)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("Click"));
    }

    [Test]
    public void LeftDown_LeftUp_ClickTimeoutを1ms超過_集約されずドロップされる()
    {
        // 境界値: ClickTimeoutMs + 1 (301ms) → Click にならない（<= 判定）
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", ClickTimeoutMs + 1)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Is.Empty);
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
        var drag = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(drag.Type, Is.EqualTo("DragAndDrop"));
            Assert.That(drag.StartSx, Is.EqualTo(100));
            Assert.That(drag.StartSy, Is.EqualTo(200));
            Assert.That(drag.EndSx, Is.EqualTo(300));
            Assert.That(drag.EndSy, Is.EqualTo(400));
            Assert.That(drag.StartRx, Is.EqualTo(50));
            Assert.That(drag.StartRy, Is.EqualTo(100));
            Assert.That(drag.EndRx, Is.EqualTo(250));
            Assert.That(drag.EndRy, Is.EqualTo(300));
        });
    }

    [Test]
    public void 連続2回Click_DblClickTimeout以内_DoubleClickが生成される()
    {
        var results = new List<AggregatedAction>();

        // 1回目のクリック
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        // 2回目のクリック（DblClickTimeout 以内: 300ms < 500ms）
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 300)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 350)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Type, Is.EqualTo("DoubleClick"));
            Assert.That(results[0].Button, Is.EqualTo("Left"));
        });
    }

    [Test]
    public void 連続2回Click_ちょうどDblClickTimeout_DoubleClickが生成される()
    {
        // 境界値: ちょうど DblClickTimeoutMs (500ms) → DoubleClick になる（<= 判定）
        var results = new List<AggregatedAction>();

        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", DblClickTimeoutMs)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", DblClickTimeoutMs + 50)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("DoubleClick"));
    }

    [Test]
    public void 連続2回Click_DblClickTimeoutを1ms超過_個別のClickが2つ生成される()
    {
        // 境界値: DblClickTimeoutMs + 1 (501ms) → DoubleClick にならない
        var results = new List<AggregatedAction>();

        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", 50)));
        // 2回目のクリック（DblClickTimeout + 1ms 超過）
        // DoubleClick 待ちタイムアウトは次イベントの CheckTimeouts で発動
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", DblClickTimeoutMs + 1)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftUp", DblClickTimeoutMs + 51)));
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
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Type, Is.EqualTo("WheelScroll"));
            Assert.That(results[0].Direction, Is.EqualTo("Up"));
            Assert.That(results[0].Delta, Is.EqualTo(120));
            Assert.That(results[0].Count, Is.EqualTo(1));
        });
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
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Sx, Is.EqualTo(450));
            Assert.That(results[0].Sy, Is.EqualTo(320));
            Assert.That(results[0].Rx, Is.EqualTo(230));
            Assert.That(results[0].Ry, Is.EqualTo(180));
        });
    }

    [Test]
    public void Dragging中にRightDown_ドラッグが中断されイベントがドロップされる()
    {
        // Dragging 中の予期しないイベントでドラッグが中断され、
        // 不完全なジェスチャーとしてドロップされる
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Mouse("LeftDown", 0, sx: 100, sy: 200)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("Move", 50, sx: 200, sy: 300, drag: true)));
        results.AddRange(_aggregator.ProcessEvent(Mouse("RightDown", 100, sx: 200, sy: 300)));
        // ドラッグ中断後 Idle に戻る。RightDown は Idle の default で無視される。
        // RightUp も Idle の default で無視される。
        results.AddRange(_aggregator.ProcessEvent(Mouse("RightUp", 150, sx: 200, sy: 300)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Is.Empty);
    }
}
