using System.Collections.Concurrent;
using WinFormsTestHarness.Logger.Models;
using WinFormsTestHarness.Logger.Sinks;

namespace WinFormsTestHarness.Logger.Internal;

/// <summary>
/// バックグラウンドキュー。ConcurrentQueue + Timer で UI スレッドから
/// ロックフリーにエンキューし、ThreadPool スレッドでフラッシュする。
/// </summary>
internal sealed class LogPipeline : IDisposable
{
    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private readonly ILogSink _primarySink;
    private readonly ILogSink _fallbackSink;
    private readonly int _maxQueueSize;
    private readonly System.Threading.Timer _timer;
    private int _queueCount;
    private int _flushing;

    internal LogPipeline(ILogSink primarySink, ILogSink fallbackSink, int maxQueueSize, int flushIntervalMs)
    {
        _primarySink = primarySink;
        _fallbackSink = fallbackSink;
        _maxQueueSize = maxQueueSize;
        _timer = new System.Threading.Timer(_ => FlushQueue(), null, flushIntervalMs, flushIntervalMs);
    }

    /// <summary>
    /// ログエントリをキューに投入する。lock-free, O(1)。
    /// キュー溢れ時は古いエントリを破棄する。
    /// </summary>
    internal void Enqueue(LogEntry entry)
    {
        if (Interlocked.Increment(ref _queueCount) > _maxQueueSize)
        {
            // 古いエントリを破棄
            if (_queue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _queueCount);
            }
        }
        _queue.Enqueue(entry);
    }

    /// <summary>キューの現在のサイズ（テスト用）</summary>
    internal int QueueCount => _queueCount;

    internal void FlushQueue()
    {
        // 再入防止
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0)
            return;

        try
        {
            while (_queue.TryDequeue(out var entry))
            {
                Interlocked.Decrement(ref _queueCount);
                WriteTo(entry);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private void WriteTo(LogEntry entry)
    {
        try
        {
            _primarySink.Write(entry);
        }
        catch
        {
            try
            {
                _fallbackSink.Write(entry);
            }
            catch
            {
                // No-Throw: ロガーはホストアプリをクラッシュさせない
            }
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        FlushQueue();
        _primarySink.Dispose();
        _fallbackSink.Dispose();
    }
}
