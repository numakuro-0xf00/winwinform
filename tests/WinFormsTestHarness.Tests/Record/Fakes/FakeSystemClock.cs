using WinFormsTestHarness.Record.Monitoring;

namespace WinFormsTestHarness.Tests.Record.Fakes;

/// <summary>
/// テスト用 ISystemClock Fake 実装。
/// 時刻を手動で進められる。
/// </summary>
public class FakeSystemClock : ISystemClock
{
    private DateTime _now;

    public FakeSystemClock(DateTime? initial = null)
    {
        _now = initial ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public DateTime UtcNow => _now;

    /// <summary>指定時間だけ進める</summary>
    public void Advance(TimeSpan duration)
    {
        _now = _now.Add(duration);
    }

    /// <summary>指定時刻に設定する</summary>
    public void SetTime(DateTime utc)
    {
        _now = utc;
    }
}
