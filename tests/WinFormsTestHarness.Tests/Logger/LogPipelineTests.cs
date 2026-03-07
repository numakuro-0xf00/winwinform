using System.Collections.Concurrent;
using NUnit.Framework;
using WinFormsTestHarness.Logger.Internal;
using WinFormsTestHarness.Logger.Models;
using WinFormsTestHarness.Logger.Sinks;

namespace WinFormsTestHarness.Tests.Logger;

[TestFixture]
public class LogPipelineTests
{
    private const string TestTimestamp = "2026-02-23T12:00:00.000000Z";

    /// <summary>テスト用のインメモリ Sink（ConcurrentQueue で順序保証）</summary>
    private sealed class InMemorySink : ILogSink
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public bool IsConnected => !_failOnWrite;
        public bool Disposed { get; private set; }
        private bool _failOnWrite;

        public void SetFailOnWrite(bool fail) => _failOnWrite = fail;

        public void Write(LogEntry entry)
        {
            if (_failOnWrite)
                throw new IOException("Simulated write failure");
            Entries.Enqueue(entry);
        }

        public void Dispose() => Disposed = true;
    }

    [Test]
    public void Enqueue_FlushQueue_Sinkに書き込まれる()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act
        pipeline.Enqueue(LogEntry.Custom("test", TestTimestamp));
        pipeline.FlushQueue();

        // Assert
        Assert.That(primary.Entries.Count, Is.EqualTo(1));
        Assert.That(primary.Entries.First().Message, Is.EqualTo("test"));
        Assert.That(fallback.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void Enqueue_複数エントリが投入順にフラッシュされる()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act
        for (int i = 0; i < 5; i++)
        {
            pipeline.Enqueue(LogEntry.Custom($"msg{i}", TestTimestamp));
        }
        pipeline.FlushQueue();

        // Assert
        var messages = primary.Entries.Select(e => e.Message).ToList();
        Assert.That(messages, Is.EqualTo(new[] { "msg0", "msg1", "msg2", "msg3", "msg4" }),
            "エントリは投入順にフラッシュされるべき");
    }

    [Test]
    public void キュー溢れ時に古いエントリが破棄される()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 3, flushIntervalMs: int.MaxValue);

        // Act
        for (int i = 0; i < 5; i++)
        {
            pipeline.Enqueue(LogEntry.Custom($"msg{i}", TestTimestamp));
        }

        // Assert
        Assert.That(pipeline.QueueCount, Is.LessThanOrEqualTo(3),
            "キューサイズが maxQueueSize を超えてはならない");

        pipeline.FlushQueue();

        Assert.That(primary.Entries.Count, Is.EqualTo(3),
            "maxQueueSize 分のエントリのみが書き込まれるべき");
    }

    [Test]
    public void PrimarySink失敗時にFallbackSinkに書き込まれる()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        primary.SetFailOnWrite(true);
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act
        pipeline.Enqueue(LogEntry.Custom("fallback_test", TestTimestamp));
        pipeline.FlushQueue();

        // Assert
        Assert.That(primary.Entries.Count, Is.EqualTo(0));
        Assert.That(fallback.Entries.Count, Is.EqualTo(1));
        Assert.That(fallback.Entries.First().Message, Is.EqualTo("fallback_test"));
    }

    [Test]
    public void PrimarySink復帰後にPrimaryに書き込まれる()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        primary.SetFailOnWrite(true);
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act: Primary 失敗中
        pipeline.Enqueue(LogEntry.Custom("to_fallback", TestTimestamp));
        pipeline.FlushQueue();

        // Primary 復帰
        primary.SetFailOnWrite(false);
        pipeline.Enqueue(LogEntry.Custom("to_primary", TestTimestamp));
        pipeline.FlushQueue();

        // Assert
        Assert.That(fallback.Entries.Count, Is.EqualTo(1));
        Assert.That(primary.Entries.Count, Is.EqualTo(1));
        Assert.That(primary.Entries.First().Message, Is.EqualTo("to_primary"));
    }

    [Test]
    public void 両Sink失敗でも例外がスローされない()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        primary.SetFailOnWrite(true);
        fallback.SetFailOnWrite(true);
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act
        pipeline.Enqueue(LogEntry.Custom("both_fail", TestTimestamp));

        // Assert
        Assert.DoesNotThrow(() => pipeline.FlushQueue());
    }

    [Test]
    public void Dispose時に残りキューがフラッシュされSinkがDisposeされる()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act
        pipeline.Enqueue(LogEntry.Custom("dispose_test", TestTimestamp));
        pipeline.Dispose();

        // Assert
        Assert.That(primary.Entries.Count, Is.EqualTo(1));
        Assert.That(primary.Disposed, Is.True);
        Assert.That(fallback.Disposed, Is.True);
    }

    [Test]
    public void Dispose_二重呼び出しでも例外がスローされない()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        // Act & Assert
        pipeline.Dispose();
        Assert.DoesNotThrow(() => pipeline.Dispose());
    }

    [Test]
    public void QueueCount_エンキュー後に増加しフラッシュ後にゼロになる()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        Assert.That(pipeline.QueueCount, Is.EqualTo(0));

        // Act & Assert
        pipeline.Enqueue(LogEntry.Custom("test", TestTimestamp));
        Assert.That(pipeline.QueueCount, Is.GreaterThan(0));

        pipeline.FlushQueue();
        Assert.That(pipeline.QueueCount, Is.EqualTo(0));
    }

    [Test]
    public void Timerによるバックグラウンドフラッシュが動作する()
    {
        // Arrange
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: 50);

        // Act
        pipeline.Enqueue(LogEntry.Custom("timer_test", TestTimestamp));

        // Assert: タイマーによるフラッシュを待機
        var flushed = SpinWait.SpinUntil(() => primary.Entries.Count > 0, TimeSpan.FromSeconds(5));
        Assert.That(flushed, Is.True, "タイマーによるバックグラウンドフラッシュが実行されるべき");
        Assert.That(primary.Entries.First().Message, Is.EqualTo("timer_test"));
    }
}
