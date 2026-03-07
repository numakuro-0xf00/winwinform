using NUnit.Framework;
using WinFormsTestHarness.Record.Events;
using WinFormsTestHarness.Record.Queue;

namespace WinFormsTestHarness.Tests.Record.Queue;

[TestFixture]
public class EventQueueTests
{
    [Test]
    public void TryWrite_正常時はイベントをキューに追加()
    {
        var queue = new EventQueue(100);
        var evt = new KeyEvent { Action = "down", VkCode = 65, KeyName = "A" };

        Assert.That(queue.TryWrite(evt), Is.True);
        Assert.That(queue.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ReadAllAsync_書き込んだイベントを読み出せる()
    {
        var queue = new EventQueue(100);
        var evt = new KeyEvent { Action = "down", VkCode = 65, KeyName = "A" };
        queue.TryWrite(evt);
        queue.Complete();

        var results = new List<InputEvent>();
        await foreach (var e in queue.ReadAllAsync())
        {
            results.Add(e);
        }

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.InstanceOf<KeyEvent>());
    }

    [Test]
    public void TryWrite_劣化時はマウス移動をドロップ()
    {
        // 小さいキャパシティで50%を超えるまで埋める
        var queue = new EventQueue(10);
        for (int i = 0; i < 5; i++)
        {
            queue.TryWrite(new KeyEvent { Action = "down", VkCode = 65, KeyName = "A" });
        }

        // マウス移動はドロップされるべき
        var move = new MouseEvent { Action = "move", ScreenX = 100, ScreenY = 200 };
        Assert.That(queue.TryWrite(move), Is.False);

        // キーイベントは受け入れられるべき
        var key = new KeyEvent { Action = "down", VkCode = 66, KeyName = "B" };
        Assert.That(queue.TryWrite(key), Is.True);
    }

    [Test]
    public void GetAndResetDropStats_ドロップ数をカウントしリセット()
    {
        var queue = new EventQueue(10);
        // キューを50%以上埋める
        for (int i = 0; i < 5; i++)
        {
            queue.TryWrite(new KeyEvent { Action = "down", VkCode = 65, KeyName = "A" });
        }

        // マウス移動をドロップさせる
        queue.TryWrite(new MouseEvent { Action = "move", ScreenX = 0, ScreenY = 0 });
        queue.TryWrite(new MouseEvent { Action = "move", ScreenX = 1, ScreenY = 1 });

        var stats = queue.GetAndResetDropStats();
        Assert.That(stats.Mouse, Is.EqualTo(2));
        Assert.That(stats.Key, Is.EqualTo(0));

        // リセット確認
        var stats2 = queue.GetAndResetDropStats();
        Assert.That(stats2.Mouse, Is.EqualTo(0));
    }

    [Test]
    public async Task ReadAllAsync_読み出し後にCountがデクリメントされる()
    {
        var queue = new EventQueue(100);
        queue.TryWrite(new KeyEvent { Action = "down", VkCode = 65, KeyName = "A" });
        queue.TryWrite(new KeyEvent { Action = "up", VkCode = 65, KeyName = "A" });
        Assert.That(queue.Count, Is.EqualTo(2));

        queue.Complete();
        var results = new List<InputEvent>();
        await foreach (var e in queue.ReadAllAsync())
        {
            results.Add(e);
        }

        Assert.That(queue.Count, Is.EqualTo(0));
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void TryWrite_SessionEventは劣化時でも受け入れ()
    {
        var queue = new EventQueue(10);
        // キューを90%以上埋める
        for (int i = 0; i < 9; i++)
        {
            queue.TryWrite(new SessionEvent { Action = "start" });
        }

        var session = new SessionEvent { Action = "stop" };
        Assert.That(queue.TryWrite(session), Is.True);
    }
}
