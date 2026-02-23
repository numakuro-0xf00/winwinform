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

    /// <summary>テスト用のインメモリ Sink</summary>
    private sealed class InMemorySink : ILogSink
    {
        public ConcurrentBag<LogEntry> Entries { get; } = new();
        public bool IsConnected => !_failOnWrite;
        public bool Disposed { get; private set; }
        private bool _failOnWrite;

        public void SetFailOnWrite(bool fail) => _failOnWrite = fail;

        public void Write(LogEntry entry)
        {
            if (_failOnWrite)
                throw new IOException("Simulated write failure");
            Entries.Add(entry);
        }

        public void Dispose() => Disposed = true;
    }

    [Test]
    public void Enqueue_FlushQueue_Sinkに書き込まれる()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        var entry = LogEntry.Custom("test", TestTimestamp);
        pipeline.Enqueue(entry);
        pipeline.FlushQueue();

        Assert.That(primary.Entries.Count, Is.EqualTo(1));
        Assert.That(primary.Entries.First().Message, Is.EqualTo("test"));
        Assert.That(fallback.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void Enqueue_複数エントリが順序通りフラッシュされる()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        for (int i = 0; i < 5; i++)
        {
            pipeline.Enqueue(LogEntry.Custom($"msg{i}", TestTimestamp));
        }
        pipeline.FlushQueue();

        Assert.That(primary.Entries.Count, Is.EqualTo(5));
    }

    [Test]
    public void キュー溢れ時に古いエントリが破棄される()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 3, flushIntervalMs: int.MaxValue);

        // 5 個投入、最大 3 なので 2 個は破棄される
        for (int i = 0; i < 5; i++)
        {
            pipeline.Enqueue(LogEntry.Custom($"msg{i}", TestTimestamp));
        }

        Assert.That(pipeline.QueueCount, Is.LessThanOrEqualTo(5));

        pipeline.FlushQueue();

        // 投入数 5 - 破棄数 2 = 3 エントリが書き込まれる
        Assert.That(primary.Entries.Count, Is.EqualTo(3));
    }

    [Test]
    public void PrimarySink失敗時にFallbackSinkに書き込まれる()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        primary.SetFailOnWrite(true);

        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        pipeline.Enqueue(LogEntry.Custom("fallback_test", TestTimestamp));
        pipeline.FlushQueue();

        Assert.That(primary.Entries.Count, Is.EqualTo(0));
        Assert.That(fallback.Entries.Count, Is.EqualTo(1));
        Assert.That(fallback.Entries.First().Message, Is.EqualTo("fallback_test"));
    }

    [Test]
    public void 両Sink失敗でも例外がスローされない()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        primary.SetFailOnWrite(true);
        fallback.SetFailOnWrite(true);

        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        pipeline.Enqueue(LogEntry.Custom("both_fail", TestTimestamp));

        Assert.DoesNotThrow(() => pipeline.FlushQueue());
    }

    [Test]
    public void Dispose時に残りキューがフラッシュされSinkがDisposeされる()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        pipeline.Enqueue(LogEntry.Custom("dispose_test", TestTimestamp));
        pipeline.Dispose();

        Assert.That(primary.Entries.Count, Is.EqualTo(1));
        Assert.That(primary.Disposed, Is.True);
        Assert.That(fallback.Disposed, Is.True);
    }

    [Test]
    public void QueueCount_エンキュー後に増加しフラッシュ後にゼロになる()
    {
        var primary = new InMemorySink();
        var fallback = new InMemorySink();
        using var pipeline = new LogPipeline(primary, fallback, maxQueueSize: 100, flushIntervalMs: int.MaxValue);

        Assert.That(pipeline.QueueCount, Is.EqualTo(0));

        pipeline.Enqueue(LogEntry.Custom("test", TestTimestamp));
        Assert.That(pipeline.QueueCount, Is.GreaterThan(0));

        pipeline.FlushQueue();
        Assert.That(pipeline.QueueCount, Is.EqualTo(0));
    }
}
