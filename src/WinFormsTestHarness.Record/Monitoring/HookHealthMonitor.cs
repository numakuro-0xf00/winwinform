namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// フックの生存監視。
/// 定期的にプローブ入力を送信し、フックが応答しているか確認する。
/// </summary>
public class HookHealthMonitor
{
    private readonly ISystemClock _clock;
    private readonly IProbeInput _probe;
    private readonly TimeSpan _probeTimeout;

    private DateTime _lastActivity;
    private DateTime _lastProbe;
    private bool _probeResponseReceived;

    public HookHealthMonitor(ISystemClock clock, IProbeInput probe, TimeSpan? probeTimeout = null)
    {
        _clock = clock;
        _probe = probe;
        _probeTimeout = probeTimeout ?? TimeSpan.FromMilliseconds(500);
        _lastActivity = clock.UtcNow;
        _lastProbe = DateTime.MinValue;
        _probeResponseReceived = true;
    }

    /// <summary>
    /// フックからイベントを受信したときに呼ぶ。
    /// </summary>
    public void RecordActivity()
    {
        _lastActivity = _clock.UtcNow;
        _probeResponseReceived = true;
    }

    /// <summary>
    /// フック状態を確認する。
    /// 一定時間イベントがない場合はプローブを送信し、応答を待つ。
    /// </summary>
    public HookStatus Check()
    {
        var now = _clock.UtcNow;
        var sinceActivity = now - _lastActivity;

        // 最近イベントを受信している場合
        if (sinceActivity < _probeTimeout)
            return HookStatus.Alive;

        // プローブ未送信、または前回プローブに応答あり→新しいプローブ送信
        if (_probeResponseReceived || _lastProbe == DateTime.MinValue)
        {
            _probeResponseReceived = false;
            _lastProbe = now;
            _probe.SendProbe();
            return HookStatus.AliveIdle;
        }

        // プローブ送信済みで応答待ち中
        var sinceProbe = now - _lastProbe;
        if (sinceProbe < _probeTimeout)
            return HookStatus.AliveIdle;

        // プローブタイムアウト
        return HookStatus.PossiblyDead;
    }
}
