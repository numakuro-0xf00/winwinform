using System.Threading.Channels;
using WinFormsTestHarness.Record.Events;

namespace WinFormsTestHarness.Record.Queue;

/// <summary>
/// 劣化ポリシー付きイベントキュー。
/// フックスレッド→ライタースレッド間のスレッドセーフなバッファ。
/// </summary>
public class EventQueue
{
    private readonly Channel<InputEvent> _channel;
    private readonly QueueDegradationPolicy _policy;
    private int _currentCount;

    private long _droppedMouse;
    private long _droppedKey;
    private long _droppedWindow;
    private long _droppedSystem;

    public EventQueue(int capacity = 10000)
    {
        _channel = Channel.CreateBounded<InputEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _policy = new QueueDegradationPolicy(capacity);
    }

    /// <summary>
    /// イベントをキューに書き込む。劣化ポリシーに基づきドロップされる場合がある。
    /// フックスレッドから呼ばれる。
    /// </summary>
    public bool TryWrite(InputEvent evt)
    {
        if (!_policy.ShouldAccept(evt, _currentCount))
        {
            IncrementDropCount(evt);
            return false;
        }

        if (_channel.Writer.TryWrite(evt))
        {
            Interlocked.Increment(ref _currentCount);
            return true;
        }

        IncrementDropCount(evt);
        return false;
    }

    /// <summary>
    /// キューからイベントを非同期で読み出す。
    /// ライタースレッドから呼ばれる。
    /// </summary>
    public async IAsyncEnumerable<InputEvent> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _currentCount);
            yield return evt;
        }
    }

    /// <summary>ライター完了を通知する</summary>
    public void Complete() => _channel.Writer.Complete();

    /// <summary>ドロップ統計を取得しリセットする</summary>
    public DropStats GetAndResetDropStats()
    {
        var stats = new DropStats(
            Interlocked.Exchange(ref _droppedMouse, 0),
            Interlocked.Exchange(ref _droppedKey, 0),
            Interlocked.Exchange(ref _droppedWindow, 0),
            Interlocked.Exchange(ref _droppedSystem, 0)
        );
        return stats;
    }

    /// <summary>現在のキューサイズ</summary>
    public int Count => _currentCount;

    private void IncrementDropCount(InputEvent evt)
    {
        var category = QueueDegradationPolicy.Classify(evt);
        switch (category)
        {
            case EventCategory.MouseMove:
            case EventCategory.MouseAction:
                Interlocked.Increment(ref _droppedMouse);
                break;
            case EventCategory.Key:
                Interlocked.Increment(ref _droppedKey);
                break;
            case EventCategory.Window:
                Interlocked.Increment(ref _droppedWindow);
                break;
            default:
                Interlocked.Increment(ref _droppedSystem);
                break;
        }
    }
}
